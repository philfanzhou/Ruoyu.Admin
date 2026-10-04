using Ruoyu.Admin.ServiceClients.StorageReferences;

namespace Admin.WebApi.Services;

public sealed record StorageReferenceCollection(HashSet<string> Keys, IReadOnlyList<StorageReferenceMetadata> Snapshots);
