using System.Data.Common;
using System.Net;
using Admin.WebApi.Database;
using Admin.WebApi.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ServiceMantle;
using ServiceMantle.Bootstrap;
using ServiceMantle.Database.PostgreSql.Migration;
using ServiceMantle.Migration;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

/// <summary>
/// Database migration integration tests for issue #57: the InitialCreate baseline reproduces
/// the retired inline DDL column by column (verified against a golden reference database
/// created by that exact DDL), a full legacy database is taken over with its data preserved and
/// only the history stamped, unknown structures and too-new histories are refused with zero
/// writes, concurrent instances execute exactly once under the real advisory lock, executor
/// failures and cancellations never fake success, and the real Program.cs host refuses to start
/// on a missing database by default, creates it only under Database:AllowCreate=true, and
/// surfaces orchestration failures as startup failures.
/// </summary>
[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed class AuditMigrationTests : ServiceMantleIntegrationTestBase, IAsyncLifetime
{
    private const string LegacyDdl = """
        CREATE TABLE IF NOT EXISTS "OssAuditRuns" (
            "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "StartedAt" bigint NOT NULL,
            "CompletedAt" bigint NULL,
            "Status" integer NOT NULL DEFAULT 0,
            "NewZombieCount" integer NOT NULL DEFAULT 0,
            "TriggerType" text NOT NULL DEFAULT 'scheduled',
            "ErrorMessage" text NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_OssAuditRuns_StartedAt" ON "OssAuditRuns" ("StartedAt");
        CREATE TABLE IF NOT EXISTS "OssAuditRecords" (
            "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "ObjectPath" text NOT NULL,
            "Bucket" text NOT NULL,
            "Size" bigint NOT NULL DEFAULT 0,
            "LastModified" bigint NOT NULL DEFAULT 0,
            "Status" integer NOT NULL DEFAULT 0,
            "CreatedAt" bigint NOT NULL DEFAULT 0,
            "ResolvedAt" bigint NULL,
            "Note" text NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_OssAuditRecords_ObjectPath" ON "OssAuditRecords" ("ObjectPath");
        CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Status" ON "OssAuditRecords" ("Status");
        CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Bucket" ON "OssAuditRecords" ("Bucket");
        CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_CreatedAt" ON "OssAuditRecords" ("CreatedAt");
        """;

    private string _database = null!;

    public AuditMigrationTests(PostgreSqlFixture database) : base(database)
    {
    }

    public async Task InitializeAsync()
    {
        _database = $"audit_mig_{Guid.NewGuid():N}";
        await ExecuteAsync(MaintenanceConnectionString, $"CREATE DATABASE \"{_database}\"");
    }

    public async Task DisposeAsync()
    {
        // The EF contexts used by the test are pooled; FORCE closes their sessions so the drop
        // never fails on "database is being accessed by other users".
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(
            MaintenanceConnectionString, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    private string TargetConnectionString => WithDatabase(Database.ConnectionString, _database);

    private string MaintenanceConnectionString =>
        WithDatabase(Database.ConnectionString, "postgres");

    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder["Database"] = database;
        return builder.ConnectionString;
    }

    private AuditDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(TargetConnectionString)
            .Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountRowsAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relname = $1)
            """;
        command.Parameters.AddWithValue(table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ReadHistoryAsync(string connectionString)
    {
        var history = new List<string>();
        if (!await TableExistsAsync(connectionString, "__EFMigrationsHistory"))
        {
            return history;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            history.Add(reader.GetString(0));
        }

        return history;
    }

    /// <summary>
    /// The full structure fingerprint of the two business tables: every column (name, type,
    /// nullability, identity kind, default expression), every index (name, columns,
    /// uniqueness), and the primary keys. Comparing two fingerprints is the column-by-column
    /// equivalence check between the migration baseline and the retired inline DDL.
    /// </summary>
    private static async Task<List<string>> ReadStructureFingerprintAsync(string connectionString)
    {
        var fingerprint = new List<string>();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using (var columns = connection.CreateCommand())
            {
                columns.CommandText = """
                    SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull,
                           a.attidentity, COALESCE(pg_get_expr(ad.adbin, ad.adrelid), '')
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_attribute a ON a.attrelid = c.oid
                    LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                    WHERE n.nspname = 'public' AND c.relkind = 'r'
                      AND c.relname IN ('OssAuditRecords', 'OssAuditRuns')
                    ORDER BY c.relname, a.attnum
                    """;
                await using var reader = await columns.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    fingerprint.Add(
                        $"column|{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|" +
                        $"notnull={reader.GetBoolean(3)}|identity={reader.GetChar(4)}|" +
                        $"default={reader.GetString(5)}");
                }
            }

            await using (var indexes = connection.CreateCommand())
            {
                indexes.CommandText = """
                    SELECT c.relname, ic.relname, i.indisunique,
                           (SELECT array_agg(a.attname ORDER BY k.ord)
                              FROM unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord)
                              JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum)
                    FROM pg_index i
                    JOIN pg_class ic ON ic.oid = i.indexrelid
                    JOIN pg_class c ON c.oid = i.indrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public' AND c.relname IN ('OssAuditRecords', 'OssAuditRuns')
                    ORDER BY c.relname, ic.relname
                    """;
                await using var reader = await indexes.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    fingerprint.Add(
                        $"index|{reader.GetString(0)}|{reader.GetString(1)}|" +
                        $"unique={reader.GetBoolean(2)}|" +
                        $"columns={string.Join(",", reader.GetFieldValue<string[]>(3))}");
                }
            }
        }

        return fingerprint;
    }

    private DatabaseMigrationOrchestrator CreateOrchestrator(IDatabaseMigrationExecutor executor) =>
        new(
            executor,
            new DatabaseMigrationLockProviderRegistry(
                [new PostgreSqlMigrationLockProvider()],
                DatabaseProviderIdResolver.Empty));

    private ServiceId MigrationServiceId => ServiceId.Parse("ruoyu-admin");

    private BootstrapDatabaseConfiguration Target => new(
        WellKnownDatabaseProviderIds.PostgreSql,
        serverVersion: null,
        TargetConnectionString);

    // ---------- baseline structure equals the retired inline DDL ----------

    [Fact]
    public async Task EmptyDatabase_AppliesKnownChain_StructureMatchesLegacyPlusReferenceColumns()
    {
        using (var context = CreateContext())
        {
            await new AuditMigrationExecutor(context, NullLogger.Instance).ExecuteAsync();
        }

        // The golden reference: the exact retired inline DDL from Program.cs applied to a
        // second database in the same server.
        var reference = $"audit_mig_ref_{Guid.NewGuid():N}";
        await ExecuteAsync(MaintenanceConnectionString, $"CREATE DATABASE \"{reference}\"");
        try
        {
            var referenceConnection = WithDatabase(Database.ConnectionString, reference);
            await ExecuteAsync(referenceConnection, LegacyDdl);
            await ExecuteAsync(referenceConnection, """
                ALTER TABLE "OssAuditRuns" ADD COLUMN "ReferenceContractVersion" text NULL;
                ALTER TABLE "OssAuditRuns" ADD COLUMN "ReferenceSnapshots" text NULL;
                """);

            var migrated = await ReadStructureFingerprintAsync(TargetConnectionString);
            var legacy = await ReadStructureFingerprintAsync(referenceConnection);

            migrated.Should().Equal(legacy,
                "the InitialCreate baseline must reproduce the retired inline DDL column by column " +
                "(types, nullability, identity kind, defaults, indexes, primary keys)");

            // And the baseline is registered exactly once.
            (await ReadHistoryAsync(TargetConnectionString)).Should()
                .Equal(AuditMigrationExecutor.KnownMigrationIds);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(
                MaintenanceConnectionString, $"DROP DATABASE IF EXISTS \"{reference}\" WITH (FORCE)");
        }
    }

    // ---------- legacy takeover ----------

    [Fact]
    public async Task FullLegacyDatabase_TakenOver_HistoryStamped_DataPreserved()
    {
        await ExecuteAsync(TargetConnectionString, LegacyDdl);
        await ExecuteAsync(
            TargetConnectionString,
            """
            INSERT INTO "OssAuditRecords"
                ("ObjectPath", "Bucket", "Size", "LastModified", "Status", "CreatedAt", "Note")
            VALUES ('uploads/a.png', 'ruoyu-study', 123, 456, 0, 789, 'kept');

            INSERT INTO "OssAuditRuns" ("StartedAt", "CompletedAt", "Status", "NewZombieCount", "TriggerType")
            VALUES (111, 222, 1, 5, 'manual');
            """);

        using (var context = CreateContext())
        {
            var executor = new AuditMigrationExecutor(context, NullLogger.Instance);
            (await executor.InspectAsync()).Should()
                .Be(AuditMigrationExecutor.DatabaseState.LegacyTakeoverRequired);
        }

        using (var context = CreateContext())
        {
            var result = await CreateOrchestrator(
                    new AuditMigrationExecutor(context, NullLogger.Instance))
                .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));

            result.Succeeded.Should().BeTrue();
            result.ExecutorWasCalled.Should().BeTrue();
        }

        // The old baseline is stamped then upgraded; the business rows survive untouched.
        (await ReadHistoryAsync(TargetConnectionString)).Should()
            .Equal(AuditMigrationExecutor.KnownMigrationIds);
        (await CountRowsAsync(TargetConnectionString, "OssAuditRecords")).Should().Be(1);
        (await CountRowsAsync(TargetConnectionString, "OssAuditRuns")).Should().Be(1);
        await using (var connection = new NpgsqlConnection(TargetConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "ObjectPath", "Bucket", "Size", "Note", "Id" FROM "OssAuditRecords" LIMIT 1
                """;
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            reader.GetString(0).Should().Be("uploads/a.png");
            reader.GetString(1).Should().Be("ruoyu-study");
            reader.GetInt64(2).Should().Be(123);
            reader.GetString(3).Should().Be("kept");
            reader.GetInt64(4).Should().Be(1, "the legacy identity sequence continues to work");
        }

        // A second orchestration observes CurrentVersionCompatible and skips execution.
        using (var context = CreateContext())
        {
            var second = await CreateOrchestrator(
                    new AuditMigrationExecutor(context, NullLogger.Instance))
                .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));
            second.Succeeded.Should().BeTrue();
            second.ExecutorWasCalled.Should().BeFalse();
        }
    }

    [Fact]
    public async Task LegacyDatabase_MissingIndex_BackfilledDuringTakeover()
    {
        // A legacy database that predates one of the inline indexes: still takeable, the index
        // is backfilled idempotently before the history stamp.
        await ExecuteAsync(
            TargetConnectionString,
            LegacyDdl.Replace(
                """CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Bucket" ON "OssAuditRecords" ("Bucket");""", ""));

        using var context = CreateContext();
        var result = await CreateOrchestrator(
                new AuditMigrationExecutor(context, NullLogger.Instance))
            .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));

        result.Succeeded.Should().BeTrue();
        result.ExecutorWasCalled.Should().BeTrue();
        (await ReadHistoryAsync(TargetConnectionString)).Should()
            .Equal(AuditMigrationExecutor.KnownMigrationIds);
    }

    [Fact]
    public async Task ExactOldKnownPrefix_UpgradesWithoutRequiringNewColumnsAndRetainsRows()
    {
        using (var context = CreateContext())
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(AuditMigrationExecutor.InitialCreateMigrationId);
        await ExecuteAsync(TargetConnectionString, """
            INSERT INTO "OssAuditRecords" ("ObjectPath","Bucket","Status") VALUES ('uploads/history.jpg','uploads',2);
            INSERT INTO "OssAuditRuns" ("StartedAt","Status") VALUES (1,1);
            """);
        using (var context = CreateContext())
        {
            var executor = new AuditMigrationExecutor(context, NullLogger.Instance);
            (await executor.InspectAsync()).Should().Be(AuditMigrationExecutor.DatabaseState.KnownUpgradeRequired);
            await executor.ExecuteAsync();
        }
        (await ReadHistoryAsync(TargetConnectionString)).Should().Equal(AuditMigrationExecutor.KnownMigrationIds);
        using (var context = CreateContext())
        {
            (await context.OssAuditRecords.SingleAsync()).Status.Should().Be(2);
            var run = await context.OssAuditRuns.SingleAsync();
            run.ReferenceSnapshots.Should().BeNull();
            run.ReferenceContractVersion.Should().BeNull();
        }
    }

    [Fact]
    public async Task NewHistoryWithoutBaseline_IsRefusedAndNonemptyObservationDownCannotLoseEvidence()
    {
        using (var context = CreateContext()) await context.Database.MigrateAsync();
        await ExecuteAsync(TargetConnectionString, """
            INSERT INTO "OssAuditRuns" ("StartedAt","Status","ReferenceContractVersion","ReferenceSnapshots")
              VALUES (1,1,'storage-references-v1','[]');
            """);
        using (var context = CreateContext())
            await Assert.ThrowsAsync<PostgresException>(() => context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync(AuditMigrationExecutor.InitialCreateMigrationId));
        (await ReadHistoryAsync(TargetConnectionString)).Should().Equal(AuditMigrationExecutor.KnownMigrationIds);
        await ExecuteAsync(TargetConnectionString, $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\"='{AuditMigrationExecutor.InitialCreateMigrationId}'");
        using (var context = CreateContext())
            (await new AuditMigrationExecutor(context, NullLogger.Instance).InspectAsync()).Should()
                .Be(AuditMigrationExecutor.DatabaseState.InspectionFailed);
        (await CountRowsAsync(TargetConnectionString, "OssAuditRuns")).Should().Be(1);
    }

    // ---------- refused states: zero writes ----------

    [Fact]
    public async Task UnknownStructures_RefusedSafely_ZeroWrites()
    {
        await ExecuteAsync(TargetConnectionString, LegacyDdl);

        // 1. extra column
        await ExecuteAsync(TargetConnectionString, """ALTER TABLE "OssAuditRecords" ADD COLUMN "Extra" text""");
        await AssertRefusedAsync("extra column");
        await ExecuteAsync(TargetConnectionString, """ALTER TABLE "OssAuditRecords" DROP COLUMN "Extra" """);

        // 2. wrong type on a mapped column
        await ExecuteAsync(
            TargetConnectionString,
            """ALTER TABLE "OssAuditRecords" ALTER COLUMN "ObjectPath" TYPE varchar(500)""");
        await AssertRefusedAsync("wrong column type");
        await ExecuteAsync(
            TargetConnectionString,
            """ALTER TABLE "OssAuditRecords" ALTER COLUMN "ObjectPath" TYPE text""");

        // 3. partial table set (only one of the two business tables)
        await ExecuteAsync(TargetConnectionString, """DROP TABLE "OssAuditRuns" CASCADE""");
        await AssertRefusedAsync("partial table set");
    }

    private async Task AssertRefusedAsync(string scenario)
    {
        using var context = CreateContext();
        var executor = new AuditMigrationExecutor(context, NullLogger.Instance);
        (await executor.InspectAsync()).Should()
            .Be(AuditMigrationExecutor.DatabaseState.InspectionFailed, scenario);

        var result = await CreateOrchestrator(executor)
            .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));
        result.Succeeded.Should().BeFalse(scenario);
        result.ExecutorWasCalled.Should().BeFalse(scenario);
        result.ErrorCode.Should().Be(WellKnownMigrationErrorCodes.InspectionFailed, scenario);

        // Zero writes: no history table was created for an unknown structure.
        (await ReadHistoryAsync(TargetConnectionString)).Should().BeEmpty(scenario);
    }

    [Fact]
    public async Task VersionTooNewHistory_RefusedSafely_WithoutExecuting()
    {
        await ExecuteAsync(
            TargetConnectionString,
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"));
            INSERT INTO "__EFMigrationsHistory" VALUES ('20990101000000_FromTheFuture', '99.0.0');
            """);

        using var context = CreateContext();
        var result = await CreateOrchestrator(
                new AuditMigrationExecutor(context, NullLogger.Instance))
            .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));

        result.Succeeded.Should().BeFalse();
        result.ExecutorWasCalled.Should().BeFalse();
        result.ErrorCode.Should().Be(WellKnownMigrationErrorCodes.VersionTooNew);
    }

    // ---------- multi-instance serialization ----------

    [Fact]
    public async Task TwoConcurrentInstances_EmptyDatabase_ExactlyOneExecutes()
    {
        using var context1 = CreateContext();
        using var context2 = CreateContext();
        var orchestrator1 = CreateOrchestrator(new AuditMigrationExecutor(context1, NullLogger.Instance));
        var orchestrator2 = CreateOrchestrator(new AuditMigrationExecutor(context2, NullLogger.Instance));
        using var barrier = new Barrier(2);

        var runs = await Task.WhenAll(
            Task.Run(async () =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                return await orchestrator1.OrchestrateMigrationAsync(
                    MigrationServiceId, Target, TimeSpan.FromSeconds(30)).AsTask();
            }),
            Task.Run(async () =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                return await orchestrator2.OrchestrateMigrationAsync(
                    MigrationServiceId, Target, TimeSpan.FromSeconds(30)).AsTask();
            }));

        runs.Should().OnlyContain(result => result.Succeeded);
        runs.Count(result => result.ExecutorWasCalled).Should().Be(1);
        runs.Count(result => !result.ExecutorWasCalled).Should().Be(1);

        (await CountRowsAsync(TargetConnectionString, "OssAuditRecords")).Should().Be(0);
        (await CountRowsAsync(TargetConnectionString, "OssAuditRuns")).Should().Be(0);
        (await ReadHistoryAsync(TargetConnectionString)).Should()
            .Equal(AuditMigrationExecutor.KnownMigrationIds);
    }

    // ---------- failures and cancellation never fake success ----------

    [Fact]
    public async Task ExecutorFailure_OrchestrationFails_LockReleasedForNextRun()
    {
        var failing = new ScriptedExecutor(
            MigrationObservationState.Empty,
            () => throw new InvalidOperationException("executor blew up"));

        var failed = await CreateOrchestrator(failing)
            .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));
        failed.Succeeded.Should().BeFalse();
        failed.ErrorCode.Should().Be(WellKnownMigrationErrorCodes.ExecutionFailed);
        failed.ErrorMessage.Should().NotContain("blew up", "the safe message never carries exception text");

        // The advisory lock was released: the next orchestration with the real executor can
        // acquire it and succeed.
        using var context = CreateContext();
        var recovered = await CreateOrchestrator(
                new AuditMigrationExecutor(context, NullLogger.Instance))
            .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30));
        recovered.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task PreCancelledToken_OrchestrationThrows_OperationCanceled()
    {
        using var context = CreateContext();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var orchestration = () => CreateOrchestrator(
                new AuditMigrationExecutor(context, NullLogger.Instance))
            .OrchestrateMigrationAsync(MigrationServiceId, Target, TimeSpan.FromSeconds(30), cancelled.Token).AsTask();

        await orchestration.Should().ThrowAsync<OperationCanceledException>();
        (await TableExistsAsync(TargetConnectionString, "OssAuditRecords")).Should()
            .BeFalse("cancellation before any stage must leave the target untouched");
    }

    // ---------- the real Program.cs host ----------

    [Fact]
    public async Task Host_MissingDatabase_Default_RefusesWithFixedCode_ZeroWrites()
    {
        var missing = $"audit_mig_missing_{Guid.NewGuid():N}";
        using var factory = CreateFactory(settings: DatabaseNameOverrides(missing));

        var exception = Record.Exception(() => factory.CreateClient());

        exception.Should().NotBeNull();
        exception!.ToString().Should().Contain(WellKnownDatabaseTargetPreparationErrorCodes.CreationNotAllowed);

        // Zero writes: the missing catalog was not created.
        (await DatabaseExistsAsync(missing)).Should().BeFalse();
    }

    [Fact]
    public async Task Host_MissingDatabase_AllowCreateTrue_CreatesMigratesAndReportsReady()
    {
        var missing = $"audit_mig_create_{Guid.NewGuid():N}";
        try
        {
            using var factory = CreateFactory(settings: DatabaseNameOverrides(
                missing, ("Database:AllowCreate", "true")));
            using var client = factory.CreateClient();

            using var ready = await client.GetAsync("/health/ready");
            ready.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ready.Content.ReadAsStringAsync()).Should().Be(
                "{\"status\":\"ready\",\"phase\":\"completed\",\"migrationStatus\":\"succeeded\"," +
                "\"databaseStatus\":\"reachable\",\"errorCode\":null}");

            (await DatabaseExistsAsync(missing)).Should().BeTrue();
            (await ReadHistoryAsync(WithDatabase(Database.ConnectionString, missing))).Should()
                .Equal(AuditMigrationExecutor.KnownMigrationIds);
            foreach (var table in AuditMigrationExecutor.KnownTableNames)
            {
                (await TableExistsAsync(WithDatabase(Database.ConnectionString, missing), table))
                    .Should().BeTrue($"table '{table}' must exist after startup");
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(
                MaintenanceConnectionString,
                $"DROP DATABASE IF EXISTS \"{missing}\" WITH (FORCE)");
        }
    }

    [Fact]
    public void Host_MissingDatabase_InvalidAllowCreate_FailsFast()
    {
        var missing = $"audit_mig_invalid_{Guid.NewGuid():N}";
        using var factory = CreateFactory(settings: DatabaseNameOverrides(
            missing, ("Database:AllowCreate", "maybe")));

        var exception = Record.Exception(() => factory.CreateClient());

        exception.Should().NotBeNull();
        exception!.ToString().Should().Contain(WellKnownDatabaseTargetPreparationErrorCodes.InvalidTarget);
        exception.ToString().Should().NotContain("maybe");
    }

    [Fact]
    public async Task Host_UnknownStructure_OrchestrationRefusesStartup()
    {
        await ExecuteAsync(TargetConnectionString, LegacyDdl);
        await ExecuteAsync(
            TargetConnectionString,
            """ALTER TABLE "OssAuditRuns" ADD COLUMN "Mystery" text""");

        using var factory = CreateFactory(settings: DatabaseNameOverrides(_database));

        var exception = Record.Exception(() => factory.CreateClient());
        exception.Should().NotBeNull();
        exception!.ToString().Should().Contain(WellKnownMigrationErrorCodes.InspectionFailed);

        // Zero writes: no history row was invented for the unknown structure.
        (await ReadHistoryAsync(TargetConnectionString)).Should().BeEmpty();
    }

    [Fact]
    public async Task Host_ExecutorFailure_FailsStartup_InsteadOfServing()
    {
        using var factory = CreateFactory(
            settings: DatabaseNameOverrides(_database),
            configureTestServices: services =>
            {
                services.RemoveAll<IDatabaseMigrationExecutor>();
                services.AddScoped<IDatabaseMigrationExecutor>(_ =>
                    new ScriptedExecutor(
                        MigrationObservationState.Empty,
                        () => throw new InvalidOperationException("executor blew up")));
            });

        var exception = Record.Exception(() => factory.CreateClient());
        exception.Should().NotBeNull();
        exception!.ToString().Should().Contain(WellKnownMigrationErrorCodes.ExecutionFailed);
    }

    // ---------- helpers ----------

    private IReadOnlyDictionary<string, string?> DatabaseNameOverrides(
        string database, params (string Key, string Value)[] extra)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Database:Name"] = database,
        };
        foreach (var (key, value) in extra)
        {
            overrides[key] = value;
        }

        return overrides;
    }

    private async Task<bool> DatabaseExistsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(
            WithDatabase(Database.ConnectionString, "postgres"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (SELECT FROM pg_database WHERE datname = $1)
            """;
        command.Parameters.AddWithValue(database);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private sealed class ScriptedExecutor(
        MigrationObservationState inspection,
        Action execute) : IDatabaseMigrationExecutor
    {
        public ValueTask<MigrationObservationState> InspectAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(inspection);

        public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
        {
            execute();
            return ValueTask.CompletedTask;
        }
    }
}
