namespace Ruoyu.Admin.ServiceClients;

public sealed record ManagedMistakeResult(string State, string ErrorKind, IReadOnlyList<Guid> CreatedItemIds, int? HttpStatus = null)
{
    public static ManagedMistakeResult Unknown(string kind, int? status = null) => new("Unknown", kind, Array.Empty<Guid>(), status);
    public static ManagedMistakeResult Failed(string kind, int? status = null) => new("Failed", kind, Array.Empty<Guid>(), status);
}
