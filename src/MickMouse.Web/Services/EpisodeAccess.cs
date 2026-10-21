using System.Security.Claims;
using MickMouse.Web.Models;

namespace MickMouse.Web.Services;

public static class EpisodeAccess
{
    public static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool CanManage(ClaimsPrincipal user, Guid creatorId) =>
        user.Identity?.IsAuthenticated == true &&
        (user.IsInRole(nameof(AccountRole.Admin)) ||
         (user.IsInRole(nameof(AccountRole.Podcaster)) && UserId(user) == creatorId));

    public static bool CanView(ClaimsPrincipal user, Episode episode, DateTime nowUtc) =>
        !episode.IsDeleted &&
        ((episode.Status == EpisodeStatus.Published && episode.ReleaseDateUtc <= nowUtc) ||
         (episode.Podcast != null && CanManage(user, episode.Podcast.CreatorId)));

    public static string SafeCover(string? key) =>
        key is "trail" or "systems" or "rain" or "workshop" or "map" ? key : "trail";
}
