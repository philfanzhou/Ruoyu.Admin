namespace Admin.WebApi.Health;

/// <summary>
/// The bounded, process-local receipt of this host instance's own startup initialization.
/// </summary>
/// <remarks>
/// Admin keeps no installation or migration-state tables in <c>ruoyu_admin</c>: the only
/// authoritative fact a readiness probe may rely on before touching PostgreSQL is whether the
/// startup database migration orchestration (target preparation + the shared ServiceMantle
/// orchestrator around <c>AuditMigrationExecutor</c>) completed successfully in this process.
/// The receipt starts as "not completed" and can only move forward, exactly once, when the
/// orchestration reports success. It never fabricates an installation record and never borrows
/// the ServiceMantle Bootstrap phase resolver. A successful orchestration return is not a
/// substitute for live verification, which is why <see cref="AdminHealthSnapshotSource"/> still
/// runs its read-only schema probe before publishing readiness.
/// </remarks>
public sealed class AdminStartupReceipt
{
    private const int Running = 0;
    private const int Completed = 1;

    private int _state = Running;

    /// <summary>Gets whether this process completed its startup initialization.</summary>
    public bool InitializationCompleted => Volatile.Read(ref _state) == Completed;

    /// <summary>
    /// Records the one-way transition to "initialization completed". Calling it again is a
    /// no-op; there is no path back to the running state within a process lifetime.
    /// </summary>
    public void MarkInitializationCompleted() =>
        Interlocked.Exchange(ref _state, Completed);
}
