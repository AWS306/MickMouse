using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using MickMouse.Web.Services;
using Xunit;

namespace MickMouse.Tests;

public sealed class AccountDeletionTests
{
    [Fact]
    public async Task ExternalCleanupFailureRetainsAnEnabledAccountForRetry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICommentStore, FailingComments>();
        services.AddSingleton<IMediaStore, UnusedMedia>();
        services.AddScoped<EpisodeCleanup>();
        services.AddScoped<AccountDeletion>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        { Id = Guid.NewGuid(), UserName = "retry@mick.local", Email = "retry@mick.local", DisplayName = "Retry listener" };
        Assert.True((await users.CreateAsync(user, "RetryPassword!42")).Succeeded);
        var deletion = scope.ServiceProvider.GetRequiredService<AccountDeletion>();
        await Assert.ThrowsAsync<IOException>(() => deletion.DeleteAsync(user));
        var retained = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(retained);
        Assert.False(retained.IsDisabled);
        Assert.True(await users.CheckPasswordAsync(retained, "RetryPassword!42"));
    }

    private sealed class FailingComments : ICommentStore
    {
        public Task<IReadOnlyList<EpisodeComment>> GetForEpisodeAsync(int id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EpisodeComment>>([]);
        public Task AddAsync(EpisodeComment comment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TryEditAsync(int episodeId, string commentId, Guid actorId, string text, int version, DateTime now, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteForEpisodeAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteForUserAsync(Guid id, CancellationToken ct = default) => throw new IOException("Simulated external outage.");
    }

    private sealed class UnusedMedia : IMediaStore
    {
        public Task<string> SaveAsync(Stream source, string name, string type, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetPlaybackUrlAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }
}
