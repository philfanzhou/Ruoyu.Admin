using Ruoyu.Admin.ServiceClients;

namespace Admin.WebApi.Authentication;

internal sealed class MistakeSessionException(int statusCode, string error) : MistakeBoundaryException
{
    internal int StatusCode { get; } = statusCode;
    internal string Error { get; } = error;
}
