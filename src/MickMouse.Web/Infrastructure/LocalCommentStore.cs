using System.Collections.Concurrent;
using System.Text.Json;
using MickMouse.Web.Models;

namespace MickMouse.Web.Infrastructure;

public sealed class LocalCommentStore : ICommentStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly SemaphoreSlim _gate;

    public LocalCommentStore(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        _file = Path.Combine(fullDirectory, "comments.json");
        _gate = Locks.GetOrAdd(_file, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<IReadOnlyList<EpisodeComment>> GetForEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await ReadAsync(ct)).Where(comment => comment.EpisodeId == episodeId)
                .OrderByDescending(comment => comment.CreatedAtUtc).ThenBy(comment => comment.CommentId).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task AddAsync(EpisodeComment comment, CancellationToken ct = default)
    {
        CommentRules.Validate(comment);
        await _gate.WaitAsync(ct);
        try
        {
            var comments = await ReadAsync(ct);
            if (comments.Any(existing => existing.EpisodeId == comment.EpisodeId && existing.CommentId == comment.CommentId))
                throw new InvalidOperationException("This comment already exists.");
            comments.Add(comment);
            await WriteAsync(comments, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TryEditAsync(int episodeId, string commentId, Guid actorId, string text,
        int expectedVersion, DateTime nowUtc, CancellationToken ct = default)
    {
        text = CommentRules.ValidateText(text);
        await _gate.WaitAsync(ct);
        try
        {
            var comments = await ReadAsync(ct);
            var comment = comments.SingleOrDefault(item => item.EpisodeId == episodeId && item.CommentId == commentId);
            if (comment is null || !comment.CanEdit(actorId, nowUtc) || comment.Version != expectedVersion ||
                comment.Version == int.MaxValue) return false;
            comment.Text = text;
            comment.UpdatedAtUtc = nowUtc;
            comment.Version++;
            await WriteAsync(comments, ct);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteForEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var comments = await ReadAsync(ct);
            if (comments.RemoveAll(comment => comment.EpisodeId == episodeId) > 0)
                await WriteAsync(comments, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A user ID is required.", nameof(userId));
        await _gate.WaitAsync(ct);
        try
        {
            var comments = await ReadAsync(ct);
            if (comments.RemoveAll(comment => comment.UserId == userId) > 0)
                await WriteAsync(comments, ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<EpisodeComment>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_file)) return [];
        await using var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<List<EpisodeComment>>(stream, JsonOptions, ct)
            ?? throw new InvalidDataException("The local comment file is empty or invalid.");
    }

    private async Task WriteAsync(List<EpisodeComment> comments, CancellationToken ct)
    {
        var temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, comments, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporary, _file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
