using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Models;

namespace MickMouse.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Podcast> Podcasts => Set<Podcast>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<EpisodeLike> EpisodeLikes => Set<EpisodeLike>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
        });
        builder.Entity<Podcast>(entity =>
        {
            entity.Property(podcast => podcast.Title).HasMaxLength(120).IsRequired();
            entity.Property(podcast => podcast.Description).HasMaxLength(2000).IsRequired();
            entity.Property(podcast => podcast.HostName).HasMaxLength(100).IsRequired();
            entity.Property(podcast => podcast.Topic).HasMaxLength(80).IsRequired();
            entity.Property(podcast => podcast.CoverKey).HasMaxLength(40).IsRequired();
            entity.HasOne(podcast => podcast.Creator).WithMany().HasForeignKey(podcast => podcast.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(podcast => podcast.HostName);
            entity.HasIndex(podcast => podcast.Topic);
        });
        builder.Entity<Episode>(entity =>
        {
            entity.Property(episode => episode.Title).HasMaxLength(160).IsRequired();
            entity.Property(episode => episode.Summary).HasMaxLength(3000).IsRequired();
            entity.Property(episode => episode.Topic).HasMaxLength(80).IsRequired();
            entity.Property(episode => episode.CoverKey).HasMaxLength(40).IsRequired();
            entity.Property(episode => episode.MediaKey).HasMaxLength(512);
            entity.Property(episode => episode.MediaContentType).HasMaxLength(100);
            entity.Property(episode => episode.SourceUrl).HasMaxLength(1000);
            entity.Property(episode => episode.MediaCredit).HasMaxLength(1000);
            entity.Property(episode => episode.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(episode => episode.Version).IsConcurrencyToken();
            entity.HasOne(episode => episode.Podcast).WithMany(podcast => podcast.Episodes)
                .HasForeignKey(episode => episode.PodcastId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(episode => new { episode.IsDeleted, episode.Status, episode.ReleaseDateUtc });
            entity.HasIndex(episode => episode.PlayCount);
            entity.HasIndex(episode => episode.Topic);
        });
        builder.Entity<Subscription>(entity =>
        {
            entity.HasOne(subscription => subscription.User).WithMany().HasForeignKey(subscription => subscription.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(subscription => subscription.Podcast).WithMany().HasForeignKey(subscription => subscription.PodcastId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(subscription => new { subscription.UserId, subscription.PodcastId }).IsUnique();
        });
        builder.Entity<EpisodeLike>(entity =>
        {
            entity.HasOne(like => like.User).WithMany().HasForeignKey(like => like.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(like => like.Episode).WithMany().HasForeignKey(like => like.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(like => new { like.UserId, like.EpisodeId }).IsUnique();
        });
    }
}
