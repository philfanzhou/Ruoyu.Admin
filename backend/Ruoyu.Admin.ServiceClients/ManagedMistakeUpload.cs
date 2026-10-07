namespace Ruoyu.Admin.ServiceClients;

/// <summary>Mirror of the mistake service's BoundingBoxDto (integer rectangle, X1&lt;X2, Y1&lt;Y2).</summary>
public sealed record ManagedBoundingBox(int X1, int Y1, int X2, int Y2);

/// <summary>Mirror of the mistake service's SourceRegionDto; a null box means the full source image.</summary>
public sealed record ManagedSourceRegion(string SourceImagePath, ManagedBoundingBox? BoundingBox);

/// <summary>
/// Managed upload payload. Exactly one of <see cref="ImagePaths"/> (full-image regions) and
/// <see cref="SourceRegions"/> (explicit crop regions) is sent; the upstream contract rejects
/// requests that carry both with 400 invalid_request.
/// </summary>
public sealed record ManagedMistakeUpload(Guid SourceUploadId, Guid ExpectedContentRevision, Guid RequestKey,
    Guid StudentId, int Subject, int Grade, IReadOnlyList<string> ImagePaths,
    IReadOnlyList<ManagedSourceRegion>? SourceRegions, string RootCause);
