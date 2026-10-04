using System.Data.Common;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Ruoyu.Admin.Common.Oss;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

[Collection(ServiceMantleIntegrationCollection.Name)]
public sealed class StorageReferenceWorkerCancellationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CancellationDuringRunningCheck_PersistsFailedRunBeforeCollectionOrS3Async()
    {
        var name = "admin_refs_cancel_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
            var plain = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connection).Options;
            await using (var db = new AuditDbContext(plain)) await db.Database.MigrateAsync();
            using var cancellation = new CancellationTokenSource();
            var interceptor = new CancelRunningCheck(cancellation);
            var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connection).AddInterceptors(interceptor).Options;
            var references = new Mock<IStorageReferenceCollector>(MockBehavior.Strict);
            var oss = new Mock<IOssService>(MockBehavior.Strict);
            var services = new ServiceCollection();
            services.AddScoped(_ => new AuditDbContext(options));
            services.AddSingleton(references.Object);
            services.AddSingleton(oss.Object);
            using var provider = services.BuildServiceProvider();
            var worker = new OssAuditWorker(provider, NullLogger<OssAuditWorker>.Instance, new ConfigurationBuilder().Build());
            await worker.RunAuditAsync("manual", cancellation.Token);
            Assert.True(interceptor.Reached);
            await using var result = new AuditDbContext(plain);
            var run = await result.OssAuditRuns.SingleAsync();
            Assert.Equal(2, run.Status);
            Assert.Equal("audit_cancelled", run.ErrorMessage);
            Assert.Empty(await result.OssAuditRecords.ToListAsync());
            references.VerifyNoOtherCalls();
            oss.VerifyNoOtherCalls();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class CancelRunningCheck(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public bool Reached { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("EXISTS", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("OssAuditRuns", StringComparison.Ordinal))
            {
                Reached = true;
                cancellation.Cancel();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
