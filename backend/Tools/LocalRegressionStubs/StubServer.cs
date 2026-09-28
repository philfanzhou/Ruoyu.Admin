using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Ruoyu.Admin.LocalRegressionStubs;

/// <summary>
/// 单进程本地替身：一个 Kestrel 进程按回环端口分片提供 Student（:5005）/ Mistake（:5007）/
/// Homework（:5009）三个只读引用查询，路由与响应形状逐字镜像 OssAuditWorker /
/// OssAuditController 聚合所用的客户端契约（StudentHttpClient.GetAllUploadRecordsAsync、
/// MistakeHttpClient.GetMistakeItemListAsync、HomeworkReferenceClient.GetAllImagePathsAsync）。
/// 不模拟下游鉴权、限流与错误形态；停掉进程即产生「来源不可达」分支。
/// </summary>
public static class StubServer
{
    public const int DefaultStudentPort = 5005;
    public const int DefaultMistakePort = 5007;
    public const int DefaultHomeworkPort = 5009;

    public static WebApplication Build(
        RegressionFixtures.FixtureFile fixtures,
        bool mistakeOutage = false,
        int studentPort = DefaultStudentPort,
        int mistakePort = DefaultMistakePort,
        int homeworkPort = DefaultHomeworkPort)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenLocalhost(studentPort);
            kestrel.ListenLocalhost(mistakePort);
            kestrel.ListenLocalhost(homeworkPort);
        });

        var app = builder.Build();

        // The three contract routes are disjoint, so each handler only guards that the request
        // arrived on the loopback port that plays that service (otherwise 404, matching a real
        // downstream that would not expose the other service's route).

        // Student stand-in: GET /api/uploads?page=&pageSize= → items[].imageEntries[].path + totalCount.
        app.MapGet("/api/uploads", (HttpContext ctx) =>
        {
            if (ctx.Connection.LocalPort != studentPort)
            {
                return Results.NotFound();
            }
            var (page, pageSize) = ReadPaging(ctx, sizeKey: "pageSize");
            var items = fixtures.Uploads.Items;
            var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Results.Json(new
            {
                success = true,
                data = new
                {
                    items = pageItems.Select(item => new
                    {
                        id = item.Id,
                        studentId = item.StudentId,
                        status = item.Status,
                        imageEntries = item.ImageEntries.Select(entry => new
                        {
                            path = entry.Path,
                            type = entry.Type,
                        }),
                        imageRotations = item.ImageRotations,
                        comments = item.Comments,
                        returnReason = item.ReturnReason,
                        createdAt = item.CreatedAt,
                        updatedAt = item.UpdatedAt,
                    }),
                    totalCount = items.Count,
                    page,
                    pageSize,
                },
            });
        });

        // Mistake stand-in: GET /api/mistakes?page=&size= → items[].sourceRegions[].sourceImagePath
        // + pageMeta.totalCount.
        //
        // mistakeOutage returns 503 with a non-JSON body: MistakeHttpClient's ReadFromJsonAsync
        // then throws JsonException (not HttpRequestException), which propagates to the audit
        // sweep / delete recheck and yields the deterministic "Mistake service unavailable"
        // abort / 502 branch. A plain connection refusal is NOT equivalent today:
        // MistakeHttpClient swallows HttpRequestException and degrades to an empty reference
        // set (tracked separately in the issue tracker).
        app.MapGet("/api/mistakes", (HttpContext ctx) =>
        {
            if (ctx.Connection.LocalPort != mistakePort)
            {
                return Results.NotFound();
            }
            if (mistakeOutage)
            {
                return Results.Text(
                    "simulated mistake service outage (restart serve without --mistake-outage to restore)",
                    "text/plain",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            var (page, size) = ReadPaging(ctx, sizeKey: "size");
            var items = fixtures.Mistakes.Items;
            var pageItems = items.Skip((page - 1) * size).Take(size).ToList();
            return Results.Json(new
            {
                success = true,
                data = new
                {
                    items = pageItems.Select(item => new
                    {
                        id = item.Id,
                        studentId = item.StudentId,
                        subject = item.Subject,
                        grade = item.Grade,
                        sourceUploadId = item.SourceUploadId,
                        sourceRegions = item.SourceRegions.Select(region => new
                        {
                            sourceImagePath = region.SourceImagePath,
                            boundingBox = region.BoundingBox is null
                                ? null
                                : new
                                {
                                    x1 = region.BoundingBox.X1,
                                    y1 = region.BoundingBox.Y1,
                                    x2 = region.BoundingBox.X2,
                                    y2 = region.BoundingBox.Y2,
                                },
                        }),
                        reviewStatus = item.ReviewStatus,
                        createdAt = item.CreatedAt,
                        updatedAt = item.UpdatedAt,
                    }),
                    pageMeta = new
                    {
                        page,
                        size,
                        totalCount = items.Count,
                        totalPages = (int)Math.Ceiling(items.Count / (double)size),
                    },
                },
            });
        });

        // Homework stand-in: GET /api/admin/storage/image-references?page=&size=
        // → { success, data: { paths[], hasMore } }.
        app.MapGet("/api/admin/storage/image-references", (HttpContext ctx) =>
        {
            if (ctx.Connection.LocalPort != homeworkPort)
            {
                return Results.NotFound();
            }
            var (page, size) = ReadPaging(ctx, sizeKey: "size");
            var paths = fixtures.HomeworkImageReferences.Paths;
            var pagePaths = paths.Skip((page - 1) * size).Take(size).ToList();
            return Results.Json(new
            {
                success = true,
                data = new
                {
                    paths = pagePaths,
                    hasMore = page * size < paths.Count,
                },
            });
        });

        app.MapGet("/", (HttpContext ctx) => Results.Text(
            $"local regression stub on port {ctx.Connection.LocalPort} " +
            "(contracts: /api/uploads, /api/mistakes, /api/admin/storage/image-references)"));

        return app;
    }

    private static (int page, int size) ReadPaging(HttpContext ctx, string sizeKey)
    {
        var page = int.TryParse(ctx.Request.Query["page"], out var p) && p > 0 ? p : 1;
        var size = int.TryParse(ctx.Request.Query[sizeKey], out var s) && s > 0 ? s : 20;
        return (page, size);
    }
}
