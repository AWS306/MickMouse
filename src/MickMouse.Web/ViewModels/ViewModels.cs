using System.ComponentModel.DataAnnotations;
using MickMouse.Web.Models;

namespace MickMouse.Web.ViewModels;

public sealed class BrowseViewModel
{
    public IReadOnlyList<Episode> Episodes { get; init; } = [];
    public Episode? Featured { get; init; }
    public IReadOnlyList<string> Topics { get; init; } = [];
    public string? Topic { get; init; }
    public string? Host { get; init; }
    public string Sort { get; init; } = "latest";
    public int Page { get; init; } = 1;
    public bool HasNext { get; init; }
    public int TotalCount { get; init; }
    public int PodcastCount { get; init; }
    public long TotalPlays { get; init; }
}

public sealed class EpisodeDetailViewModel
{
    public required Episode Episode { get; init; }
    public IReadOnlyList<EpisodeComment> Comments { get; init; } = [];
    public Guid? CurrentUserId { get; init; }
    public bool IsLiked { get; init; }
    public bool IsSubscribed { get; init; }
    public bool CanManage { get; init; }
    public string? PlaybackUrl { get; init; }
    public bool CommentsUnavailable { get; init; }
    public bool MediaUnavailable { get; init; }
    public DateTime NowUtc { get; init; }
    public string? CommentText { get; init; }
}

public sealed class LoginViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required, StringLength(70, MinimumLength = 2), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class DeleteAccountViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string Password { get; set; } = "";
    [Required, Display(Name = "Type delete my account to confirm")]
    public string Confirmation { get; set; } = "";
    public int PodcastCount { get; set; }
    public int EpisodeCount { get; set; }
    public bool IsLastAdmin { get; set; }
}

public sealed class ProfileViewModel
{
    public required ApplicationUser User { get; init; }
    public IReadOnlyList<Podcast> Subscriptions { get; init; } = [];
    public IReadOnlyList<Episode> LikedEpisodes { get; init; } = [];
}

public sealed class StudioViewModel
{
    public IReadOnlyList<Podcast> Podcasts { get; init; } = [];
    public IReadOnlyList<Episode> Episodes { get; init; } = [];
    public long TotalPlays { get; init; }
    public int TotalComments { get; init; }
    public int TotalSubscribers { get; init; }
    public bool CommentsUnavailable { get; init; }
    public int PendingCleanup { get; init; }
}

public sealed class PodcastFormViewModel
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [Required, StringLength(1500)] public string Description { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Host name")] public string HostName { get; set; } = "";
    [Required, StringLength(60)] public string Topic { get; set; } = "";
    [Required] public string CoverKey { get; set; } = "trail";
}

public sealed class EpisodeFormViewModel
{
    public int Id { get; set; }
    public int Version { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Podcast")] public int PodcastId { get; set; }
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [Required, StringLength(3000)] public string Summary { get; set; } = "";
    [Required, StringLength(60)] public string Topic { get; set; } = "";
    [DataType(DataType.Date), Display(Name = "Release date (UTC)")]
    public DateTime ReleaseDateUtc { get; set; } = DateTime.UtcNow.Date;
    [Range(1, 720), Display(Name = "Duration (minutes)")] public int DurationMinutes { get; set; } = 1;
    [Required] public string CoverKey { get; set; } = "trail";
    [Display(Name = "Audio or video file")] public IFormFile? Media { get; set; }
    [Url, StringLength(1000), Display(Name = "Source URL (optional)")] public string? SourceUrl { get; set; }
    [StringLength(1000), Display(Name = "Media credit (optional)")] public string? MediaCredit { get; set; }
    public string? ExistingMediaKey { get; set; }
    public IReadOnlyList<Podcast> Podcasts { get; set; } = [];
}

public sealed class DeleteEpisodeViewModel
{
    public required Episode Episode { get; init; }
}

public sealed class DeletePodcastViewModel
{
    public required Podcast Podcast { get; init; }
}

public sealed class EditCommentViewModel
{
    public int EpisodeId { get; set; }
    public string CommentId { get; set; } = "";
    public int Version { get; set; }
    [Required, StringLength(2000, MinimumLength = 1)] public string Text { get; set; } = "";
    public string EpisodeTitle { get; set; } = "";
    public DateTime EditUntilUtc { get; set; }
}

public sealed class AdminViewModel
{
    public IReadOnlyList<ApplicationUser> Users { get; init; } = [];
    public IReadOnlyList<Episode> PendingEpisodes { get; init; } = [];
    public IReadOnlyList<Episode> TopEpisodes { get; init; } = [];
    public long TotalPlays { get; init; }
    public int TotalPodcasts { get; init; }
    public int TotalComments { get; init; }
    public bool CommentsUnavailable { get; init; }
}

public sealed class ErrorViewModel
{
    public int StatusCode { get; init; } = 500;
    public string Title { get; init; } = "Something interrupted the recording.";
    public string Message { get; init; } = "Please try again shortly.";
    public string? RequestId { get; init; }
}
