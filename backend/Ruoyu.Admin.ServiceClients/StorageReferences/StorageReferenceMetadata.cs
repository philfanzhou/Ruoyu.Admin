using System.Text.Json.Serialization;
namespace Ruoyu.Admin.ServiceClients.StorageReferences;

public sealed record StorageReferenceMetadata(
    [property: JsonRequired] string Version,
    [property: JsonRequired] string Provider,
    [property: JsonRequired] Guid SnapshotId,
    [property: JsonRequired] DateTimeOffset CreatedAt,
    [property: JsonRequired] DateTimeOffset ExpiresAt,
    [property: JsonRequired] string Coverage,
    [property: JsonRequired] int EntryCount,
    [property: JsonRequired] int PageSize,
    [property: JsonRequired] int PageCount,
    [property: JsonRequired] string Sha256,
    [property: JsonRequired] bool DeletionAuthorized);
