using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using MickMouse.Web.Models;

namespace MickMouse.Web.Infrastructure;

public sealed class DynamoCommentStore(IAmazonDynamoDB client, string tableName) : ICommentStore
{
    public async Task<IReadOnlyList<EpisodeComment>> GetForEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        var comments = new List<EpisodeComment>();
        Dictionary<string, AttributeValue>? continuation = null;
        do
        {
            var response = await client.QueryAsync(new QueryRequest
            {
                TableName = tableName,
                KeyConditionExpression = "EpisodeId = :episode",
                ExpressionAttributeValues = new() { [":episode"] = Number(episodeId) },
                ExclusiveStartKey = continuation,
                ConsistentRead = true
            }, ct);
            foreach (var item in response.Items ?? []) comments.Add(FromItem(item));
            continuation = response.LastEvaluatedKey;
        } while (continuation is { Count: > 0 });
        return comments.OrderByDescending(comment => comment.CreatedAtUtc).ThenBy(comment => comment.CommentId).ToArray();
    }

    public async Task AddAsync(EpisodeComment comment, CancellationToken ct = default)
    {
        CommentRules.Validate(comment);
        var item = new Dictionary<string, AttributeValue>
        {
            ["EpisodeId"] = Number(comment.EpisodeId), ["CommentId"] = Text(comment.CommentId),
            ["PodcastId"] = Number(comment.PodcastId), ["UserId"] = Text(comment.UserId.ToString("D")),
            ["AuthorName"] = Text(comment.AuthorName), ["Text"] = Text(comment.Text),
            ["CreatedAtUtc"] = Text(AsUtc(comment.CreatedAtUtc).ToString("O", CultureInfo.InvariantCulture)),
            ["EditUntilEpoch"] = Number(EpochSeconds(AsUtc(comment.CreatedAtUtc).AddHours(24))),
            ["Version"] = Number(comment.Version)
        };
        if (comment.UpdatedAtUtc is { } updated)
            item["UpdatedAtUtc"] = Text(AsUtc(updated).ToString("O", CultureInfo.InvariantCulture));
        await client.PutItemAsync(new PutItemRequest
        {
            TableName = tableName,
            Item = item,
            ConditionExpression = "attribute_not_exists(EpisodeId) AND attribute_not_exists(CommentId)"
        }, ct);
    }

    public async Task<bool> TryEditAsync(int episodeId, string commentId, Guid actorId, string text,
        int expectedVersion, DateTime nowUtc, CancellationToken ct = default)
    {
        text = CommentRules.ValidateText(text);
        if (actorId == Guid.Empty || expectedVersion < 1 || expectedVersion == int.MaxValue) return false;
        var utcNow = AsUtc(nowUtc);
        try
        {
            await client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = tableName,
                Key = Key(episodeId, commentId),
                UpdateExpression = "SET #text = :text, #updated = :updated, #version = #version + :one",
                ConditionExpression = "#user = :user AND #deadline > :now AND #version = :expected AND #created <= :updated",
                ExpressionAttributeNames = new()
                {
                    ["#text"] = "Text", ["#updated"] = "UpdatedAtUtc", ["#version"] = "Version",
                    ["#user"] = "UserId", ["#deadline"] = "EditUntilEpoch", ["#created"] = "CreatedAtUtc"
                },
                ExpressionAttributeValues = new()
                {
                    [":text"] = Text(text), [":updated"] = Text(utcNow.ToString("O", CultureInfo.InvariantCulture)),
                    [":one"] = Number(1), [":user"] = Text(actorId.ToString("D")),
                    [":now"] = Number(EpochSeconds(utcNow)), [":expected"] = Number(expectedVersion)
                }
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException) { return false; }
    }

    public async Task DeleteForEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        Dictionary<string, AttributeValue>? continuation = null;
        do
        {
            var page = await client.QueryAsync(new QueryRequest
            {
                TableName = tableName,
                KeyConditionExpression = "EpisodeId = :episode",
                ExpressionAttributeValues = new() { [":episode"] = Number(episodeId) },
                ProjectionExpression = "EpisodeId, CommentId",
                ExclusiveStartKey = continuation,
                ConsistentRead = true
            }, ct);
            foreach (var chunk in (page.Items ?? []).Chunk(25))
            {
                var pending = chunk.Select(item => new WriteRequest { DeleteRequest = new DeleteRequest { Key = item } }).ToList();
                for (var attempt = 0; pending.Count > 0 && attempt < 8; attempt++)
                {
                    if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(100 * Math.Pow(2, attempt), 2000)), ct);
                    var result = await client.BatchWriteItemAsync(new BatchWriteItemRequest
                    {
                        RequestItems = new() { [tableName] = pending }
                    }, ct);
                    pending = result.UnprocessedItems is not null && result.UnprocessedItems.TryGetValue(tableName, out var remaining)
                        ? remaining : [];
                }
                if (pending.Count > 0)
                    throw new InvalidOperationException("Comment cleanup could not finish. Retry deleting this episode.");
            }
            continuation = page.LastEvaluatedKey;
        } while (continuation is { Count: > 0 });
    }

    public async Task DeleteForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A user ID is required.", nameof(userId));
        Dictionary<string, AttributeValue>? continuation = null;
        do
        {
            // The lab has a small comments table. A UserId index would be more
            // suitable than a filtered scan for a large production catalogue.
            var page = await client.ScanAsync(new ScanRequest
            {
                TableName = tableName,
                FilterExpression = "#user = :user",
                ExpressionAttributeNames = new() { ["#user"] = "UserId" },
                ExpressionAttributeValues = new() { [":user"] = Text(userId.ToString("D")) },
                ProjectionExpression = "EpisodeId, CommentId",
                ExclusiveStartKey = continuation,
                ConsistentRead = true
            }, ct);
            foreach (var chunk in (page.Items ?? []).Chunk(25))
            {
                var pending = chunk.Select(item => new WriteRequest
                { DeleteRequest = new DeleteRequest { Key = item } }).ToList();
                for (var attempt = 0; pending.Count > 0 && attempt < 8; attempt++)
                {
                    if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(100 * Math.Pow(2, attempt), 2000)), ct);
                    var result = await client.BatchWriteItemAsync(new BatchWriteItemRequest
                    { RequestItems = new() { [tableName] = pending } }, ct);
                    pending = result.UnprocessedItems is not null && result.UnprocessedItems.TryGetValue(tableName, out var remaining)
                        ? remaining : [];
                }
                if (pending.Count > 0) throw new InvalidOperationException("Account comment cleanup needs another attempt.");
            }
            continuation = page.LastEvaluatedKey;
        } while (continuation is { Count: > 0 });
    }

    private static Dictionary<string, AttributeValue> Key(int episodeId, string commentId) =>
        new() { ["EpisodeId"] = Number(episodeId), ["CommentId"] = Text(commentId) };
    private static AttributeValue Number(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
    private static AttributeValue Number(decimal value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
    private static decimal EpochSeconds(DateTime value) =>
        (value.Ticks - DateTime.UnixEpoch.Ticks) / (decimal)TimeSpan.TicksPerSecond;
    private static AttributeValue Text(string value) => new() { S = value };
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static EpisodeComment FromItem(Dictionary<string, AttributeValue> item) => new()
    {
        EpisodeId = int.Parse(item["EpisodeId"].N, CultureInfo.InvariantCulture),
        PodcastId = int.Parse(item["PodcastId"].N, CultureInfo.InvariantCulture),
        CommentId = item["CommentId"].S,
        UserId = Guid.Parse(item["UserId"].S),
        AuthorName = item["AuthorName"].S,
        Text = item["Text"].S,
        CreatedAtUtc = DateTime.Parse(item["CreatedAtUtc"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        UpdatedAtUtc = item.TryGetValue("UpdatedAtUtc", out var updated)
            ? DateTime.Parse(updated.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
        Version = int.Parse(item["Version"].N, CultureInfo.InvariantCulture)
    };
}
