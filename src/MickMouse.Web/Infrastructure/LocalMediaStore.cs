namespace MickMouse.Web.Infrastructure;

public sealed class LocalMediaStore : IMediaStore
{
    private readonly string _directory;

    public LocalMediaStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(Path.Combine(_directory, "uploads"));
    }

    public async Task<string> SaveAsync(Stream source, string originalName, string contentType, CancellationToken ct = default)
    {
        await using var upload = await MediaValidation.ReadUploadAsync(source, originalName, contentType, ct);
        var key = "uploads/" + Guid.NewGuid().ToString("N") + Path.GetExtension(originalName).ToLowerInvariant();
        var path = GetLocalPath(key);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous);
            await upload.CopyToAsync(file, ct);
            return key;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public Task<string> GetPlaybackUrlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        StorageKey.Validate(key);
        return Task.FromResult("/media/play?key=" + Uri.EscapeDataString(key));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        StorageKey.Validate(key);
        if (!key.StartsWith("samples/", StringComparison.Ordinal)) File.Delete(GetLocalPath(key));
        return Task.CompletedTask;
    }

    public string GetLocalPath(string key) => Path.Combine(_directory, StorageKey.Validate(key).Replace('/', Path.DirectorySeparatorChar));
}
