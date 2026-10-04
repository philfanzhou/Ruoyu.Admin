using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

public sealed class AdminSessionDisabledTests
{
    [Theory]
    [InlineData("/api/admin/students", "GET")]
    [InlineData("/API/ADMIN/IMAGE/", "GET")]
    [InlineData("/api/admin/oss-audit/records/1/resolve", "POST")]
    [InlineData("/api/auth/csrf/", "GET")]
    [InlineData("/API/AUTH/SESSION/", "GET")]
    public async Task DisabledApiStopsBeforeAuthenticationOrBusiness(string path, string method)
    {
        var nextCalls = 0;
        var middleware = new AdminSessionMiddleware(_ => { nextCalls++; return Task.CompletedTask; }, new AdminOidcSettings(false, "", "", "", "", false, TimeSpan.Zero));
        var context = new DefaultHttpContext();
        context.Request.Path = path; context.Request.Method = method;
        context.Request.Headers.Authorization = "Bearer forged";
        context.Request.Headers.Cookie = "adminAuthToken=forged; adminSession=forged";
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context, null!);
        Assert.Equal(503, context.Response.StatusCode); Assert.Equal(0, nextCalls);
        context.Response.Body.Position = 0;
        Assert.Equal("{\"error\":\"session_api_disabled\"}", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("/api/auth/logout", "POST")]
    [InlineData("/API/AUTH/LOGOUT/", "GET")]
    [InlineData("/api/auth/logout/csrf", "GET")]
    [InlineData("/api/auth/oidc/logout-callback", "GET")]
    public async Task DisabledLogoutStopsBeforeAuthenticationRevocationOrPrepare(string path, string method)
    {
        var middleware = new AdminLogoutMiddleware(_ => throw new InvalidOperationException("Must not fall through"), new AdminOidcSettings(false, "", "", "", "", false, TimeSpan.Zero));
        var context = new DefaultHttpContext(); context.Request.Path = path; context.Request.Method = method;
        context.Request.Headers.Authorization = "Bearer forged"; context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context, null!);
        Assert.Equal(503, context.Response.StatusCode); context.Response.Body.Position = 0;
        Assert.Equal("{\"error\":\"session_logout_disabled\"}", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("GET")][InlineData("HEAD")][InlineData("PUT")][InlineData("PATCH")]
    [InlineData("DELETE")][InlineData("OPTIONS")][InlineData("TRACE")]
    public async Task EnabledLogoutRejectsOtherMethodsBeforeRevocation(string method)
    {
        var middleware = new AdminLogoutMiddleware(_ => throw new InvalidOperationException("Must not fall through"), new AdminOidcSettings(true, "", "", "", "", false, TimeSpan.Zero) { UseSessionForLogout = true });
        var context = new DefaultHttpContext(); context.Request.Path = "/API/AUTH/LOGOUT/"; context.Request.Method = method;
        await middleware.InvokeAsync(context, null!);
        Assert.Equal(405, context.Response.StatusCode);
    }
}
