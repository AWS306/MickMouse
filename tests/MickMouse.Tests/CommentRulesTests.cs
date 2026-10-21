using MickMouse.Web.Models;
using Xunit;

namespace MickMouse.Tests;

public sealed class CommentRulesTests
{
    private static readonly Guid Author = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Created = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AuthorCanEditJustBeforeTwentyFourHours()
    {
        Assert.True(Comment().CanEdit(Author, Created.AddDays(1).AddTicks(-1)));
    }

    [Fact]
    public void ExactlyTwentyFourHoursIsClosed()
    {
        Assert.False(Comment().CanEdit(Author, Created.AddDays(1)));
    }

    [Fact]
    public void AfterTwentyFourHoursIsClosed()
    {
        Assert.False(Comment().CanEdit(Author, Created.AddDays(1).AddSeconds(1)));
    }

    [Fact]
    public void AnotherUserCannotEditEvenWithinTheWindow()
    {
        Assert.False(Comment().CanEdit(Guid.NewGuid(), Created.AddMinutes(1)));
    }

    [Fact]
    public void FutureCreatedCommentCannotBeEdited()
    {
        Assert.False(Comment().CanEdit(Author, Created.AddTicks(-1)));
    }

    [Fact]
    public void UpdatingDoesNotRestartTheOriginalWindow()
    {
        var comment = Comment();
        comment.UpdatedAtUtc = Created.AddHours(23);
        Assert.False(comment.CanEdit(Author, Created.AddHours(25)));
    }

    private static EpisodeComment Comment() => new()
    {
        EpisodeId = 1,
        PodcastId = 1,
        CommentId = "boundary-comment",
        UserId = Author,
        AuthorName = "Listener",
        Text = "A useful field note.",
        CreatedAtUtc = Created,
        Version = 1
    };
}
