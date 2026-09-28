using FluentAssertions;
using Ruoyu.Admin.Common.Oss;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// LocalFileOssService is the dev/local stand-in for S3OssService (USE_LOCAL_OSS=1).
/// Its DeleteAsync must mirror S3OssService: deleting an original cascades to the derived
/// thumbnails, so the local regression environment (docs/development/LocalRegressionEnv.md)
/// exercises the same audit disposal semantics as production.
/// </summary>
public class LocalFileOssServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"local-oss-tests-{Guid.NewGuid():N}");
    private readonly LocalFileOssService _service;

    public LocalFileOssServiceTests()
    {
        _service = new LocalFileOssService(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FullPath(string objectPath)
        => Path.Combine(_root, objectPath.Replace('/', Path.DirectorySeparatorChar));

    private async Task WriteObjectAsync(string objectPath, string content = "file")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FullPath(objectPath))!);
        await File.WriteAllTextAsync(FullPath(objectPath), content);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOriginalAndAllDerivedThumbnails()
    {
        var objectPath = "uploads/regression/orphan.jpg";
        var thumbnails = ThumbnailHelper.GetAllThumbnailPaths(objectPath);
        await WriteObjectAsync(objectPath, "original");
        foreach (var thumbnail in thumbnails)
        {
            await WriteObjectAsync(thumbnail);
        }

        var deleted = await _service.DeleteAsync(objectPath);

        deleted.Should().BeTrue();
        File.Exists(FullPath(objectPath)).Should().BeFalse("the original must be deleted");
        foreach (var thumbnail in thumbnails)
        {
            File.Exists(FullPath(thumbnail)).Should()
                .BeFalse($"thumbnail {thumbnail} must be deleted together with the original");
        }
    }

    [Fact]
    public async Task DeleteAsync_OnThumbnailPath_DeletesOnlyThatThumbnail()
    {
        var objectPath = "uploads/regression/keep.jpg";
        var thumbnail = ThumbnailHelper.GetThumbnailPath(objectPath, "small");
        await WriteObjectAsync(objectPath, "original");
        await WriteObjectAsync(thumbnail);

        var deleted = await _service.DeleteAsync(thumbnail);

        deleted.Should().BeTrue();
        File.Exists(FullPath(thumbnail)).Should().BeFalse();
        File.Exists(FullPath(objectPath)).Should()
            .BeTrue("deleting a thumbnail must never cascade to its original");
    }

    [Fact]
    public async Task DeleteAsync_LeavesUnrelatedFilesInPlace()
    {
        var objectPath = "uploads/regression/target.jpg";
        var unrelated = "uploads/regression/unrelated.jpg";
        await WriteObjectAsync(objectPath);
        foreach (var thumbnail in ThumbnailHelper.GetAllThumbnailPaths(objectPath))
        {
            await WriteObjectAsync(thumbnail);
        }
        await WriteObjectAsync(unrelated);

        var deleted = await _service.DeleteAsync(objectPath);

        deleted.Should().BeTrue();
        File.Exists(FullPath(unrelated)).Should().BeTrue("unrelated files must be untouched");
    }

    [Fact]
    public async Task DeleteAsync_MissingOriginal_ReturnsFalseAndKeepsExistingThumbnail()
    {
        var objectPath = "uploads/regression/never-existed.jpg";
        var thumbnail = ThumbnailHelper.GetThumbnailPath(objectPath, "thumbnail");
        await WriteObjectAsync(thumbnail);

        var deleted = await _service.DeleteAsync(objectPath);

        deleted.Should().BeFalse("an object that does not exist cannot be deleted");
        File.Exists(FullPath(thumbnail)).Should()
            .BeTrue("a missing original must not wipe thumbnails either");
    }

    [Fact]
    public async Task DeleteManyAsync_CascadesThumbnailsForEachObject()
    {
        var first = "uploads/regression/first.jpg";
        var second = "mistakes/regression/second.jpg";
        var paths = new[] { first, second };
        foreach (var path in paths)
        {
            await WriteObjectAsync(path, "original");
            foreach (var thumbnail in ThumbnailHelper.GetAllThumbnailPaths(path))
            {
                await WriteObjectAsync(thumbnail);
            }
        }

        var count = await _service.DeleteManyAsync(paths);

        count.Should().Be(2);
        foreach (var path in paths)
        {
            File.Exists(FullPath(path)).Should().BeFalse();
            foreach (var thumbnail in ThumbnailHelper.GetAllThumbnailPaths(path))
            {
                File.Exists(FullPath(thumbnail)).Should().BeFalse();
            }
        }
    }
}
