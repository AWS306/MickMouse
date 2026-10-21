using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using MickMouse.Web.Services;
using MickMouse.Web.ViewModels;

namespace MickMouse.Web.Controllers;

[Authorize(Roles = "Podcaster,Admin")]
public sealed class StudioController(AppDbContext db, ICommentStore comments, IMediaStore media,
    EpisodeCleanup cleanup, TimeProvider clock, ILogger<StudioController> logger) : Controller
{
    private Guid UserId => EpisodeAccess.UserId(User)!.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private IQueryable<Podcast> OwnPodcasts => User.IsInRole("Admin") ? db.Podcasts : db.Podcasts.Where(x => x.CreatorId == UserId);
    private IQueryable<Episode> OwnEpisodes => User.IsInRole("Admin") ? db.Episodes : db.Episodes.Where(x => x.Podcast!.CreatorId == UserId);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var episodes = await OwnEpisodes.AsNoTracking().Include(x => x.Podcast).Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.ReleaseDateUtc).ToListAsync(ct);
        var podcasts = await OwnPodcasts.AsNoTracking().OrderBy(x => x.Title).ToListAsync(ct);
        var podcastIds = podcasts.Select(x => x.Id).ToList();
        var commentCount = 0; var unavailable = false;
        try { foreach (var episode in episodes) commentCount += (await comments.GetForEpisodeAsync(episode.Id, ct)).Count; }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "Studio comment statistics unavailable"); unavailable = true; }
        return View(new StudioViewModel
        {
            Podcasts = podcasts, Episodes = episodes, TotalPlays = episodes.Sum(x => x.PlayCount),
            TotalSubscribers = await db.Subscriptions.CountAsync(x => podcastIds.Contains(x.PodcastId), ct),
            TotalComments = commentCount, CommentsUnavailable = unavailable,
            PendingCleanup = await OwnEpisodes.CountAsync(x => x.IsDeleted && !x.CleanupCompleted, ct)
        });
    }

    [HttpGet]
    public IActionResult CreatePodcast() => View("PodcastForm", new PodcastFormViewModel());

    [HttpPost]
    public async Task<IActionResult> CreatePodcast(PodcastFormViewModel model, CancellationToken ct)
    {
        CleanPodcastForm(model);
        if (!ModelState.IsValid) return View("PodcastForm", model);
        var podcast = new Podcast
        {
            Title = model.Title, Description = model.Description, HostName = model.HostName,
            Topic = model.Topic, CoverKey = EpisodeAccess.SafeCover(model.CoverKey),
            CreatorId = UserId, CreatedAtUtc = Now
        };
        db.Podcasts.Add(podcast); await db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "Your podcast is ready. Add its first episode.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditPodcast(int id, CancellationToken ct)
    {
        var podcast = await OwnPodcasts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (podcast is null) return NotFound();
        return View("PodcastForm", new PodcastFormViewModel
        { Id = id, Title = podcast.Title, Description = podcast.Description, HostName = podcast.HostName, Topic = podcast.Topic, CoverKey = podcast.CoverKey });
    }

    [HttpPost]
    public async Task<IActionResult> EditPodcast(int id, PodcastFormViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var podcast = await OwnPodcasts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (podcast is null) return NotFound();
        CleanPodcastForm(model);
        if (!ModelState.IsValid) return View("PodcastForm", model);
        podcast.Title = model.Title; podcast.Description = model.Description; podcast.HostName = model.HostName;
        podcast.Topic = model.Topic; podcast.CoverKey = EpisodeAccess.SafeCover(model.CoverKey);
        await db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "Podcast details updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> CreateEpisode(int? podcastId, CancellationToken ct)
    {
        var model = new EpisodeFormViewModel { PodcastId = podcastId ?? 0 };
        await PopulatePodcasts(model, ct);
        return View("EpisodeForm", model);
    }

    [HttpPost, RequestSizeLimit(MediaValidation.MaxBytes + 1048576)]
    public async Task<IActionResult> CreateEpisode(EpisodeFormViewModel model, CancellationToken ct)
    {
        CleanEpisodeForm(model);
        if (!await OwnPodcasts.AnyAsync(x => x.Id == model.PodcastId, ct)) return Forbid();
        if (model.Media is null) ModelState.AddModelError(nameof(model.Media), "Choose an audio or video file.");
        else ValidateUpload(model.Media);
        if (!ModelState.IsValid) { await PopulatePodcasts(model, ct); return View("EpisodeForm", model); }
        string? uploaded = null;
        try
        {
            await using var source = model.Media!.OpenReadStream();
            uploaded = await media.SaveAsync(source, model.Media.FileName, model.Media.ContentType, ct);
            db.Episodes.Add(new Episode
            {
                PodcastId = model.PodcastId, Title = model.Title, Summary = model.Summary, Topic = model.Topic,
                CoverKey = EpisodeAccess.SafeCover(model.CoverKey), ReleaseDateUtc = model.ReleaseDateUtc,
                DurationMinutes = model.DurationMinutes, MediaKey = uploaded,
                MediaContentType = model.Media.ContentType, Status = EpisodeStatus.Pending,
                SourceUrl = model.SourceUrl, MediaCredit = model.MediaCredit, Version = 1
            });
            await db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = "Episode saved and sent for approval.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (uploaded != null) await DeleteOrphan(uploaded, ct);
            logger.LogWarning(error, "Episode creation failed");
            ModelState.AddModelError("", error is InvalidDataException ? error.Message : "The episode could not be saved. Please try again.");
            await PopulatePodcasts(model, ct); return View("EpisodeForm", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditEpisode(int id, CancellationToken ct)
    {
        var episode = await OwnEpisodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (episode is null) return NotFound();
        var model = new EpisodeFormViewModel
        {
            Id = id, Version = episode.Version, PodcastId = episode.PodcastId, Title = episode.Title,
            Summary = episode.Summary, Topic = episode.Topic, CoverKey = episode.CoverKey,
            ReleaseDateUtc = episode.ReleaseDateUtc, DurationMinutes = episode.DurationMinutes,
            ExistingMediaKey = episode.MediaKey, SourceUrl = episode.SourceUrl, MediaCredit = episode.MediaCredit
        };
        await PopulatePodcasts(model, ct); return View("EpisodeForm", model);
    }

    [HttpPost, RequestSizeLimit(MediaValidation.MaxBytes + 1048576)]
    public async Task<IActionResult> EditEpisode(int id, EpisodeFormViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var episode = await OwnEpisodes.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (episode is null) return NotFound();
        if (!await OwnPodcasts.AnyAsync(x => x.Id == model.PodcastId, ct)) return Forbid();
        if (episode.Version != model.Version) return Conflict("This episode changed in another window. Reload it before saving.");
        CleanEpisodeForm(model); model.ExistingMediaKey = episode.MediaKey;
        if (model.Media != null) ValidateUpload(model.Media);
        if (!ModelState.IsValid) { await PopulatePodcasts(model, ct); return View("EpisodeForm", model); }
        string? uploaded = null; var previous = episode.MediaKey;
        try
        {
            if (model.Media != null)
            {
                await using var source = model.Media.OpenReadStream();
                uploaded = await media.SaveAsync(source, model.Media.FileName, model.Media.ContentType, ct);
                episode.MediaKey = uploaded; episode.MediaContentType = model.Media.ContentType;
            }
            episode.Title = model.Title; episode.Summary = model.Summary; episode.Topic = model.Topic;
            episode.PodcastId = model.PodcastId; episode.CoverKey = EpisodeAccess.SafeCover(model.CoverKey);
            episode.ReleaseDateUtc = model.ReleaseDateUtc; episode.DurationMinutes = model.DurationMinutes;
            episode.SourceUrl = model.SourceUrl; episode.MediaCredit = model.MediaCredit;
            episode.Status = EpisodeStatus.Pending; episode.Version++;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (uploaded != null) await DeleteOrphan(uploaded, ct);
            logger.LogWarning(error, "Episode update failed for {EpisodeId}", id);
            ModelState.AddModelError("", error is InvalidDataException ? error.Message : "The episode changed or could not be saved. Reload and try again.");
            await PopulatePodcasts(model, ct); return View("EpisodeForm", model);
        }
        if (uploaded != null && previous != null) await DeleteOrphan(previous, ct);
        TempData["StatusMessage"] = "Episode updated and sent for approval again.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteEpisode(int id, CancellationToken ct)
    {
        var episode = await OwnEpisodes.AsNoTracking().Include(x => x.Podcast).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return episode is null ? NotFound() : View(new DeleteEpisodeViewModel { Episode = episode });
    }

    [HttpPost, ActionName("DeleteEpisode")]
    public async Task<IActionResult> DeleteEpisodeConfirmed(int id, CancellationToken ct)
    {
        var episode = await OwnEpisodes.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (episode is null) return NotFound();
        episode.IsDeleted = true; episode.DeletedAtUtc = Now; episode.Version++;
        await db.SaveChangesAsync(ct);
        var completed = await cleanup.TryCompleteAsync(episode, ct);
        TempData[completed ? "StatusMessage" : "ErrorMessage"] = completed ? "Episode deleted." :
            "Episode removed from browsing. Some stored data still needs cleanup; use Retry cleanup.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RetryCleanup(CancellationToken ct)
    {
        var pending = await OwnEpisodes.Where(x => x.IsDeleted && !x.CleanupCompleted).ToListAsync(ct);
        var done = 0;
        foreach (var episode in pending) if (await cleanup.TryCompleteAsync(episode, ct)) done++;
        TempData[done == pending.Count ? "StatusMessage" : "ErrorMessage"] = $"Cleanup completed for {done} of {pending.Count} episodes.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DeletePodcast(int id, CancellationToken ct)
    {
        var podcast = await OwnPodcasts.AsNoTracking().Include(x => x.Episodes).SingleOrDefaultAsync(x => x.Id == id, ct);
        return podcast is null ? NotFound() : View(new DeletePodcastViewModel { Podcast = podcast });
    }

    [HttpPost, ActionName("DeletePodcast")]
    public async Task<IActionResult> DeletePodcastConfirmed(int id, CancellationToken ct)
    {
        var podcast = await OwnPodcasts.Include(x => x.Episodes).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (podcast is null) return NotFound();
        if (podcast.Episodes.Any(x => !x.IsDeleted || !x.CleanupCompleted))
        {
            TempData["ErrorMessage"] = "Delete the podcast's episodes and complete their cleanup first.";
            return RedirectToAction(nameof(DeletePodcast), new { id });
        }
        db.Episodes.RemoveRange(podcast.Episodes); db.Podcasts.Remove(podcast);
        await db.SaveChangesAsync(ct); TempData["StatusMessage"] = "Podcast deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulatePodcasts(EpisodeFormViewModel model, CancellationToken ct) =>
        model.Podcasts = await OwnPodcasts.AsNoTracking().OrderBy(x => x.Title).ToListAsync(ct);

    private void CleanPodcastForm(PodcastFormViewModel model)
    {
        model.Title = model.Title?.Trim() ?? ""; model.Description = model.Description?.Trim() ?? "";
        model.HostName = model.HostName?.Trim() ?? ""; model.Topic = model.Topic?.Trim() ?? "";
        if (new[] { model.Title, model.Description, model.HostName, model.Topic }.Any(string.IsNullOrWhiteSpace))
            ModelState.AddModelError("", "Fill in each podcast detail.");
    }

    private void CleanEpisodeForm(EpisodeFormViewModel model)
    {
        model.Title = model.Title?.Trim() ?? ""; model.Summary = model.Summary?.Trim() ?? ""; model.Topic = model.Topic?.Trim() ?? "";
        model.SourceUrl = string.IsNullOrWhiteSpace(model.SourceUrl) ? null : model.SourceUrl.Trim();
        model.MediaCredit = string.IsNullOrWhiteSpace(model.MediaCredit) ? null : model.MediaCredit.Trim();
        model.ReleaseDateUtc = DateTime.SpecifyKind(model.ReleaseDateUtc.Date, DateTimeKind.Utc);
        if (new[] { model.Title, model.Summary, model.Topic }.Any(string.IsNullOrWhiteSpace))
            ModelState.AddModelError("", "Fill in each episode detail.");
        if (model.ReleaseDateUtc.Year < 1970 || model.ReleaseDateUtc > Now.AddYears(5))
            ModelState.AddModelError(nameof(model.ReleaseDateUtc), "Choose a release date between 1970 and five years from now.");
        if (model.SourceUrl != null && (!Uri.TryCreate(model.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            ModelState.AddModelError(nameof(model.SourceUrl), "Use a full http or https source URL.");
    }

    private void ValidateUpload(IFormFile file)
    {
        if (!MediaValidation.IsAllowed(file.FileName, file.ContentType, file.Length))
            ModelState.AddModelError("Media", "Choose a supported audio/video file up to 25 MiB. Its type must match its extension.");
    }

    private async Task DeleteOrphan(string key, CancellationToken ct)
    {
        try { await media.DeleteAsync(key, ct); }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "Unused media cleanup needs a manual retry for key {MediaKey}", key); }
    }
}
