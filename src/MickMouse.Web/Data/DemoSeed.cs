using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;

namespace MickMouse.Web.Data;

public static class DemoSeed
{
    public const string DemoPassword = "TrailDemo!42";
    public static readonly Guid AdminId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CemId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid MayaId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid AlexId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid RainId = Guid.Parse("10000000-0000-0000-0000-000000000005");

    public static IReadOnlyList<ApplicationUser> BuildUsers()
    {
        ApplicationUser[] users =
        [
            NewUser(AdminId, "admin@mick.local", "Studio Admin", AccountRole.Admin),
            NewUser(CemId, "cem@mick.local", "Cem", AccountRole.Podcaster),
            NewUser(MayaId, "maya@mick.local", "Maya", AccountRole.Podcaster),
            NewUser(AlexId, "alex@mick.local", "Alex", AccountRole.Listener),
            NewUser(RainId, "rain@mick.local", "Rain", AccountRole.Listener)
        ];
        var hasher = new PasswordHasher<ApplicationUser>();
        foreach (var user in users) user.PasswordHash = hasher.HashPassword(user, DemoPassword);
        return users;
    }

    public static IReadOnlyList<Podcast> BuildPodcasts() => ReadCatalog()
        .GroupBy(episode => episode.PodcastId).OrderBy(group => group.Key)
        .Select(group =>
        {
            var first = group.First();
            return new Podcast
            {
                Id = group.Key, Title = first.PodcastTitle, Description = first.PodcastDescription,
                HostName = first.HostName, Topic = first.Topic, CoverKey = first.CoverKey,
                CreatorId = group.Key is 3 or 5 ? MayaId : CemId,
                CreatedAtUtc = group.Min(episode => episode.ReleaseDateUtc).AddDays(-1)
            };
        }).ToArray();

    public static IReadOnlyList<Episode> BuildEpisodes()
    {
        long[] samplePlays = [1248, 835, 978, 642, 1536, 1104, 724, 512, 905, 468];
        return ReadCatalog().OrderBy(episode => episode.Id).Select(source => new Episode
        {
            Id = source.Id, PodcastId = source.PodcastId, Title = source.Title, Summary = source.Summary,
            Topic = source.Topic, CoverKey = source.CoverKey, ReleaseDateUtc = source.ReleaseDateUtc,
            DurationMinutes = source.DurationMinutes, PlayCount = samplePlays[source.Id - 1],
            MediaKey = source.MediaKey, MediaContentType = source.ContentType,
            SourceUrl = source.SourceUrl, MediaCredit = source.MediaCredit,
            Status = EpisodeStatus.Published, Version = 1
        }).ToArray();
    }

    public static IReadOnlyList<Subscription> BuildSubscriptions() =>
    [
        NewSubscription(1, AlexId, 1), NewSubscription(2, AlexId, 2),
        NewSubscription(3, RainId, 3), NewSubscription(4, RainId, 5),
        NewSubscription(5, CemId, 3), NewSubscription(6, MayaId, 1)
    ];

    public static IReadOnlyList<EpisodeLike> BuildLikes() =>
    [
        NewLike(1, AlexId, 1), NewLike(2, AlexId, 3), NewLike(3, RainId, 5),
        NewLike(4, RainId, 9), NewLike(5, CemId, 7), NewLike(6, MayaId, 1)
    ];

    public static async Task InitializeAsync(AppDbContext db, UserManager<ApplicationUser> userManager,
        ICommentStore comments, TimeProvider timeProvider, bool seedComments, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken) || await db.Podcasts.AnyAsync(cancellationToken)) return;

        // This initializer is for the Local preview. AWS data comes from the explicit SQL script.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var user in BuildUsers())
        {
            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Could not create a sample account: " +
                    string.Join("; ", result.Errors.Select(error => error.Description)));
        }
        db.Podcasts.AddRange(BuildPodcasts());
        db.Episodes.AddRange(BuildEpisodes());
        db.Subscriptions.AddRange(BuildSubscriptions());
        db.EpisodeLikes.AddRange(BuildLikes());
        await db.SaveChangesAsync(cancellationToken);

        if (seedComments)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            EpisodeComment[] samples =
            [
                NewComment("sample-comment-01", 1, 1, AlexId, "Alex", now.AddHours(-2),
                    "A useful starting point for my next Linux lab. I saved this episode for later."),
                NewComment("sample-comment-02", 1, 1, RainId, "Rain", now.AddHours(-30),
                    "I came back to this after trying the ideas on my own computer."),
                NewComment("sample-comment-03", 3, 2, CemId, "Cem", now.AddHours(-1),
                    "I like hearing how other developers think through everyday problems."),
                NewComment("sample-comment-04", 5, 3, RainId, "Rain", now.AddMinutes(-35),
                    "This is going on my weekend listening list."),
                NewComment("sample-comment-05", 9, 5, MayaId, "Maya", now.AddHours(-26),
                    "An interesting conversation. I followed the source link to learn more.")
            ];
            foreach (var comment in samples)
            {
                var existing = await comments.GetForEpisodeAsync(comment.EpisodeId, cancellationToken);
                if (existing.All(item => item.CommentId != comment.CommentId)) await comments.AddAsync(comment, cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static ApplicationUser NewUser(Guid id, string email, string displayName, AccountRole role) => new()
    {
        Id = id, UserName = email, Email = email, NormalizedUserName = email.ToUpperInvariant(),
        NormalizedEmail = email.ToUpperInvariant(), DisplayName = displayName, Role = role,
        EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("D"),
        ConcurrencyStamp = Guid.NewGuid().ToString("D"), LockoutEnabled = true
    };

    private static Subscription NewSubscription(int id, Guid user, int podcast) => new()
    {
        Id = id, UserId = user, PodcastId = podcast, CreatedAtUtc = SampleInteractionDate
    };

    private static EpisodeLike NewLike(int id, Guid user, int episode) => new()
    {
        Id = id, UserId = user, EpisodeId = episode, CreatedAtUtc = SampleInteractionDate
    };

    private static readonly DateTime SampleInteractionDate = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static EpisodeComment NewComment(string id, int episode, int podcast, Guid user, string author,
        DateTime createdAt, string text) => new()
    {
        CommentId = id, EpisodeId = episode, PodcastId = podcast, UserId = user, AuthorName = author,
        CreatedAtUtc = createdAt, Text = text, Version = 1
    };

    private static IReadOnlyList<CatalogEpisode> ReadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "media-catalog.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The credited podcast catalog is missing. Include media-catalog.json in the build output.", path);
        var episodes = JsonSerializer.Deserialize<List<CatalogEpisode>>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The credited podcast catalog is invalid.");
        if (episodes.Count != 10 || !episodes.Select(episode => episode.Id).Order().SequenceEqual(Enumerable.Range(1, 10)) ||
            !episodes.Select(episode => episode.PodcastId).Distinct().Order().SequenceEqual(Enumerable.Range(1, 5)) ||
            episodes.Any(episode => string.IsNullOrWhiteSpace(episode.Title) || string.IsNullOrWhiteSpace(episode.PodcastTitle) ||
                string.IsNullOrWhiteSpace(episode.MediaKey) || string.IsNullOrWhiteSpace(episode.SourceUrl) ||
                string.IsNullOrWhiteSpace(episode.MediaCredit) || episode.DurationMinutes < 1 || episode.ReleaseDateUtc == default))
            throw new InvalidDataException("The catalog must contain ten credited episodes from five podcasts with IDs 1 through 10.");
        foreach (var episode in episodes)
            episode.ReleaseDateUtc = DateTime.SpecifyKind(episode.ReleaseDateUtc, DateTimeKind.Utc);
        return episodes;
    }

    private sealed class CatalogEpisode
    {
        public int Id { get; set; }
        public int PodcastId { get; set; }
        public string PodcastTitle { get; set; } = "";
        public string PodcastDescription { get; set; } = "";
        public string HostName { get; set; } = "";
        public string Topic { get; set; } = "";
        public string CoverKey { get; set; } = "trail";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public DateTime ReleaseDateUtc { get; set; }
        public int DurationMinutes { get; set; }
        public string SourceUrl { get; set; } = "";
        public string MediaKey { get; set; } = "";
        public string ContentType { get; set; } = "audio/mpeg";
        public string MediaCredit { get; set; } = "";
    }
}
