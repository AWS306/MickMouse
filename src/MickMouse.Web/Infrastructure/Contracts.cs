using MickMouse.Web.Models;

namespace MickMouse.Web.Infrastructure;

public interface ICommentStore
{
    Task<IReadOnlyList<EpisodeComment>> GetForEpisodeAsync(int episodeId, CancellationToken ct = default);
    Task AddAsync(EpisodeComment comment, CancellationToken ct = default);
    Task<bool> TryEditAsync(int episodeId, string commentId, Guid actorId, string text,
        int expectedVersion, DateTime nowUtc, CancellationToken ct = default);
    Task DeleteForEpisodeAsync(int episodeId, CancellationToken ct = default);
    Task DeleteForUserAsync(Guid userId, CancellationToken ct = default);
}

public interface IMediaStore
{
    Task<string> SaveAsync(Stream source, string originalName, string contentType, CancellationToken ct = default);
    Task<string> GetPlaybackUrlAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

internal static class CommentRules
{
    internal const int MaxTextLength = 2000;

    internal static string ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > MaxTextLength)
            throw new ArgumentException("A comment must contain between 1 and 2,000 characters.", nameof(text));
        return text.Trim();
    }

    internal static void Validate(EpisodeComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        if (comment.EpisodeId <= 0 || comment.PodcastId <= 0 || comment.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(comment.CommentId) || comment.CommentId.Length > 64 ||
            string.IsNullOrWhiteSpace(comment.AuthorName) || comment.AuthorName.Length > 100 ||
            comment.Version < 1 || comment.CreatedAtUtc == default)
            throw new ArgumentException("The comment metadata is invalid.", nameof(comment));
        comment.Text = ValidateText(comment.Text);
    }
}
