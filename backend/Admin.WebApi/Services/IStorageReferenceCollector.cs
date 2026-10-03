namespace Admin.WebApi.Services;

public interface IStorageReferenceCollector
{
    Task<StorageReferenceCollection> CollectAsync(CancellationToken cancellationToken);
}
