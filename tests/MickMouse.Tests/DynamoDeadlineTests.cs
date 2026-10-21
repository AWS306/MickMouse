using System.Globalization;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using Xunit;

namespace MickMouse.Tests;

public sealed class DynamoDeadlineTests
{
    [Fact]
    public async Task StoredDeadlineRetainsTheFractionalCreationTime()
    {
        using var client = new CaptureClient();
        var store = new DynamoCommentStore(client, "TestComments");
        var comment = Comment();
        await store.AddAsync(comment);
        var stored = decimal.Parse(client.Put!.Item["EditUntilEpoch"].N, CultureInfo.InvariantCulture);
        Assert.Equal(EpochSeconds(comment.CreatedAtUtc.AddHours(24)), stored);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    public async Task ConditionalUpdateRetainsSubsecondNowAtTheExactBoundary(long offsetTicks)
    {
        using var client = new CaptureClient();
        var store = new DynamoCommentStore(client, "TestComments");
        var comment = Comment();
        var nowUtc = comment.CreatedAtUtc.AddHours(24).AddTicks(offsetTicks);
        await store.AddAsync(comment);
        await store.TryEditAsync(1, comment.CommentId, comment.UserId, "Updated", 1, nowUtc);
        var deadline = decimal.Parse(client.Put!.Item["EditUntilEpoch"].N, CultureInfo.InvariantCulture);
        var now = decimal.Parse(client.Update!.ExpressionAttributeValues[":now"].N, CultureInfo.InvariantCulture);
        Assert.Equal(EpochSeconds(nowUtc), now);
        Assert.Contains("#deadline > :now", client.Update.ConditionExpression);
        Assert.Equal(offsetTicks < 0, deadline > now);
    }

    private static decimal EpochSeconds(DateTime value) =>
        (value.Ticks - DateTime.UnixEpoch.Ticks) / (decimal)TimeSpan.TicksPerSecond;

    private static EpisodeComment Comment() => new()
    {
        EpisodeId = 1, PodcastId = 1, CommentId = "fractional-boundary",
        UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"), AuthorName = "Listener",
        Text = "Original", CreatedAtUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567), Version = 1
    };

    // Captures SDK requests without network calls. This verifies numeric precision,
    // not AWS permissions or the behavior of the live DynamoDB service.
    private sealed class CaptureClient() : AmazonDynamoDBClient(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public PutItemRequest? Put { get; private set; }
        public UpdateItemRequest? Update { get; private set; }

        public override Task<PutItemResponse> PutItemAsync(PutItemRequest request, CancellationToken cancellationToken = default)
        {
            Put = request;
            return Task.FromResult(new PutItemResponse());
        }

        public override Task<UpdateItemResponse> UpdateItemAsync(UpdateItemRequest request, CancellationToken cancellationToken = default)
        {
            Update = request;
            return Task.FromResult(new UpdateItemResponse());
        }
    }
}
