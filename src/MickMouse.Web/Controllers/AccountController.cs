using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Models;
using MickMouse.Web.Services;
using MickMouse.Web.ViewModels;

namespace MickMouse.Web.Controllers;

public sealed class AccountController(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, AppDbContext db, AccountDeletion deletion,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is null || user.IsDisabled)
        {
            ModelState.AddModelError("", "The email or password is incorrect, or the account is unavailable.");
            return View(model);
        }
        var result = await signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.IsLockedOut ? "Too many attempts. Please try again in 5 minutes." :
                "The email or password is incorrect, or the account is unavailable.");
            return View(model);
        }
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Profile");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var email = model.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email,
            DisplayName = model.DisplayName.Trim(), Role = AccountRole.Listener
        };
        if (user.DisplayName.Length < 2)
        {
            ModelState.AddModelError(nameof(model.DisplayName), "Enter at least two characters.");
            return View(model);
        }
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View(model);
        }
        await signIn.SignInAsync(user, isPersistent: false);
        TempData["StatusMessage"] = "Your account is ready. Find something worth listening to.";
        return RedirectToAction("Profile");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Episodes");
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(x => x.UserId == user.Id)
            .Select(x => x.Podcast!).OrderBy(x => x.Title).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var likes = await db.Episodes.AsNoTracking().Include(x => x.Podcast)
            .Where(episode => !episode.IsDeleted && episode.Status == EpisodeStatus.Published && episode.ReleaseDateUtc <= now &&
                db.EpisodeLikes.Any(like => like.UserId == user.Id && like.EpisodeId == episode.Id))
            .OrderByDescending(x => x.ReleaseDateUtc).ToListAsync(ct);
        return View(new ProfileViewModel { User = user, Subscriptions = subscriptions, LikedEpisodes = likes });
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDisabled) return Challenge();
        var model = new DeleteAccountViewModel();
        await PopulateDeletion(model, user, ct);
        return View(model);
    }

    [HttpPost, Authorize, EnableRateLimiting("auth")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountViewModel model, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDisabled) return Challenge();
        await PopulateDeletion(model, user, ct);
        if (model.IsLastAdmin)
        {
            Response.StatusCode = 403;
            ModelState.AddModelError("", "Create another active admin before deleting this account.");
            return View(model);
        }
        if (!string.Equals(model.Confirmation, "delete my account", StringComparison.Ordinal))
            ModelState.AddModelError(nameof(model.Confirmation), "Type delete my account exactly as shown.");
        if (!ModelState.IsValid) return View(model);
        var verified = await signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError(nameof(model.Password), "The password could not be verified. Too many attempts temporarily lock the account.");
            return View(model);
        }
        try { await deletion.DeleteAsync(user, ct); }
        catch (AccountDeletionBlockedException error)
        {
            ModelState.AddModelError("", error.Message);
            await PopulateDeletion(model, user, ct);
            return View(model);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Account deletion was interrupted for {UserId}", user.Id);
            await signIn.SignOutAsync();
            TempData["ErrorMessage"] = "Account deletion did not finish. Some content may already be removed. Sign in again and retry; contact an admin if you cannot sign in.";
            return RedirectToAction(nameof(Login));
        }
        await signIn.SignOutAsync();
        TempData["StatusMessage"] = "Your account and its associated content have been deleted.";
        return RedirectToAction("Index", "Episodes");
    }

    private async Task PopulateDeletion(DeleteAccountViewModel model, ApplicationUser user, CancellationToken ct)
    {
        model.PodcastCount = await db.Podcasts.CountAsync(podcast => podcast.CreatorId == user.Id, ct);
        model.EpisodeCount = await db.Episodes.CountAsync(episode => episode.Podcast!.CreatorId == user.Id && !episode.IsDeleted, ct);
        model.IsLastAdmin = user.Role == AccountRole.Admin &&
            !await db.Users.AnyAsync(other => other.Id != user.Id && other.Role == AccountRole.Admin && !other.IsDisabled, ct);
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View("~/Views/Shared/Error.cshtml", new ErrorViewModel
        { StatusCode = 403, Title = "This page needs a different role.", Message = "Your account does not have access to this action." });
    }
}
