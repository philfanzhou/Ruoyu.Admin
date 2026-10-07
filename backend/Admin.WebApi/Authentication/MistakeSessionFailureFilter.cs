using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Admin.WebApi.Authentication;

internal sealed class MistakeSessionFailureFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MistakeSessionException error) return;
        context.Result = new ObjectResult(new { error = error.Error }) { StatusCode = error.StatusCode };
        context.ExceptionHandled = true;
    }
}
