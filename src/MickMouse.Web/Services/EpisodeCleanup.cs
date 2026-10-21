using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;

namespace MickMouse.Web.Services;

public sealed class EpisodeCleanup(AppDbContext db, ICommentStore comments, IMediaStore media,
    ILogger<EpisodeCleanup> logger)
{
    public async Task<bool> TryCompleteAsync(Episode episode, CancellationToken ct = default)
    {
        if (!episode.IsDeleted) throw new InvalidOperationException("Only deleted episodes can be cleaned up.");
        if (episode.CleanupCompleted) return true;
        try
        {
            await comments.DeleteForEpisodeAsync(episode.Id, ct);
            if (episode.MediaKey != null) await media.DeleteAsync(episode.MediaKey, ct);
            await db.EpisodeLikes.Where(like => like.EpisodeId == episode.Id).ExecuteDeleteAsync(ct);
            episode.CleanupCompleted = true;
            episode.MediaKey = null;
            episode.Version++;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Cleanup for deleted episode {EpisodeId} is pending a retry.", episode.Id);
            return false;
        }
    }
}
