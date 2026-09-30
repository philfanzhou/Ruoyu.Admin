using Microsoft.AspNetCore.Mvc.ApplicationModels;
using ServiceMantle.Web.Http;

namespace Admin.WebApi.Controllers;

/// <summary>
/// Marks a controller's JSON actions for the ServiceMantle six-header security response
/// baseline. The controller convention below translates this attribute into the library's
/// <see cref="SecurityResponseHeadersMetadata"/> endpoint metadata, which
/// <c>UseServiceMantleSecurityResponseHeaders()</c> (Program.cs, after route selection)
/// collapses onto every routed response of the marked endpoint while headers are unsent.
/// </summary>
/// <remarks>
/// This is the MVC-controller equivalent of the library's
/// <c>RequireServiceMantleSecurityResponseHeaders()</c> convention-builder method (which only
/// covers minimal-API endpoint builders): attribute routing flows controller/action attributes
/// into endpoint metadata, and MVC builds <see cref="ControllerActionDescriptor.EndpointMetadata"/>
/// from each selector's <see cref="SelectorModel.EndpointMetadata"/>, which is exactly where
/// this convention inserts the library marker.
/// Exclusions are deliberate: <see cref="ImageController"/> (browser-native image redirects),
/// the three proxy middlewares' forwarded responses, the SPA / static files, and the anonymous
/// health endpoints carry no marker and keep their existing header behavior.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class RequireSecurityResponseHeadersAttribute : Attribute;

/// <summary>
/// Applies <see cref="SecurityResponseHeadersMetadata"/> to every action of controllers
/// annotated with <see cref="RequireSecurityResponseHeadersAttribute"/>. Explicit opt-in per
/// controller: a new JSON controller must carry the attribute to inherit the baseline.
/// </summary>
internal sealed class AdminSecurityResponseHeadersConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        if (!action.Controller.Attributes.OfType<RequireSecurityResponseHeadersAttribute>().Any())
        {
            return;
        }

        foreach (var selector in action.Selectors)
        {
            if (!selector.EndpointMetadata.OfType<SecurityResponseHeadersMetadata>().Any())
            {
                selector.EndpointMetadata.Add(new SecurityResponseHeadersMetadata());
            }
        }
    }
}
