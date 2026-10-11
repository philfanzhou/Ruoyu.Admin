namespace Ruoyu.Admin.Common.Oss;

/// <summary>
/// 缩略图路径判定的共享逻辑。
/// 路径规则：与原图同目录，文件名格式为 {stem}_{size}.jpg
/// 此规则一旦上线不可变更，否则会产生孤儿文件。
/// </summary>
public static class ThumbnailHelper
{
    // 缩略图尺寸后缀。与 Tools/LocalRegressionStubs/RegressionFixtures.cs 的
    // ThumbnailSuffixes 手工保持同步（该工具不引用本项目以保持轻量依赖）。
    private static readonly string[] ThumbnailSizes = ["thumbnail", "small", "medium"];

    /// <summary>
    /// 判断路径是否是缩略图路径（匹配 _{size}.jpg 模式）
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
