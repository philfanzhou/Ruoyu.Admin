using System.Text;
using Npgsql;

namespace Ruoyu.Admin.LocalRegressionStubs;

/// <summary>
/// 独立种子脚本：直写本地 PostgreSQL（ruoyu_admin 的 OssAuditRuns / OssAuditRecords）、
/// OSS_LOCAL_PATH 本地对象目录，以及替身（serve 模式）读取的夹具 JSON。
///
/// 安全边界（与 #26 一致，不可放宽）：
/// - 仅在 USE_LOCAL_OSS=1 时执行；否则拒绝（此时 Admin.WebApi 解析到的是 S3OssService）。
/// - 任何文件写入只允许落在解析后的本地 OSS 目录内。
/// - 不读取、不写入任何真实下游凭据；数据库连接串只来自命令行或环境变量。
/// </summary>
internal static class RegressionSeeder
{
    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string?> options)
    {
        if (Environment.GetEnvironmentVariable("USE_LOCAL_OSS") != "1")
        {
            Console.Error.WriteLine(
                "拒绝执行：USE_LOCAL_OSS 未设置为 1。种子脚本只允许写入本地对象目录（LocalFileOssService）；" +
                "当前配置会解析到 S3OssService，可能指向真实对象存储。请先 `export USE_LOCAL_OSS=1`。");
            return 2;
        }

        var ossRoot = ResolveOption(options, "oss-root")
            ?? Environment.GetEnvironmentVariable("OSS_LOCAL_PATH")
            ?? "data/oss";
        var fixturesPath = ResolveOption(options, "fixtures")
            ?? Environment.GetEnvironmentVariable("LOCAL_REGRESSION_FIXTURES")
            ?? "data/regression/fixtures.json";
        var connectionString = ResolveOption(options, "connection-string")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__AuditDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine(
                "拒绝执行：缺少数据库连接串。请通过 --connection-string 或环境变量 ConnectionStrings__AuditDb " +
                "提供本地 ruoyu_admin 的连接串（appsettings.json 的兜底串硬编码 Username=phil，不能假设适用于本机）。");
            return 2;
        }

        var rootFullPath = Path.GetFullPath(ossRoot);
        GuardLocalOssRoot(rootFullPath);

        // 1. Fixtures JSON shared with serve mode.
        RegressionFixtures.WriteFile(fixturesPath);
        Console.WriteLine($"已写入夹具 JSON: {Path.GetFullPath(fixturesPath)}");

        // 2. Local OSS objects (originals + thumbnails), content fixed for idempotency.
        var content = RegressionFixtures.ObjectContent();
        foreach (var objectPath in RegressionFixtures.AllObjectPaths)
        {
            WriteObject(rootFullPath, objectPath, content);
            var stem = Path.GetFileNameWithoutExtension(objectPath);
            var dir = Path.GetDirectoryName(objectPath)!.Replace('\\', '/');
            foreach (var suffix in RegressionFixtures.ThumbnailSuffixes)
            {
                var thumbnailPath = string.IsNullOrEmpty(dir)
                    ? $"{stem}_{suffix}.jpg"
                    : $"{dir}/{stem}_{suffix}.jpg";
                WriteObject(rootFullPath, thumbnailPath, content);
            }
        }
        Console.WriteLine($"已写入本地对象（原图 + 缩略图）: {rootFullPath}");

        // 3. Database rows.
        try
        {
            await SeedDatabaseAsync(connectionString);
        }
        catch (Exception ex) when (ex is NpgsqlException or PostgresException or InvalidOperationException)
        {
            Console.Error.WriteLine($"数据库种子失败：{ex.Message}");
            Console.Error.WriteLine(
                "请确认本地 PostgreSQL 已启动、连接串正确；若表不存在，先启动一次 Admin.WebApi 由 DatabaseInitializer 建表。");
            return 2;
        }
        Console.WriteLine("种子完成。重复执行结果一致（按 ObjectPath / TriggerType 幂等覆盖）。");
        return 0;
    }

    private static string? ResolveOption(IReadOnlyDictionary<string, string?> options, string name)
        => options.TryGetValue(name, out var value) ? value : null;

    private static void GuardLocalOssRoot(string rootFullPath)
    {
        // Refuse degenerate roots (filesystem root or a home directory itself): a seed that
        // sweeps files into such a location is almost certainly a misconfiguration.
        var root = Path.GetPathRoot(rootFullPath);
        if (string.Equals(rootFullPath.TrimEnd('/'), root?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝执行：OSS_LOCAL_PATH 解析为文件系统根目录（{rootFullPath}）。");
        }
    }

    private static void WriteObject(string rootFullPath, string objectPath, byte[] content)
    {
        var normalized = objectPath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(".."))
        {
            throw new InvalidOperationException($"夹具对象路径包含 ..，拒绝写入: {objectPath}");
        }
        var fullPath = Path.GetFullPath(Path.Combine(rootFullPath, normalized));
        if (!fullPath.StartsWith(rootFullPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"夹具对象路径逃逸出本地 OSS 目录，拒绝写入: {objectPath}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
    }

    private static async Task SeedDatabaseAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        await EnsureDatabaseExistsAsync(builder);

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await EnsureTablesExistAsync(connection);

        await using var transaction = await connection.BeginTransactionAsync();

        // Idempotency: wipe exactly the seed-owned rows (by ObjectPath / TriggerType), then
        // re-insert fixed rows. Row ids change between runs; the observable fixture state
        // converges to the same content, which is what the regression recipe relies on.
        var recordPaths = RegressionFixtures.PendingRecords
            .Select(r => r.Path)
            .Append(RegressionFixtures.IgnoredRecord.Path)
            .ToArray();

        await using (var deleteRecords = new NpgsqlCommand(
            "DELETE FROM \"OssAuditRecords\" WHERE \"ObjectPath\" = ANY(@paths)", connection, transaction))
        {
            deleteRecords.Parameters.AddWithValue("paths", recordPaths);
            await deleteRecords.ExecuteNonQueryAsync();
        }

        foreach (var (path, bucket) in RegressionFixtures.PendingRecords)
        {
            await InsertRecordAsync(connection, transaction, path, bucket, status: 0, note: null);
        }
        var ignored = RegressionFixtures.IgnoredRecord;
        await InsertRecordAsync(connection, transaction, ignored.Path, ignored.Bucket, status: 2, note: ignored.Note);

        await using (var deleteRuns = new NpgsqlCommand(
            "DELETE FROM \"OssAuditRuns\" WHERE \"TriggerType\" = @trigger", connection, transaction))
        {
            deleteRuns.Parameters.AddWithValue("trigger", RegressionFixtures.SeedTriggerType);
            await deleteRuns.ExecuteNonQueryAsync();
        }

        await using (var insertRun = new NpgsqlCommand(
            """
            INSERT INTO "OssAuditRuns"
                ("StartedAt", "CompletedAt", "Status", "NewZombieCount", "TriggerType", "ErrorMessage")
            VALUES (@startedAt, @completedAt, @status, @newZombieCount, @trigger, NULL)
            """,
            connection, transaction))
        {
            insertRun.Parameters.AddWithValue("startedAt", RegressionFixtures.FixedEpochSeconds - 1000);
            insertRun.Parameters.AddWithValue("completedAt", RegressionFixtures.FixedEpochSeconds - 400);
            insertRun.Parameters.AddWithValue("status", 1);
            insertRun.Parameters.AddWithValue("newZombieCount", RegressionFixtures.PendingRecords.Count);
            insertRun.Parameters.AddWithValue("trigger", RegressionFixtures.SeedTriggerType);
            await insertRun.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task InsertRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string path,
        string bucket,
        int status,
        string? note)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "OssAuditRecords"
                ("ObjectPath", "Bucket", "Size", "LastModified", "Status", "CreatedAt", "ResolvedAt", "Note")
            VALUES (@path, @bucket, @size, @lastModified, @status, @createdAt, @resolvedAt, @note)
            """,
            connection, transaction);
        command.Parameters.AddWithValue("path", path);
        command.Parameters.AddWithValue("bucket", bucket);
        command.Parameters.AddWithValue("size", RegressionFixtures.ObjectContent().LongLength);
        command.Parameters.AddWithValue("lastModified", RegressionFixtures.FixedEpochSeconds);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("createdAt", RegressionFixtures.FixedEpochSeconds);
        command.Parameters.AddWithValue("resolvedAt", status == 0 ? (object)DBNull.Value : RegressionFixtures.FixedEpochSeconds);
        command.Parameters.AddWithValue("note", note is null ? DBNull.Value : note);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureDatabaseExistsAsync(NpgsqlConnectionStringBuilder builder)
    {
        var databaseName = builder.Database
            ?? throw new InvalidOperationException("连接串缺少 Database，拒绝执行。");
        var adminBuilder = new NpgsqlConnectionStringBuilder(builder.ConnectionString) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();

        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", admin);
        exists.Parameters.AddWithValue("name", databaseName);
        var found = await exists.ExecuteScalarAsync();
        if (found is not null)
        {
            return;
        }

        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE {QuoteIdentifier(databaseName)}", admin);
        await create.ExecuteNonQueryAsync();
        Console.WriteLine($"已创建数据库 {databaseName}（建表仍由 Admin.WebApi 的 DatabaseInitializer 负责）。");
    }

    private static string QuoteIdentifier(string name)
        => new StringBuilder("\"").Append(name.Replace("\"", "\"\"")).Append('"').ToString();

    private static async Task EnsureTablesExistAsync(NpgsqlConnection connection)
    {
        foreach (var table in new[] { "OssAuditRuns", "OssAuditRecords" })
        {
            await using var command = new NpgsqlCommand(
                "SELECT to_regclass(@table) IS NOT NULL", connection);
            command.Parameters.AddWithValue("table", $"public.\"{table}\"");
            var exists = await command.ExecuteScalarAsync();
            if (exists is not true)
            {
                throw new InvalidOperationException(
                    $"表 {table} 不存在。建表由 Admin.WebApi 的 DatabaseInitializer 负责：" +
                    "请先以本地配置启动一次 Admin.WebApi（它会执行 CREATE TABLE IF NOT EXISTS），再运行种子。");
            }
        }
    }
}
