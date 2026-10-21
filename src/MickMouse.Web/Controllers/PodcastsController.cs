using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Models;
using MickMouse.Web.Services;

namespace MickMouse.Web.Controllers;

[Authorize]
public sealed class PodcastsController(AppDbContext db, TimeProvider clock, ILogger<PodcastsController> logger) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Subscribe(int id, int? episodeId, CancellationToken ct)
    {
        if (!await db.Podcasts.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var userId = EpisodeAccess.UserId(User)!.Value;
        var existing = await db.Subscriptions.SingleOrDefaultAsync(x => x.PodcastId == id && x.UserId == userId, ct);
        if (existing is null) db.Subscriptions.Add(new Subscription
        { UserId = userId, PodcastId = id, CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
        else db.Subscriptions.Remove(existing);
        try { await db.SaveChangesAsync(ct); TempData["StatusMessage"] = existing is null ? "Podcast added to your subscriptions." : "Subscription removed."; }
        catch (DbUpdateException error)
        { logger.LogWarning(error, "Concurrent subscription change for podcast {PodcastId}", id); TempData["ErrorMessage"] = "The subscription changed in another request. Please check your profile."; }
        return episodeId is > 0 ? RedirectToAction("Details", "Episodes", new { id = episodeId }) : RedirectToAction("Profile", "Account");
    }
}
