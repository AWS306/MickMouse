using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using Xunit;

namespace MickMouse.Tests;

public sealed class LocalCommentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mick-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid Author = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Created = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StoreEnforcesOwnerDeadlineAndVersionWithoutRelyingOnTheUi()
    {
        var store = new LocalCommentStore(_directory);
        await store.AddAsync(Comment());

        Assert.False(await store.TryEditAsync(1, "note-1", Guid.NewGuid(), "forged", 1, Created.AddMinutes(1)));
        Assert.False(await store.TryEditAsync(1, "note-1", Author, "expired", 1, Created.AddDays(1)));
        Assert.False(await store.TryEditAsync(1, "note-1", Author, "stale", 0, Created.AddMinutes(1)));

        var saved = Assert.Single(await store.GetForEpisodeAsync(1));
        Assert.Equal("Original note", saved.Text);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task SuccessfulEditPreservesIdentityAndOriginalCreationAcrossReopen()
    {
        var store = new LocalCommentStore(_directory);
        await store.AddAsync(Comment());
        var edited = Created.AddHours(1);
        Assert.True(await store.TryEditAsync(1, "note-1", Author, "Revised note", 1, edited));

        var reopened = new LocalCommentStore(_directory);
        var saved = Assert.Single(await reopened.GetForEpisodeAsync(1));
        Assert.Equal("Revised note", saved.Text);
        Assert.Equal(2, saved.Version);
        Assert.Equal(Author, saved.UserId);
        Assert.Equal(Created, saved.CreatedAtUtc);
        Assert.Equal(edited, saved.UpdatedAtUtc);
        Assert.Equal(1, saved.PodcastId);
        Assert.False(await reopened.TryEditAsync(1, "note-1", Author, "Late edit", 2, Created.AddDays(1)));
    }

    [Fact]
    public async Task TwoEditsUsingTheSameVersionCannotBothWin()
    {
        var store = new LocalCommentStore(_directory);
        await store.AddAsync(Comment());
        var attempts = await Task.WhenAll(
            store.TryEditAsync(1, "note-1", Author, "Tab A", 1, Created.AddMinutes(1)),
            store.TryEditAsync(1, "note-1", Author, "Tab B", 1, Created.AddMinutes(1)));

        Assert.Single(attempts, success => success);
        Assert.Equal(2, Assert.Single(await store.GetForEpisodeAsync(1)).Version);
    }

    [Fact]
    public async Task CallersCannotMutateSavedRecordsByKeepingObjectReferences()
    {
        var store = new LocalCommentStore(_directory);
        var original = Comment();
        await store.AddAsync(original);
        original.Text = "Mutated input";
        original.UserId = Guid.NewGuid();

        var read = Assert.Single(await store.GetForEpisodeAsync(1));
        Assert.Equal("Original note", read.Text);
        Assert.Equal(Author, read.UserId);
        read.Text = "Mutated output";
        Assert.Equal("Original note", Assert.Single(await store.GetForEpisodeAsync(1)).Text);
    }

    [Fact]
    public async Task QueriesAndDeletionAreScopedToTheEpisode()
    {
        var store = new LocalCommentStore(_directory);
        await store.AddAsync(Comment());
        var other = Comment();
        other.EpisodeId = 2;
        other.CommentId = "note-2";
        await store.AddAsync(other);

        await store.DeleteForEpisodeAsync(1);
        Assert.Empty(await store.GetForEpisodeAsync(1));
        Assert.Equal("note-2", Assert.Single(await store.GetForEpisodeAsync(2)).CommentId);
        Assert.False(await store.TryEditAsync(1, "note-2", Author, "Wrong partition", 1, Created.AddMinutes(1)));
    }

    [Fact]
    public async Task UserDeletionRemovesTheirCommentsAcrossEpisodesAndPreservesOthers()
    {
        var store = new LocalCommentStore(_directory);
        await store.AddAsync(Comment());
        var second = Comment(); second.EpisodeId = 2; second.CommentId = "second";
        await store.AddAsync(second);
        var other = Comment(); other.UserId = Guid.NewGuid(); other.CommentId = "other";
        await store.AddAsync(other);
        await store.DeleteForUserAsync(Author);
        await store.DeleteForUserAsync(Author);
        var retained = Assert.Single(await new LocalCommentStore(_directory).GetForEpisodeAsync(1));
        Assert.Equal(other.UserId, retained.UserId);
        Assert.Empty(await store.GetForEpisodeAsync(2));
    }

    [Fact]
    public async Task UserDeletionRejectsAnEmptyIdentity()
    {
        var store = new LocalCommentStore(_directory);
        await store.AddAsync(Comment());
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteForUserAsync(Guid.Empty));
        Assert.Single(await store.GetForEpisodeAsync(1));
    }

    private static EpisodeComment Comment() => new()
    {
        EpisodeId = 1,
        PodcastId = 1,
        CommentId = "note-1",
        UserId = Author,
        AuthorName = "Listener",
        Text = "Original note",
        CreatedAtUtc = Created,
        Version = 1
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
