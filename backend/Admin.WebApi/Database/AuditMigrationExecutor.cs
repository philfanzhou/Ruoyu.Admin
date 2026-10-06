using System.Reflection;
using Admin.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Npgsql;
using ServiceMantle.Database.PostgreSql.Migration;
using ServiceMantle.Persistence.Relational.Migration;
using ServiceMantle.Migration;

namespace Admin.WebApi.Database;

/// <summary>
/// The EF-migration executor for the <c>ruoyu_admin</c> audit database (issue #57).
/// </summary>
/// <remarks>
/// <para>
/// The executor implements <see cref="IDatabaseMigrationExecutor"/> for the shared ServiceMantle
/// <c>DatabaseMigrationOrchestrator</c>: its internal <see cref="DatabaseState"/> classification
/// maps one-to-one onto <see cref="MigrationObservationState"/> except that a verified legacy
/// database (<see cref="DatabaseState.LegacyTakeoverRequired"/> — created by the retired inline
/// <c>CREATE TABLE IF NOT EXISTS</c> startup DDL) is reported as
/// <see cref="MigrationObservationState.PendingMigration"/>, the orchestrator's "execute once
/// under the lock" signal. <c>__EFMigrationsHistory</c> is owned by this executor.
/// </para>
/// <para>
/// <c>InspectAsync</c> is strictly read-only (PostgreSQL catalogs only) and classifies the target
/// database against the single known baseline migration:
/// </para>
/// <list type="bullet">
/// <item>Empty: no business tables and no applied history (a missing catalog also classifies as
/// Empty; creating it is the target-preparation stage's decision, not this executor's).</item>
/// <item>CurrentVersionCompatible: the baseline is applied and the complete current schema
/// verifies; nothing will be written.</item>
/// <item>LegacyTakeoverRequired: both business tables are present without history and the
/// structure verifies — a missing model index is collected as a safe backfill to apply before
/// stamping.</item>
/// <item>VersionTooNew: the history contains migration ids this application does not know.</item>
/// <item>InspectionFailed: any unknown or conflicting structure (extra or wrong-typed columns,
/// nullability mismatches, non-identity or wrong-identity-kind Id columns, primary-key or index
/// mismatches, partial table sets, or a history that claims a version the schema contradicts).
/// Unknown state is never stamped, repaired, or silently accepted.</item>
/// </list>
/// <para>
/// The verification compares column names, store types, nullability, and the
/// <c>GENERATED ALWAYS AS IDENTITY</c> kind of the Id columns (the retired inline DDL used
/// exactly that identity form), plus primary keys and the complete index set; column store
/// defaults are deliberately not compared (their catalog expression text is driver-formatted and
/// the known legacy states are byte-identical anyway).
/// </para>
/// <para>
/// <c>ExecuteAsync</c> re-verifies immediately before any write, applies the safe index backfills,
/// stamps only the verified baseline in its own transaction (the stamp point is after the
/// structure verification, never at startup), and lets EF Core run the real migrations on the
/// empty state. Rejected states fail startup with the fixed error code
/// <see cref="IncompatibleErrorCode"/> and a structure-difference summary; the error surfaces and
/// logs never contain SQL statement text or connection values.
/// </para>
/// </remarks>
public sealed class AuditMigrationExecutor : IDatabaseMigrationExecutor
{
    internal const string InitialCreateMigrationId = "20260930190608_InitialCreate";
    internal const string HistoryTableName = "__EFMigrationsHistory";
    internal const string IncompatibleErrorCode = "RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE";

    /// <summary>The ordered migration contract this executor supports; frozen by the issue.</summary>
    internal static readonly IReadOnlyList<string> KnownMigrationIds = [InitialCreateMigrationId, "20261003030002_AddReferenceObservations"];

    /// <summary>The two business tables of the baseline.</summary>
    internal static readonly IReadOnlyList<string> KnownTableNames = ["OssAuditRecords", "OssAuditRuns"];

    private const string SchemaName = "public";

    private readonly AuditDbContext _context;
    private readonly ILogger? _logger;

    public AuditMigrationExecutor(AuditDbContext context, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc cref="IDatabaseMigrationExecutor.InspectAsync"/>
    /// <remarks>
    /// Explicit interface implementation for the shared orchestrator: the same read-only
    /// inspection, translated into the ServiceMantle observation states
    /// (<see cref="DatabaseState.LegacyTakeoverRequired"/> becomes
    /// <see cref="MigrationObservationState.PendingMigration"/>).
    /// </remarks>
    async ValueTask<MigrationObservationState> IDatabaseMigrationExecutor.InspectAsync(
        CancellationToken cancellationToken)
    {
        var state = await InspectAsync(cancellationToken).ConfigureAwait(false);
        return ToObservationState(state);
    }

    /// <inheritdoc cref="IDatabaseMigrationExecutor.ExecuteAsync"/>
    /// <remarks>
    /// Explicit interface implementation: the identical execution path (re-verify, backfill,
    /// stamp, migrate, or refuse) observing the orchestrator's cancellation token.
    /// </remarks>
    ValueTask IDatabaseMigrationExecutor.ExecuteAsync(CancellationToken cancellationToken) =>
        new(ExecuteAsync(cancellationToken));

    /// <summary>
    /// The fixed translation from this executor's classification to the ServiceMantle
    /// observation states consumed by <c>DatabaseMigrationOrchestrator</c>.
    /// </summary>
    internal static MigrationObservationState ToObservationState(DatabaseState state) => state switch
    {
        DatabaseState.Empty => MigrationObservationState.Empty,
        DatabaseState.CurrentVersionCompatible => MigrationObservationState.CurrentVersionCompatible,
        DatabaseState.LegacyTakeoverRequired => MigrationObservationState.PendingMigration,
        DatabaseState.KnownUpgradeRequired => MigrationObservationState.PendingMigration,
        DatabaseState.VersionTooNew => MigrationObservationState.VersionTooNew,
        _ => MigrationObservationState.InspectionFailed,
    };

    /// <summary>The read-only classification of the target database.</summary>
    public enum DatabaseState
    {
        /// <summary>No business tables and no history: every migration really executes.</summary>
        Empty,

        /// <summary>The full known history is applied and the schema verifies.</summary>
        CurrentVersionCompatible,

        /// <summary>A verified legacy database without history: backfill, then stamp the baseline.</summary>
        LegacyTakeoverRequired,

        KnownUpgradeRequired,

        /// <summary>The history contains migration ids this application does not know.</summary>
        VersionTooNew,

        /// <summary>Any unknown or conflicting structure; execution is refused.</summary>
        InspectionFailed,
    }

    internal sealed record DatabaseInspection(
        DatabaseState State,
        string Reason,
        IReadOnlyList<BackfillStatement> Backfills,
        bool RequiresHistoryStamp)
    {
        internal static DatabaseInspection Of(
            DatabaseState state, string reason,
            IReadOnlyList<BackfillStatement>? backfills = null, bool requiresHistoryStamp = false) =>
            new(state, reason, backfills ?? [], requiresHistoryStamp);

        internal static DatabaseInspection Failed(string reason) =>
            new(DatabaseState.InspectionFailed, reason, [], false);
    }

    /// <summary>A safe, idempotent DDL statement collected during inspection.</summary>
    internal sealed record BackfillStatement(string Table, string Description, string Sql);

    public async Task<DatabaseState> InspectAsync(CancellationToken cancellationToken = default)
    {
        var inspection = await InspectDetailedAsync(cancellationToken).ConfigureAwait(false);
        return inspection.State;
    }

    internal async Task<DatabaseInspection> InspectDetailedAsync(CancellationToken cancellationToken)
    {
        EnsureMigrationContract();

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        try
        {
            var evidence = await new PostgreSqlSchemaEvidenceReader().ReadAsync(connection,
                new PostgreSqlSchemaEvidenceReadOptions(includeExtendedObjectEvidence: true,
                    tables: KnownTableNames.Select(name => (SchemaName, name)).ToArray()),
                cancellationToken).ConfigureAwait(false);
            if (evidence.State == SchemaEvidenceReadState.ReadFailed)
                _logger?.LogWarning("Database inspection could not read the target structure");
            return evidence.State switch
            {
                SchemaEvidenceReadState.TargetDatabaseMissing => DatabaseInspection.Of(
                    DatabaseState.Empty, "the target database does not exist yet"),
                SchemaEvidenceReadState.ReadFailed => DatabaseInspection.Failed(evidence.Message),
                _ => InspectEvidence(evidence)
            };
        }
        catch (Exception exception) when (IsCancellation(exception, cancellationToken))
        {
            throw NewCancellation(exception, cancellationToken);
        }
        catch (Exception)
        {
            _logger?.LogWarning("Database inspection could not read the target structure");
            return DatabaseInspection.Failed("the database structure could not be read");
        }
        finally
        {
            try
            {
                if (openedHere) await connection.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        EnsureMigrationContract();

        // Re-verify immediately before any write: the classification that got this call
        // scheduled may be stale, and duplicate runs must re-read the legitimate state instead
        // of double-stamping.
        var inspection = await InspectDetailedAsync(cancellationToken).ConfigureAwait(false);
        switch (inspection.State)
        {
            case DatabaseState.CurrentVersionCompatible:
                _logger?.LogInformation(
                    "Database is already compatible with the current version; nothing to execute");
                return;

            case DatabaseState.KnownUpgradeRequired:
            case DatabaseState.Empty:
                _logger?.LogInformation(
                    "Applying the {MigrationId} baseline migration to the empty database state ({Reason})",
                    InitialCreateMigrationId, inspection.Reason);
                cancellationToken.ThrowIfCancellationRequested();
                await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Database initialized from the migration baseline");
                return;

            case DatabaseState.LegacyTakeoverRequired:
                _logger?.LogInformation(
                    "Taking over a verified legacy database: {BackfillCount} safe structure backfill(s), then registering the {MigrationId} baseline",
                    inspection.Backfills.Count, InitialCreateMigrationId);
                cancellationToken.ThrowIfCancellationRequested();
                await ApplyBackfillsAsync(inspection.Backfills, cancellationToken).ConfigureAwait(false);
                await StampBaselineAsync(cancellationToken).ConfigureAwait(false);
                await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation(
                    "Legacy database taken over: schema verified, data preserved, baseline registered");
                return;

            case DatabaseState.VersionTooNew:
            case DatabaseState.InspectionFailed:
            default:
                throw new InvalidOperationException(
                    $"[{IncompatibleErrorCode}] Database migration was requested but the target state is " +
                    $"{inspection.State}: {inspection.Reason}. Refusing to take over the database; no changes " +
                    "were made. Back up the database and reconcile its structure with the migration baseline " +
                    "manually before restarting.");
        }
    }

    // ---------- inspection ----------

    private DatabaseInspection InspectEvidence(SchemaEvidenceReadResult evidence)
    {
        var expected = BuildExpectedTables();
        var snapshot = evidence.Snapshot!;
        var presentBusinessTables = snapshot.Tables.Select(table => table.Name).ToList();
        var applied = evidence.AppliedMigrationIds;

        var unknownIds = applied.Where(id => !KnownMigrationIds.Contains(id)).ToList();
        if (unknownIds.Count > 0)
        {
            return DatabaseInspection.Of(
                DatabaseState.VersionTooNew,
                $"the migration history contains ids this application does not know: {Join(unknownIds)}");
        }

        if (!applied.SequenceEqual(KnownMigrationIds.Take(applied.Count), StringComparer.Ordinal))
            return DatabaseInspection.Failed("migration history is not an exact supported prefix");

        if (applied.Count == KnownMigrationIds.Count)
        {
            // The history claims the current version; the complete current schema must verify.
            var mismatch = ValidateTables(snapshot, expected, backfills: null);
            return mismatch is null
                ? DatabaseInspection.Of(
                    DatabaseState.CurrentVersionCompatible,
                    "the full known history is applied and the current schema verifies")
                : DatabaseInspection.Failed(
                    $"the history claims the current version but the schema does not verify: {mismatch.Reason}");
        }

        if (applied.Count == 1)
        {
            var mismatch = ValidateTables(snapshot, BuildExpectedTables(baseline: true), backfills: null);
            return mismatch is null ? DatabaseInspection.Of(DatabaseState.KnownUpgradeRequired,
                "the exact baseline schema requires the reference-observation migration") : DatabaseInspection.Failed(mismatch.Reason);
        }

        if (presentBusinessTables.Count == 0)
        {
            if (applied.Count != 0) return DatabaseInspection.Failed("history present without business tables");
            // No business tables at all (history absent): EF runs the baseline migration.
            return DatabaseInspection.Of(
                DatabaseState.Empty,
                "no business tables and no applied migrations are present");
        }

        if (presentBusinessTables.Count != KnownTableNames.Count)
        {
            // Partial table sets without a full history are refused.
            return DatabaseInspection.Failed(
                $"business tables are partially present: found [{Join(presentBusinessTables)}] but the baseline requires all of [{Join(KnownTableNames)}]");
        }

        // Both tables are present without history: verify and collect safe backfills.
        var backfills = new List<BackfillStatement>();
        var verification = ValidateTables(snapshot, BuildExpectedTables(baseline: true), backfills);
        if (verification is not null)
        {
            return DatabaseInspection.Failed(verification.Reason);
        }

        return DatabaseInspection.Of(
            DatabaseState.LegacyTakeoverRequired,
            "a verified legacy database without migration history requires the safe backfills and the baseline registration",
            backfills,
            requiresHistoryStamp: true);
    }

    private void EnsureMigrationContract()
    {
        var assemblyMigrations = _context.Database.GetMigrations().ToList();
        if (!assemblyMigrations.SequenceEqual(KnownMigrationIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"[{IncompatibleErrorCode}] The migration assembly contains [{string.Join(", ", assemblyMigrations)}] " +
                $"but this executor only supports [{string.Join(", ", KnownMigrationIds)}]. Adding or changing " +
                "migrations requires extending the executor's known-version contract.");
        }
    }

    // Projection applies only this consumer's established comparison boundary. The shared
    // derivation owns EF traversal; public is the historical default schema, not a migration.
    private ExpectedSchema BuildExpectedTables(bool baseline = false)
    {
        var model = _context.GetService<IDesignTimeModel>().Model;
        var derived = EfCoreExpectedSchemaDerivation.Derive(model,
            new EfCoreExpectedSchemaDerivationOptions(includeExtendedObjectEvidence: true));
        if (derived.Tables.Count != KnownTableNames.Count ||
            derived.Tables.Any(table => !KnownTableNames.Contains(table.Name)))
            throw new InvalidOperationException($"[{IncompatibleErrorCode}] The EF model does not match the supported table contract.");
        return new ExpectedSchema(derived.Tables.Select(table => new SchemaTable(table.Name,
            table.Columns.Where(column => !baseline || column.Name is not
                ("ReferenceContractVersion" or "ReferenceSnapshots")).ToArray(),
            table.PrimaryKey, indexes: table.Indexes,
            schema: table.Schema ?? SchemaName)).ToArray());
    }

    private static VerificationFailure? ValidateTables(SchemaSnapshot snapshot,
        ExpectedSchema expected, List<BackfillStatement>? backfills)
    {
        var projected = new List<SchemaTable>();
        var comparisonExpected = new List<SchemaTable>();
        foreach (var table in expected.Tables)
        {
            var actual = snapshot.Tables.SingleOrDefault(candidate => candidate.Name == table.Name);
            if (actual is null) { comparisonExpected.Add(table); continue; }
            var indexes = table.Indexes.ToList();
            if (backfills is not null)
                foreach (var index in table.Indexes.Where(index => !actual.Indexes.Any(candidate =>
                             NameMatches(candidate.Name, index.Name))))
                {
                    // Only absent named indexes are eligible for the historical safe backfill.
                    var name = index.Name!;
                    backfills.Add(new BackfillStatement(table.Name, $"index {name}",
                        $"CREATE {(index.IsUnique ? "UNIQUE " : "")}INDEX IF NOT EXISTS {Quote(name)} ON {Quote(table.Name)} ({string.Join(", ", index.Columns.Select(Quote))})"));
                    indexes.Remove(index);
                }
            comparisonExpected.Add(new SchemaTable(table.Name, table.Columns, NormalizeKey(table.PrimaryKey),
                indexes: indexes.Select(NormalizeIndex).ToArray(), schema: table.Schema));
            projected.Add(new SchemaTable(actual.Name, actual.Columns.Select(column =>
                new SchemaColumn(column.Name, column.DataType, column.IsNullable,
                    column.IdentityKind == SchemaIdentityKind.Always ? SchemaIdentityKind.Always : SchemaIdentityKind.None,
                    column.HasStoredDefault)).ToArray(), NormalizeKey(actual.PrimaryKey),
                indexes: table.Indexes.Select(index => actual.Indexes.FirstOrDefault(candidate =>
                    NameMatches(candidate.Name, index.Name))).OfType<SchemaIndex>().Select(NormalizeIndex).ToArray(), schema: actual.Schema));
        }
        var differences = SchemaEvidenceComparer.Compare(new SchemaSnapshot(projected),
            new ExpectedSchema(comparisonExpected), new SchemaEvidenceComparisonOptions(
                compareObjectNames: true, compareIndexKeyDetails: true));
        return differences.Count == 0 ? null : new VerificationFailure(string.Join("; ", differences));
    }

    private static SchemaPrimaryKey? NormalizeKey(SchemaPrimaryKey? key) =>
        key is null ? null : new SchemaPrimaryKey(key.Columns, key.Name?.ToUpperInvariant());

    private static SchemaIndex NormalizeIndex(SchemaIndex index) =>
        new(index.Columns, index.IsUnique, index.Name?.ToUpperInvariant(), index.KeyColumnCount, index.IncludedColumns);

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>
    /// Known constraint and index names are compared case-insensitively; the semantics
    /// (columns, uniqueness) must still match exactly.
    /// </summary>
    private static bool NameMatches(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private sealed record VerificationFailure(string Reason);

    // ---------- execution ----------

    private async Task ApplyBackfillsAsync(
        IReadOnlyList<BackfillStatement> backfills, CancellationToken cancellationToken)
    {
        foreach (var backfill in backfills)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _context.Database.ExecuteSqlRawAsync(backfill.Sql, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(
                "Applied legacy structure backfill on {Table}: {Description}",
                backfill.Table, backfill.Description);
        }
    }

    private async Task StampBaselineAsync(CancellationToken cancellationToken)
    {
        await new EfCoreMigrationBaselineWriter(_context).WriteBaselineAsync(
            _context.Database.GetDbConnection(), InitialCreateMigrationId, EfProductVersion,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _logger?.LogInformation("Registered the verified {MigrationId} baseline for the legacy database",
            InitialCreateMigrationId);
    }

    private static string EfProductVersion { get; } =
        typeof(DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(DbContext).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    // ---------- safe diagnostics ----------

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);

    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested || exception is OperationCanceledException;

    private static OperationCanceledException NewCancellation(
        Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException canceled
            ? canceled
            : new OperationCanceledException(
                "Database migration work was cancelled.", cancellationToken);
}
