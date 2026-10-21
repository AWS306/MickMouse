using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;

namespace MickMouse.Web.Services;

public sealed class AccountDeletion(AppDbContext db, UserManager<ApplicationUser> users,
    ICommentStore comments, EpisodeCleanup cleanup, TimeProvider clock, ILogger<AccountDeletion> logger)
{
    public async Task DeleteAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user.Role == AccountRole.Admin &&
            !await db.Users.AnyAsync(other => other.Id != user.Id && other.Role == AccountRole.Admin && !other.IsDisabled, ct))
            throw new AccountDeletionBlockedException("Create another active admin before deleting this account.");

        try
        {
            // Stop new authenticated requests before cleaning the external stores.
            user.IsDisabled = true;
            RequireSuccess(await users.UpdateAsync(user));
            RequireSuccess(await users.UpdateSecurityStampAsync(user));

            var episodes = await db.Episodes.Where(episode => episode.Podcast!.CreatorId == user.Id).ToListAsync(ct);
            foreach (var episode in episodes.Where(episode => !episode.IsDeleted))
            {
                episode.IsDeleted = true;
                episode.DeletedAtUtc = clock.GetUtcNow().UtcDateTime;
                episode.Version++;
            }
            await db.SaveChangesAsync(ct);
            foreach (var episode in episodes)
                if (!await cleanup.TryCompleteAsync(episode, ct))
                    throw new InvalidOperationException("Episode cleanup is incomplete.");

            await comments.DeleteForUserAsync(user.Id, ct);
            var podcasts = await db.Podcasts.Where(podcast => podcast.CreatorId == user.Id).ToListAsync(ct);
            db.Episodes.RemoveRange(episodes);
            db.Podcasts.RemoveRange(podcasts);
            // Identity's EF store saves these pending removals and the user in
            // one SaveChanges transaction. FK cascades remove likes/follows.
            RequireSuccess(await users.DeleteAsync(user));
        }
        catch
        {
            // External stores cannot share a SQL transaction. Retain a usable
            // account on failure; completed content removals are not undone.
            db.ChangeTracker.Clear();
            var retained = await users.FindByIdAsync(user.Id.ToString());
            if (retained is not null && retained.IsDisabled)
            {
                retained.IsDisabled = false;
                var restored = await users.UpdateAsync(retained);
                if (!restored.Succeeded)
                    logger.LogError("Could not re-enable account {UserId} after interrupted deletion.", user.Id);
            }
            throw;
        }
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("The account changed. Sign in again and retry.");
    }
}

public sealed class AccountDeletionBlockedException(string message) : Exception(message);
