using System.Text.Json.Serialization;
namespace Ruoyu.Admin.ServiceClients.StorageReferences;

public sealed record StorageReferencePage(
    [property: JsonRequired] string Version,
    [property: JsonRequired] string Provider,
    [property: JsonRequired] Guid SnapshotId,
    [property: JsonRequired] int Index,
    [property: JsonRequired] IReadOnlyList<string> Keys,
    [property: JsonRequired] bool IsLast);
