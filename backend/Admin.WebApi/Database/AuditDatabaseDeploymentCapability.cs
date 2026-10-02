using ServiceMantle.Bootstrap;

namespace Admin.WebApi.Database;

/// <summary>Declares the PostgreSQL deployment supported by Admin's real advisory-lock provider.</summary>
internal sealed class AuditDatabaseDeploymentCapability : IDatabaseDeploymentCapabilityProvider
{
    public DatabaseDeploymentCapability Capability { get; } = new(
        WellKnownDatabaseProviderIds.PostgreSql, DatabaseDeploymentSupport.SingleAndMultiInstance);

    // Admin always selects MultiInstance. A single-instance canonical identity is never
    // needed here; refuse that unsupported entry rather than inventing a global identity.
    public ValueTask<string> GetCanonicalTargetIdentityAsync(
        BootstrapDatabaseConfiguration target, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Admin requires the MultiInstance database deployment path.");
}
