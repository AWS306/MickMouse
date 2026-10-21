using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using MickMouse.Web.Infrastructure;
using Xunit;

namespace MickMouse.Tests;

public sealed class DynamoUserDeletionTests
{
    [Fact]
    public async Task AccountCleanupFiltersByUserAndContinuesPastEmptyPagesAndRetries()
    {
        using var client = new CaptureClient();
        var userId = Guid.NewGuid();
        await new DynamoCommentStore(client, "Comments").DeleteForUserAsync(userId);
        Assert.Equal(3, client.Scans.Count);
        Assert.All(client.Scans, request =>
        {
            Assert.Equal("#user = :user", request.FilterExpression);
            Assert.Equal("UserId", request.ExpressionAttributeNames["#user"]);
            Assert.Equal(userId.ToString("D"), request.ExpressionAttributeValues[":user"].S);
            Assert.True(request.ConsistentRead);
        });
        Assert.Null(client.Scans[0].ExclusiveStartKey);
        Assert.NotEmpty(client.Scans[1].ExclusiveStartKey);
        Assert.Equal(3, client.Batches.Count); // First write is retried once.
        Assert.All(client.Batches, request =>
            Assert.All(request.RequestItems["Comments"], write =>
            {
                Assert.NotNull(write.DeleteRequest);
                Assert.Null(write.PutRequest);
                Assert.Equal(2, write.DeleteRequest.Key.Count);
            }));
    }

    [Fact]
    public async Task EmptyIdentityDoesNotScanTheTable()
    {
        using var client = new CaptureClient();
        await Assert.ThrowsAsync<ArgumentException>(() => new DynamoCommentStore(client, "Comments").DeleteForUserAsync(Guid.Empty));
        Assert.Empty(client.Scans);
    }

    private sealed class CaptureClient() : AmazonDynamoDBClient(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public List<ScanRequest> Scans { get; } = [];
        public List<BatchWriteItemRequest> Batches { get; } = [];

        public override Task<ScanResponse> ScanAsync(ScanRequest request, CancellationToken cancellationToken = default)
        {
            Scans.Add(request);
            Dictionary<string, AttributeValue> Key(int id) => new()
            { ["EpisodeId"] = new() { N = id.ToString() }, ["CommentId"] = new() { S = "note-" + id } };
            return Task.FromResult(new ScanResponse
            {
                Items = Scans.Count == 1 ? [] : [Key(Scans.Count)],
                LastEvaluatedKey = Scans.Count < 3 ? Key(Scans.Count) : []
            });
        }

        public override Task<BatchWriteItemResponse> BatchWriteItemAsync(BatchWriteItemRequest request, CancellationToken cancellationToken = default)
        {
            Batches.Add(request);
            return Task.FromResult(new BatchWriteItemResponse
            { UnprocessedItems = Batches.Count == 1 ? request.RequestItems : [] });
        }
    }
}
