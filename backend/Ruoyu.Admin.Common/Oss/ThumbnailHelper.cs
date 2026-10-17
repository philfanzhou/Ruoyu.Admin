namespace Ruoyu.Admin.Common.Oss;

/// <summary>
/// Shared logic for deciding whether a path is a thumbnail.
/// Path rule: same directory as the original, file name of the form {stem}_{size}.jpg.
/// Once deployed, this rule must not change; otherwise orphan files would appear.
/// </summary>
public static class ThumbnailHelper
{
    // Thumbnail size suffixes. Kept in sync by hand with ThumbnailSuffixes in
    // Tools/LocalRegressionStubs/RegressionFixtures.cs (that tool does not reference this
    // project, to keep its dependency footprint light).
    private static readonly string[] ThumbnailSizes = ["thumbnail", "small", "medium"];

    /// <summary>
    /// Determines whether the path is a thumbnail path (matching the _{size}.jpg pattern)
    /// </summary>
    public static bool IsThumbnailPath(string objectPath)
    {
        if (!objectPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = Path.GetFileNameWithoutExtension(objectPath);
        foreach (var size in ThumbnailSizes)
        {
            if (fileName.EndsWith($"_{size}", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
