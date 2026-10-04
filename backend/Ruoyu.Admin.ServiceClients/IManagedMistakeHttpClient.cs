namespace Ruoyu.Admin.ServiceClients;

public interface IManagedMistakeHttpClient
{
    Task<ManagedMistakeResult> SubmitAsync(ManagedMistakeUpload upload, string accessToken, CancellationToken ct);
}
