namespace MickMouse.Web.Infrastructure;

public static class MediaValidation
{
    public const long MaxBytes = 25L * 1024 * 1024;
    private static readonly Dictionary<string, string[]> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = ["audio/mpeg", "audio/mp3"],
        [".wav"] = ["audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave"],
        [".ogg"] = ["audio/ogg", "application/ogg"],
        [".m4a"] = ["audio/mp4", "audio/x-m4a"],
        [".mp4"] = ["video/mp4", "audio/mp4"],
        [".webm"] = ["video/webm", "audio/webm"]
    };

    public static bool IsAllowed(string fileName, string contentType, long size) =>
        !string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(contentType) &&
        size > 0 && size <= MaxBytes &&
        ContentTypes.TryGetValue(Path.GetExtension(fileName), out var allowed) &&
        allowed.Contains(NormalizeContentType(contentType), StringComparer.OrdinalIgnoreCase);

    internal static string NormalizeContentType(string contentType) => contentType.Split(';', 2)[0].Trim().ToLowerInvariant();

    internal static async Task<MemoryStream> ReadUploadAsync(Stream source, string fileName,
        string contentType, CancellationToken ct)
    {
        var result = new MemoryStream();
        try
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                if (result.Length + read > MaxBytes) throw new InvalidDataException("Media must be 25 MiB or smaller.");
                await result.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (!IsAllowed(fileName, contentType, result.Length) || !HasExpectedHeader(result, Path.GetExtension(fileName)))
                throw new InvalidDataException("The file contents do not match a supported audio or video format.");
            result.Position = 0;
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }

    private static bool HasExpectedHeader(MemoryStream stream, string extension)
    {
        var bytes = stream.GetBuffer().AsSpan(0, (int)Math.Min(stream.Length, 16));
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => bytes.StartsWith("ID3"u8) || (bytes.Length >= 2 && bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0),
            ".wav" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8),
            ".ogg" => bytes.StartsWith("OggS"u8),
            ".m4a" or ".mp4" => bytes.Length >= 8 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8),
            ".webm" => bytes.Length >= 4 && bytes[0] == 0x1a && bytes[1] == 0x45 && bytes[2] == 0xdf && bytes[3] == 0xa3,
            _ => false
        };
    }
}

internal static class StorageKey
{
    internal static string Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('\\')) throw new ArgumentException("Invalid media key.", nameof(key));
        var parts = key.Split('/');
        if (parts.Length != 2 || parts[0] is not ("uploads" or "samples") || parts[1].Length is < 1 or > 120 ||
            parts[1][0] == '.' || parts[1].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')) ||
            parts[1].Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid media key.", nameof(key));
        return key;
    }
}
