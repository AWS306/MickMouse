using Microsoft.AspNetCore.Identity;

namespace MickMouse.Web.Models;

public enum AccountRole { Listener, Podcaster, Admin }
public enum EpisodeStatus { Pending, Published, Rejected }

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public AccountRole Role { get; set; } = AccountRole.Listener;
    public bool IsDisabled { get; set; }
}

public sealed class Podcast
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string HostName { get; set; } = "";
    public string Topic { get; set; } = "";
    public string CoverKey { get; set; } = "trail";
    public Guid CreatorId { get; set; }
    public ApplicationUser? Creator { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}

public sealed class Episode
{
    public int Id { get; set; }
    public int PodcastId { get; set; }
    public Podcast? Podcast { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Topic { get; set; } = "";
    public string CoverKey { get; set; } = "trail";
    public DateTime ReleaseDateUtc { get; set; }
    public int DurationMinutes { get; set; }
    public long PlayCount { get; set; }
    public string? MediaKey { get; set; }
    public string? MediaContentType { get; set; }
    public string? SourceUrl { get; set; }
    public string? MediaCredit { get; set; }
    public EpisodeStatus Status { get; set; } = EpisodeStatus.Pending;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public bool CleanupCompleted { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class Subscription
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public int PodcastId { get; set; }
    public Podcast? Podcast { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class EpisodeLike
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public int EpisodeId { get; set; }
    public Episode? Episode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class EpisodeComment
{
    public int EpisodeId { get; set; }
    public int PodcastId { get; set; }
    public string CommentId { get; set; } = "";
    public Guid UserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int Version { get; set; } = 1;

    public bool CanEdit(Guid actorId, DateTime nowUtc) =>
        actorId != Guid.Empty && actorId == UserId &&
        nowUtc >= CreatedAtUtc && nowUtc - CreatedAtUtc < TimeSpan.FromHours(24);
}
