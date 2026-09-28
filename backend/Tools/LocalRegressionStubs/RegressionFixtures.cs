using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruoyu.Admin.LocalRegressionStubs;

/// <summary>
/// 固定夹具数据：种子（写库 + 写 OSS 本地目录 + 写 fixtures JSON）与替身（serve 读同一份
/// fixtures JSON）共享的唯一数据源。所有值都是确定性的，保证种子重复执行结果一致（幂等）。
/// </summary>
public static class RegressionFixtures
{
    public const string SeedTriggerType = "regression-seed";

    // Fixed epoch (2025-09-27T10:06:40Z) so repeated seed runs converge to identical rows.
    public const long FixedEpochSeconds = 1759000000;

    public const string StudentId = "reg-student-0001";

    // OSS object paths. Buckets are limited to uploads/mistakes: OssAuditWorker's startup
    // CleanupUnauditedBucketAuditRecordsAsync removes audit records in any other bucket.
    // None of the paths may match the legacy shape uploads/homework/{guid}/{guid}/reviews/{guid}.jpg,
    // which CleanupLegacyHomeworkReviewImagesAsync really deletes ~2 minutes after startup.
    public const string OrphanObjectPath = "uploads/regression/orphan-object.jpg";
    public const string UploadReferencedPath = "uploads/regression/referenced-by-upload.jpg";
    public const string HomeworkReferencedPath = "uploads/regression/referenced-by-homework.jpg";
    public const string MistakeReferencedPath = "mistakes/regression/referenced-by-mistake.jpg";
    public const string IgnoredObjectPath = "uploads/regression/ignored-object.jpg";

    // Mirrors Ruoyu.Admin.Common ThumbnailHelper.SizeMap keys. Keep in sync manually; the
    // tool intentionally does not reference Ruoyu.Admin.Common to stay dependency-light.
    public static readonly string[] ThumbnailSuffixes = ["thumbnail", "small", "medium"];

    // A real 1x1 JPEG so image previews keep working during regression.
    private static readonly byte[] ImageBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwcJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDs0NDT/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==");

    /// <summary>Objects the seed writes under OSS_LOCAL_PATH (original + derived thumbnails).</summary>
    public static IReadOnlyList<string> AllObjectPaths =
    [
        OrphanObjectPath,
        UploadReferencedPath,
        HomeworkReferencedPath,
        MistakeReferencedPath,
        IgnoredObjectPath,
    ];

    public static byte[] ObjectContent() => ImageBytes;

    /// <summary>Pending (Status=0) audit records the seed inserts so resolve paths can be exercised.</summary>
    public static IReadOnlyList<(string Path, string Bucket)> PendingRecords =>
    [
        (OrphanObjectPath, "uploads"),
        (UploadReferencedPath, "uploads"),
        (HomeworkReferencedPath, "uploads"),
        (MistakeReferencedPath, "mistakes"),
    ];

    public static (string Path, string Bucket, string Note) IgnoredRecord =>
        (IgnoredObjectPath, "uploads", "本地回归夹具：非待处理（已忽略）记录");

    /// <summary>The fixtures JSON contract shared by seed (writer) and serve (reader).</summary>
    public sealed class FixtureFile
    {
        [JsonPropertyName("uploads")]
        public UploadsFixture Uploads { get; set; } = new();

        [JsonPropertyName("mistakes")]
        public MistakesFixture Mistakes { get; set; } = new();

        [JsonPropertyName("homeworkImageReferences")]
        public HomeworkReferencesFixture HomeworkImageReferences { get; set; } = new();
    }

    public sealed class UploadsFixture
    {
        [JsonPropertyName("items")]
        public List<UploadItem> Items { get; set; } = new();
    }

    public sealed class UploadItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("studentId")]
        public string StudentId { get; set; } = "";

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("imageEntries")]
        public List<ImageEntry> ImageEntries { get; set; } = new();

        [JsonPropertyName("imageRotations")]
        public List<int> ImageRotations { get; set; } = new();

        [JsonPropertyName("comments")]
        public string Comments { get; set; } = "";

        [JsonPropertyName("returnReason")]
        public string ReturnReason { get; set; } = "";

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = "";

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = "";
    }

    public sealed class ImageEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "mistake";
    }

    public sealed class MistakesFixture
    {
        [JsonPropertyName("items")]
        public List<MistakeItem> Items { get; set; } = new();
    }

    public sealed class MistakeItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("studentId")]
        public string StudentId { get; set; } = "";

        [JsonPropertyName("subject")]
        public int Subject { get; set; }

        [JsonPropertyName("grade")]
        public int Grade { get; set; }

        [JsonPropertyName("sourceUploadId")]
        public string SourceUploadId { get; set; } = "";

        [JsonPropertyName("sourceRegions")]
        public List<SourceRegion> SourceRegions { get; set; } = new();

        [JsonPropertyName("reviewStatus")]
        public int ReviewStatus { get; set; }

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = "";

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = "";
    }

    public sealed class SourceRegion
    {
        [JsonPropertyName("sourceImagePath")]
        public string SourceImagePath { get; set; } = "";

        [JsonPropertyName("boundingBox")]
        public BoundingBox? BoundingBox { get; set; }
    }

    public sealed class BoundingBox
    {
        [JsonPropertyName("x1")]
        public int X1 { get; set; }

        [JsonPropertyName("y1")]
        public int Y1 { get; set; }

        [JsonPropertyName("x2")]
        public int X2 { get; set; }

        [JsonPropertyName("y2")]
        public int Y2 { get; set; }
    }

    public sealed class HomeworkReferencesFixture
    {
        [JsonPropertyName("paths")]
        public List<string> Paths { get; set; } = new();
    }

    public static FixtureFile Build()
    {
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(FixedEpochSeconds).UtcDateTime.ToString("o");
        return new FixtureFile
        {
            Uploads = new UploadsFixture
            {
                Items =
                [
                    new UploadItem
                    {
                        Id = "reg-upl-0001",
                        StudentId = StudentId,
                        Status = 1,
                        ImageEntries = [new ImageEntry { Path = UploadReferencedPath, Type = "mistake" }],
                        ImageRotations = [0],
                        Comments = "本地回归夹具：被上传记录引用",
                        CreatedAt = createdAt,
                        UpdatedAt = createdAt,
                    },
                ],
            },
            Mistakes = new MistakesFixture
            {
                Items =
                [
                    new MistakeItem
                    {
                        Id = "reg-mis-0001",
                        StudentId = StudentId,
                        Subject = 1,
                        Grade = 7,
                        SourceUploadId = "reg-upl-0001",
                        SourceRegions =
                        [
                            new SourceRegion
                            {
                                SourceImagePath = MistakeReferencedPath,
                                BoundingBox = new BoundingBox { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 },
                            },
                        ],
                        ReviewStatus = 2,
                        CreatedAt = createdAt,
                        UpdatedAt = createdAt,
                    },
                ],
            },
            HomeworkImageReferences = new HomeworkReferencesFixture
            {
                Paths = [HomeworkReferencedPath],
            },
        };
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(Build(), JsonOptions));
    }

    public static FixtureFile ReadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"找不到夹具文件 {fullPath}。请先运行 seed 模式生成它（见 docs/development/LocalRegressionEnv.md）。");
        }
        return JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidOperationException($"夹具文件 {fullPath} 内容无效。");
    }
}
