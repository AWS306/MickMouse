using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;

namespace MickMouse.Web.Services;

public static class SubmissionExports
{
    public static async Task WriteSqlAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            "Server=localhost;Database=MickMouse;Integrated Security=True;TrustServerCertificate=True").Options;
        await using var db = new AppDbContext(options);
        var text = new StringBuilder();
        text.AppendLine("-- Mick Mouse COMP306 Lab 3. Run on an EMPTY SQL Server database.");
        text.AppendLine("-- Demo accounts only: password TrailDemo!42. Rotate these before public use.");
        text.AppendLine("-- Optional Identity claims/logins/tokens tables intentionally remain empty.");
        text.AppendLine("-- Playback counts below are example app data, not the source podcast's audience metrics.");
        text.AppendLine(db.Database.GenerateCreateScript());
        text.AppendLine("SET XACT_ABORT ON;\nBEGIN TRANSACTION;");
        foreach (var user in DemoSeed.BuildUsers())
            Insert("AspNetUsers", new[] { "Id", "DisplayName", "Role", "IsDisabled", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnd", "LockoutEnabled", "AccessFailedCount" },
                new object?[] { user.Id, user.DisplayName, user.Role.ToString(), false, user.UserName, user.NormalizedUserName, user.Email, user.NormalizedEmail, user.EmailConfirmed, user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp, null, false, false, null, true, 0 });
        Identity("Podcasts", () =>
        {
            foreach (var item in DemoSeed.BuildPodcasts()) Insert("Podcasts",
                new[] { "Id", "Title", "Description", "HostName", "Topic", "CoverKey", "CreatorId", "CreatedAtUtc" },
                new object?[] { item.Id, item.Title, item.Description, item.HostName, item.Topic, item.CoverKey, item.CreatorId, item.CreatedAtUtc });
        });
        Identity("Episodes", () =>
        {
            foreach (var item in DemoSeed.BuildEpisodes()) Insert("Episodes",
                new[] { "Id", "PodcastId", "Title", "Summary", "Topic", "CoverKey", "ReleaseDateUtc", "DurationMinutes", "PlayCount", "MediaKey", "MediaContentType", "SourceUrl", "MediaCredit", "Status", "IsDeleted", "DeletedAtUtc", "CleanupCompleted", "Version" },
                new object?[] { item.Id, item.PodcastId, item.Title, item.Summary, item.Topic, item.CoverKey, item.ReleaseDateUtc, item.DurationMinutes, item.PlayCount, item.MediaKey, item.MediaContentType, item.SourceUrl, item.MediaCredit, item.Status.ToString(), false, null, false, 1 });
        });
        Identity("Subscriptions", () =>
        {
            foreach (var item in DemoSeed.BuildSubscriptions()) Insert("Subscriptions",
                new[] { "Id", "UserId", "PodcastId", "CreatedAtUtc" }, new object?[] { item.Id, item.UserId, item.PodcastId, item.CreatedAtUtc });
        });
        Identity("EpisodeLikes", () =>
        {
            foreach (var item in DemoSeed.BuildLikes()) Insert("EpisodeLikes",
                new[] { "Id", "UserId", "EpisodeId", "CreatedAtUtc" }, new object?[] { item.Id, item.UserId, item.EpisodeId, item.CreatedAtUtc });
        });
        text.AppendLine("COMMIT TRANSACTION;");
        text.AppendLine("SELECT 'Users' AS [TableName], COUNT(*) AS [Rows] FROM [AspNetUsers] UNION ALL SELECT 'Podcasts',COUNT(*) FROM [Podcasts] UNION ALL SELECT 'Episodes',COUNT(*) FROM [Episodes] UNION ALL SELECT 'Subscriptions',COUNT(*) FROM [Subscriptions] UNION ALL SELECT 'EpisodeLikes',COUNT(*) FROM [EpisodeLikes];");
        await Write(path, text.ToString());
        Console.WriteLine($"Wrote SQL schema and sample data: {Path.GetFullPath(path)}");

        void Insert(string table, string[] columns, object?[] values) => text.AppendLine(
            $"INSERT INTO [{table}] ({string.Join(",", columns.Select(column => $"[{column}]"))}) VALUES ({string.Join(",", values.Select(SqlValue))});");
        void Identity(string table, Action inserts)
        { text.AppendLine($"SET IDENTITY_INSERT [{table}] ON;"); inserts(); text.AppendLine($"SET IDENTITY_INSERT [{table}] OFF;"); }
    }

    public static async Task WriteDynamoAsync(string path)
    {
        var now = DateTime.UtcNow;
        var texts = new[]
        {
            "The practical examples made this a useful listen.", "I would like to try this with my own small server.",
            "This older comment demonstrates the editing deadline.", "A clear explanation of a complicated topic.",
            "The conversation gave me a new idea for a class project."
        };
        var items = Enumerable.Range(0, 5).Select(index =>
        {
            var created = now.AddHours(index == 2 ? -30 : -1 - index);
            var author = index % 2 == 0 ? "10000000-0000-0000-0000-000000000004" : "10000000-0000-0000-0000-000000000005";
            return new Dictionary<string, object>
            {
                ["EpisodeId"] = new { N = "1" }, ["CommentId"] = new { S = $"console-demo-{index + 1:D2}" },
                ["PodcastId"] = new { N = "1" }, ["UserId"] = new { S = author },
                ["AuthorName"] = new { S = index % 2 == 0 ? "Alex" : "Rain" }, ["Text"] = new { S = texts[index] },
                ["CreatedAtUtc"] = new { S = created.ToString("O", CultureInfo.InvariantCulture) },
                ["EditUntilEpoch"] = new { N = ((decimal)(created.AddHours(24).Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture) },
                ["Version"] = new { N = "1" }
            };
        }).ToArray();
        await Write(path, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote five DynamoDB item examples with fresh timestamps: {Path.GetFullPath(path)}");
    }

    private static string SqlValue(object? value) => value switch
    {
        null => "NULL", bool boolean => boolean ? "1" : "0",
        DateTime date => "'" + date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "'",
        Guid guid => "'" + guid + "'",
        int or long or decimal => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => "N'" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("'", "''") + "'"
    };

    private static async Task Write(string path, string text)
    {
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await File.WriteAllTextAsync(absolute, text, new UTF8Encoding(false));
    }
}
