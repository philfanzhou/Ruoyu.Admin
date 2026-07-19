using Admin.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// Regression tests for controller-level [Authorize] attributes.
///
/// Background: admin_portal relies on JWT Bearer + cookie dual-channel auth with a
/// RequireAuthenticatedUser FallbackPolicy (Program.cs). Although the fallback policy
/// intercepts unauthenticated requests, the project convention requires every
/// /api/admin/* controller to explicitly carry [Authorize] for two reasons:
///   1. Express intent — readers immediately see the auth requirement.
///   2. Extension point — future [Authorize(Roles = "admin")] tightening builds on it.
///
/// These tests fail if someone accidentally removes [Authorize] from a secured
/// controller (e.g. during refactoring or to "fix" a 401 locally).
/// </summary>
public class ControllerAuthorizationTests
{
    /// <summary>
    /// R1 fix: OssUploadRecordController must require authentication.
    /// Route prefix /api/admin/oss-upload-records exposes image download, status reset,
    /// assignment, rotation and legacy cleanup — all admin-only operations.
    /// </summary>
    [Fact]
    public void OssUploadRecordController_HasAuthorizeAttribute()
    {
        var attributes = typeof(OssUploadRecordController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty(
            "OssUploadRecordController (route /api/admin/oss-upload-records) must carry [Authorize] " +
            "to enforce admin authentication. FallbackPolicy alone is insufficient per project convention.");
    }

    /// <summary>
    /// R1 fix: OssAuditController must require authentication.
    /// Route prefix /api/admin/oss-audit exposes trigger, records, resolve, ignore and batch-resolve —
    /// all admin-only operations that can delete OSS objects.
    /// </summary>
    [Fact]
    public void OssAuditController_HasAuthorizeAttribute()
    {
        var attributes = typeof(OssAuditController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty(
            "OssAuditController (route /api/admin/oss-audit) must carry [Authorize] " +
            "to enforce admin authentication. Cleanup endpoints delete OSS objects, " +
            "so explicit auth is mandatory.");
    }

    /// <summary>
    /// IdentityAccountsController manages Identity accounts (link user, create student account, etc).
    /// Route prefix /api/admin exposes account creation and linking — admin-only operations.
    /// </summary>
    [Fact]
    public void IdentityAccountsController_HasAuthorizeAttribute()
    {
        var attributes = typeof(IdentityAccountsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty(
            "IdentityAccountsController (route /api/admin) must carry [Authorize] " +
            "to enforce admin authentication on Identity account management.");
    }

    /// <summary>
    /// EnumOptionsController exposes enum lookups (upload status, grade, subject, etc).
    /// Although low-risk (read-only), it sits under /api/admin/* and follows the
    /// project convention of explicit [Authorize] on all admin controllers.
    /// </summary>
    [Fact]
    public void EnumOptionsController_HasAuthorizeAttribute()
    {
        var attributes = typeof(EnumOptionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty(
            "EnumOptionsController (route /api/admin/enum-options) must carry [Authorize] " +
            "per project convention: all /api/admin/* controllers require explicit auth.");
    }

    /// <summary>
    /// MistakeController exposes mistake list/review operations.
    /// Route prefix /api/admin/mistakes — admin-only operations.
    /// </summary>
    [Fact]
    public void MistakeController_HasAuthorizeAttribute()
    {
        var attributes = typeof(MistakeController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty(
            "MistakeController (route /api/admin/mistakes) must carry [Authorize] " +
            "to enforce admin authentication on mistake review operations.");
    }

    /// <summary>
    /// StudentsController exposes student CRUD operations.
    /// Route prefix /api/admin/students — admin-only operations.
    /// </summary>
    [Fact]
    public void StudentsController_HasAuthorizeAttribute()
    {
        var attributes = typeof(StudentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty(
            "StudentsController (route /api/admin/students) must carry [Authorize] " +
            "to enforce admin authentication on student management.");
    }

    /// <summary>
    /// Reference controller: ImageController already carries [Authorize] (cookie+JWT dual-channel).
    /// Included here as a positive control to confirm the test mechanism works.
    /// </summary>
    [Fact]
    public void ImageController_HasAuthorizeAttribute()
    {
        var attributes = typeof(ImageController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        attributes.Should().NotBeEmpty();
    }
}
