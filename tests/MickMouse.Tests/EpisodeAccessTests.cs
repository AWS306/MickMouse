using System.Security.Claims;
using MickMouse.Web.Models;
using MickMouse.Web.Services;
using Xunit;

namespace MickMouse.Tests;

public sealed class EpisodeAccessTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PodcasterMustOwnTheShowToManageItsEpisodes()
    {
        Assert.True(EpisodeAccess.CanManage(User(Owner, AccountRole.Podcaster), Owner));
        Assert.False(EpisodeAccess.CanManage(User(Guid.NewGuid(), AccountRole.Podcaster), Owner));
        Assert.False(EpisodeAccess.CanManage(User(Owner, AccountRole.Listener), Owner));
        Assert.False(EpisodeAccess.CanManage(new ClaimsPrincipal(new ClaimsIdentity()), Owner));
    }

    [Fact]
    public void AdminCanManageAcrossOwners()
    {
        Assert.True(EpisodeAccess.CanManage(User(Guid.NewGuid(), AccountRole.Admin), Owner));
    }

    [Theory]
    [InlineData(EpisodeStatus.Pending)]
    [InlineData(EpisodeStatus.Rejected)]
    public void UnapprovedEpisodesAreVisibleOnlyToTheirOwnerOrAdmin(EpisodeStatus status)
    {
        var episode = Episode(status);
        Assert.False(EpisodeAccess.CanView(User(Guid.NewGuid(), AccountRole.Listener), episode, Now));
        Assert.False(EpisodeAccess.CanView(User(Guid.NewGuid(), AccountRole.Podcaster), episode, Now));
        Assert.True(EpisodeAccess.CanView(User(Owner, AccountRole.Podcaster), episode, Now));
        Assert.True(EpisodeAccess.CanView(User(Guid.NewGuid(), AccountRole.Admin), episode, Now));
    }

    [Fact]
    public void ScheduledEpisodesStayPrivateUntilTheirReleaseTime()
    {
        var episode = Episode(EpisodeStatus.Published);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        episode.ReleaseDateUtc = Now.AddSeconds(1);
        Assert.False(EpisodeAccess.CanView(anonymous, episode, Now));
        Assert.True(EpisodeAccess.CanView(User(Owner, AccountRole.Podcaster), episode, Now));
        episode.ReleaseDateUtc = Now;
        Assert.True(EpisodeAccess.CanView(anonymous, episode, Now));
    }

    [Fact]
    public void DeletedEpisodeCannotBeViewedEvenByAdmin()
    {
        var episode = Episode(EpisodeStatus.Published);
        episode.IsDeleted = true;
        Assert.False(EpisodeAccess.CanView(User(Guid.NewGuid(), AccountRole.Admin), episode, Now));
    }

    private static Episode Episode(EpisodeStatus status) => new()
    {
        Id = 1,
        PodcastId = 1,
        Podcast = new Podcast { Id = 1, CreatorId = Owner },
        Status = status,
        ReleaseDateUtc = Now.AddDays(-1)
    };

    private static ClaimsPrincipal User(Guid id, AccountRole role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role.ToString())], "Test"));
}
