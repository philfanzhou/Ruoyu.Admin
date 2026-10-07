namespace Ruoyu.Admin.ServiceClients;

public sealed record MistakeClientPolicy(bool UseSessionToken)
{
    // Per-message cancellation only; never a credential or pooled request scope.
    public static readonly HttpRequestOptionsKey<CancellationToken> RequestAborted = new("Ruoyu.Admin.Mistake.RequestAborted");
}
