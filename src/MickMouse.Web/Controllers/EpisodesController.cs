using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using MickMouse.Web.Services;
using MickMouse.Web.ViewModels;

namespace MickMouse.Web.Controllers;

public sealed class EpisodesController(AppDbContext db, ICommentStore comments, IMediaStore media,
    UserManager<ApplicationUser> users, TimeProvider clock, ILogger<EpisodesController> logger) : Controller
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private IQueryable<Episode> PublicEpisodes => db.Episodes.Where(x =>
        !x.IsDeleted && x.Status == EpisodeStatus.Published && x.ReleaseDateUtc <= Now);

    [HttpGet]
    public async Task<IActionResult> Index(string? topic, string? host, string sort = "latest", int page = 1,
        CancellationToken ct = default)
    {
        topic = topic?.Trim(); host = host?.Trim();
        if (topic?.Length > 80 || host?.Length > 100) return BadRequest("Search text is too long.");
        page = Math.Clamp(page, 1, 10000);
        sort = sort == "popular" ? "popular" : "latest";
        var all = PublicEpisodes.AsNoTracking();
        var filtered = all.Include(x => x.Podcast).AsQueryable();
        if (!string.IsNullOrEmpty(topic))
        {
            var search = topic.ToLowerInvariant();
            filtered = filtered.Where(x => x.Topic.ToLower().Contains(search));
        }
        if (!string.IsNullOrEmpty(host))
        {
            var search = host.ToLowerInvariant();
            filtered = filtered.Where(x => x.Podcast!.HostName.ToLower().Contains(search));
        }
        var ordered = sort == "popular"
            ? filtered.OrderByDescending(x => x.PlayCount).ThenByDescending(x => x.ReleaseDateUtc).ThenByDescending(x => x.Id)
            : filtered.OrderByDescending(x => x.ReleaseDateUtc).ThenByDescending(x => x.Id);
        const int pageSize = 8;
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize + 1).ToListAsync(ct);
        var total = await filtered.CountAsync(ct);
        return View(new BrowseViewModel
        {
            Episodes = items.Take(pageSize).ToList(), Featured = await all.Include(x => x.Podcast)
                .OrderByDescending(x => x.PlayCount).ThenBy(x => x.Id).FirstOrDefaultAsync(ct),
            Topics = await all.Select(x => x.Topic).Distinct().OrderBy(x => x).ToListAsync(ct),
            Topic = topic, Host = host, Sort = sort, Page = page, HasNext = items.Count > pageSize,
            TotalCount = total, PodcastCount = await db.Podcasts.CountAsync(ct),
            TotalPlays = await all.SumAsync(x => (long?)x.PlayCount, ct) ?? 0
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var episode = await db.Episodes.AsNoTracking().Include(x => x.Podcast).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (episode is null || !EpisodeAccess.CanView(User, episode, Now)) return NotFound();
        IReadOnlyList<EpisodeComment> entries = [];
        string? playbackUrl = null;
        bool commentsUnavailable = false, mediaUnavailable = false;
        try { entries = await comments.GetForEpisodeAsync(id, ct); }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "Comments unavailable for episode {EpisodeId}", id); commentsUnavailable = true; }
        if (episode.MediaKey != null)
        {
            try { playbackUrl = await media.GetPlaybackUrlAsync(episode.MediaKey, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning(error, "Media unavailable for episode {EpisodeId}", id); mediaUnavailable = true; }
        }
        else mediaUnavailable = true;
        var userId = EpisodeAccess.UserId(User);
        return View(new EpisodeDetailViewModel
        {
            Episode = episode, Comments = entries, PlaybackUrl = playbackUrl,
            CommentsUnavailable = commentsUnavailable, MediaUnavailable = mediaUnavailable,
            CurrentUserId = userId, NowUtc = Now, CanManage = EpisodeAccess.CanManage(User, episode.Podcast!.CreatorId),
            IsLiked = userId != null && await db.EpisodeLikes.AnyAsync(x => x.EpisodeId == id && x.UserId == userId, ct),
            IsSubscribed = userId != null && await db.Subscriptions.AnyAsync(x => x.PodcastId == episode.PodcastId && x.UserId == userId, ct)
        });
    }

    [HttpPost, EnableRateLimiting("plays")]
    public async Task<IActionResult> Play(int id, CancellationToken ct)
    {
        var changed = await PublicEpisodes.Where(x => x.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.PlayCount, x => x.PlayCount + 1), ct);
        if (changed == 0) return NotFound();
        var count = await db.Episodes.Where(x => x.Id == id).Select(x => x.PlayCount).SingleAsync(ct);
        return Json(new { playCount = count });
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Like(int id, CancellationToken ct)
    {
        if (!await PublicEpisodes.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var userId = EpisodeAccess.UserId(User)!.Value;
        var existing = await db.EpisodeLikes.SingleOrDefaultAsync(x => x.EpisodeId == id && x.UserId == userId, ct);
        if (existing is null) db.EpisodeLikes.Add(new EpisodeLike { UserId = userId, EpisodeId = id, CreatedAtUtc = Now });
        else db.EpisodeLikes.Remove(existing);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error)
        {
            logger.LogWarning(error, "Concurrent like change for episode {EpisodeId}", id);
            TempData["ErrorMessage"] = "That like changed in another request. Please check its current state.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize, EnableRateLimiting("comments")]
    public async Task<IActionResult> Comment(int id, string? text, CancellationToken ct)
    {
        var episode = await PublicEpisodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (episode is null) return NotFound();
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000)
        {
            TempData["ErrorMessage"] = "Write a comment between 1 and 2,000 characters.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDisabled) return Challenge();
        try
        {
            await comments.AddAsync(new EpisodeComment
            {
                EpisodeId = id, PodcastId = episode.PodcastId, CommentId = Guid.NewGuid().ToString("N"),
                UserId = user.Id, AuthorName = user.DisplayName, Text = text, CreatedAtUtc = Now, Version = 1
            }, ct);
            if (!await db.Users.AnyAsync(author => author.Id == user.Id && !author.IsDisabled, ct))
            {
                await comments.DeleteForUserAsync(user.Id, ct);
                return Challenge();
            }
            // A delete can finish while the separate comment store is saving.
            // Mark the tombstone for another cleanup pass if this write raced it.
            if (await db.Episodes.AnyAsync(x => x.Id == id && x.IsDeleted, ct))
            {
                await db.Episodes.Where(x => x.Id == id && x.IsDeleted)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.CleanupCompleted, false), ct);
                return NotFound();
            }
            TempData["StatusMessage"] = "Your comment is posted. You can edit it for 24 hours.";
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Comment creation failed for episode {EpisodeId}", id);
            TempData["ErrorMessage"] = "Your comment could not be saved. Please try again.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> EditComment(int episodeId, string commentId, CancellationToken ct)
    {
        var episode = await PublicEpisodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == episodeId, ct);
        if (episode is null) return NotFound();
        var comment = (await comments.GetForEpisodeAsync(episodeId, ct)).FirstOrDefault(x => x.CommentId == commentId);
        if (comment is null) return NotFound();
        if (!comment.CanEdit(EpisodeAccess.UserId(User)!.Value, Now)) return Forbid();
        return View(new EditCommentViewModel
        {
            EpisodeId = episodeId, CommentId = commentId, Version = comment.Version, Text = comment.Text,
            EpisodeTitle = episode.Title, EditUntilUtc = comment.CreatedAtUtc.AddHours(24)
        });
    }

    [HttpPost, Authorize, EnableRateLimiting("comments")]
    public async Task<IActionResult> EditComment(EditCommentViewModel model, CancellationToken ct)
    {
        if (!await PublicEpisodes.AnyAsync(x => x.Id == model.EpisodeId, ct)) return NotFound();
        model.Text = model.Text?.Trim() ?? "";
        if (!ModelState.IsValid || model.Text.Length == 0) return BadRequest("Enter a comment between 1 and 2,000 characters.");
        var saved = await comments.TryEditAsync(model.EpisodeId, model.CommentId,
            EpisodeAccess.UserId(User)!.Value, model.Text, model.Version, Now, ct);
        if (!saved)
        {
            Response.StatusCode = 409;
            return View("~/Views/Shared/Error.cshtml", new ErrorViewModel
            {
                StatusCode = 409, Title = "This comment cannot be changed.",
                Message = "Only its author can edit during the first 24 hours. Another edit may also have changed its version."
            });
        }
        TempData["StatusMessage"] = "Your comment has been updated.";
        return RedirectToAction(nameof(Details), new { id = model.EpisodeId });
    }
}
