namespace Ruoyu.Admin.ServiceClients;

public sealed record ManagedMistakeUpload(Guid SourceUploadId, Guid ExpectedContentRevision, Guid RequestKey,
    Guid StudentId, int Subject, int Grade, IReadOnlyList<string> ImagePaths, string RootCause);
