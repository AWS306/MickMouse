using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using MickMouse.Web.Services;
using MickMouse.Web.ViewModels;

namespace MickMouse.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(AppDbContext db, UserManager<ApplicationUser> users,
    ICommentStore comments, ILogger<AdminController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var active = await db.Episodes.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync(ct);
        var count = 0; var unavailable = false;
        try { foreach (var episode in active) count += (await comments.GetForEpisodeAsync(episode.Id, ct)).Count; }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "Admin comment statistics unavailable"); unavailable = true; }
        return View(new AdminViewModel
        {
            Users = await db.Users.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct),
            PendingEpisodes = await db.Episodes.AsNoTracking().Include(x => x.Podcast)
                .Where(x => !x.IsDeleted && x.Status == EpisodeStatus.Pending).OrderBy(x => x.Id).ToListAsync(ct),
            TopEpisodes = await db.Episodes.AsNoTracking().Include(x => x.Podcast).Where(x => !x.IsDeleted && x.Status == EpisodeStatus.Published)
                .OrderByDescending(x => x.PlayCount).ThenBy(x => x.Id).Take(5).ToListAsync(ct),
            TotalPlays = active.Sum(x => x.PlayCount), TotalPodcasts = await db.Podcasts.CountAsync(ct),
            TotalComments = count, CommentsUnavailable = unavailable
        });
    }

    [HttpPost]
    public Task<IActionResult> Approve(int id, CancellationToken ct) => SetStatus(id, EpisodeStatus.Published, ct);

    [HttpPost]
    public Task<IActionResult> Reject(int id, CancellationToken ct) => SetStatus(id, EpisodeStatus.Rejected, ct);

    [HttpPost]
    public async Task<IActionResult> ChangeRole(Guid userId, AccountRole role)
    {
        if (!Enum.IsDefined(role)) return BadRequest();
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();
        if (user.Role == AccountRole.Admin && role != AccountRole.Admin && !user.IsDisabled &&
            await db.Users.CountAsync(x => x.Role == AccountRole.Admin && !x.IsDisabled) <= 1)
        {
            TempData["ErrorMessage"] = "Keep at least one active admin account.";
            return RedirectToAction(nameof(Index));
        }
        user.Role = role;
        var result = await users.UpdateAsync(user);
        if (result.Succeeded)
        {
            await users.UpdateSecurityStampAsync(user);
            TempData["StatusMessage"] = "Role updated. The user must sign in again.";
        }
        else TempData["ErrorMessage"] = "The account changed in another request. Reload and try again.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleUser(Guid userId)
    {
        if (userId == EpisodeAccess.UserId(User)) return BadRequest("You cannot disable your own admin account.");
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();
        user.IsDisabled = !user.IsDisabled;
        var result = await users.UpdateAsync(user);
        if (result.Succeeded)
        {
            await users.UpdateSecurityStampAsync(user);
            TempData["StatusMessage"] = user.IsDisabled ? "Account disabled. Existing sessions will be rejected." : "Account enabled.";
        }
        else TempData["ErrorMessage"] = "The account could not be updated. Reload and try again.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SetStatus(int id, EpisodeStatus status, CancellationToken ct)
    {
        var episode = await db.Episodes.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (episode is null) return NotFound();
        if (status == EpisodeStatus.Published && episode.MediaKey == null) return BadRequest("Upload media before publishing.");
        episode.Status = status; episode.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = status == EpisodeStatus.Published ? "Episode approved." : "Episode rejected. Its owner can edit and resubmit it.";
        }
        catch (DbUpdateConcurrencyException)
        { TempData["ErrorMessage"] = "This episode changed in another request. Reload before reviewing it."; }
        return RedirectToAction(nameof(Index));
    }
}
