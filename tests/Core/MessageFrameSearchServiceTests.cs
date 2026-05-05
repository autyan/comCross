using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using System.Text;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class MessageFrameSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_MatchesRawUtf8AttributesAndDirection()
    {
        var query = new FakeQueryService([
            new MessageFrameRecord(1, "session-a", DateTime.UtcNow, FrameDirection.Rx, "temperature=25"u8.ToArray(), MessageFormat.Text, "test"),
            new MessageFrameRecord(2, "session-a", DateTime.UtcNow, FrameDirection.Tx, "ignored"u8.ToArray(), MessageFormat.Text, "test", new Dictionary<string, string>
            {
                ["target"] = "motor-a"
            }),
            new MessageFrameRecord(3, "session-a", DateTime.UtcNow, FrameDirection.Rx, "ignored"u8.ToArray(), MessageFormat.Text, "test", new Dictionary<string, string>
            {
                ["note"] = "motor-a"
            })
        ]);
        var search = new MessageFrameSearchService(query);

        var raw = await search.SearchAsync(new MessageFrameSearchQuery("session-a", MessageFrameDataSource.LiveSpool, "temperature"));
        var attrTx = await search.SearchAsync(new MessageFrameSearchQuery("session-a", MessageFrameDataSource.LiveSpool, "motor-a", FrameDirection.Tx));

        Assert.Equal([1L], raw.Matches.Select(match => match.FrameId));
        var match = Assert.Single(attrTx.Matches);
        Assert.Equal(2, match.FrameId);
        Assert.Equal(FrameDirection.Tx, match.Direction);
    }

    [Fact]
    public async Task SearchAsync_ParsesStructuredDirectionAndAttributeFilters()
    {
        var query = new FakeQueryService([
            new MessageFrameRecord(1, "session-a", DateTime.UtcNow, FrameDirection.Rx, "boot ready"u8.ToArray(), MessageFormat.Text, "test", new Dictionary<string, string>
            {
                ["target"] = "motor-a"
            }),
            new MessageFrameRecord(2, "session-a", DateTime.UtcNow, FrameDirection.Tx, "boot ready"u8.ToArray(), MessageFormat.Text, "test", new Dictionary<string, string>
            {
                ["target"] = "motor-a"
            }),
            new MessageFrameRecord(3, "session-a", DateTime.UtcNow, FrameDirection.Tx, "boot ready"u8.ToArray(), MessageFormat.Text, "test", new Dictionary<string, string>
            {
                ["target"] = "motor-b"
            })
        ]);
        var search = new MessageFrameSearchService(query);

        var result = await search.SearchAsync(new MessageFrameSearchQuery(
            "session-a",
            MessageFrameDataSource.LiveSpool,
            "dir:tx attr:target=motor-a boot"));

        Assert.Equal([2L], result.Matches.Select(match => match.FrameId));
    }

    [Fact]
    public async Task SearchAsync_AttributeKeyFilterMatchesWithoutText()
    {
        var query = new FakeQueryService([
            new MessageFrameRecord(1, "session-a", DateTime.UtcNow, FrameDirection.Rx, "ignored"u8.ToArray(), MessageFormat.Text, "test"),
            new MessageFrameRecord(2, "session-a", DateTime.UtcNow, FrameDirection.Rx, "ignored"u8.ToArray(), MessageFormat.Text, "test", new Dictionary<string, string>
            {
                ["crc"] = "ok"
            })
        ]);
        var search = new MessageFrameSearchService(query);

        var result = await search.SearchAsync(new MessageFrameSearchQuery("session-a", MessageFrameDataSource.LiveSpool, "attr:crc"));

        Assert.Equal([2L], result.Matches.Select(match => match.FrameId));
    }

    [Fact]
    public async Task SearchAsync_DoesNotMatchHexRepresentation()
    {
        var query = new FakeQueryService([
            new MessageFrameRecord(1, "session-a", DateTime.UtcNow, FrameDirection.Rx, [0xCA, 0xFE], MessageFormat.Hex, "test")
        ]);
        var search = new MessageFrameSearchService(query);

        var result = await search.SearchAsync(new MessageFrameSearchQuery("session-a", MessageFrameDataSource.LiveSpool, "CA FE"));

        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task SearchAsync_HonorsCancellation()
    {
        var frames = Enumerable.Range(1, 2000)
            .Select(i => new MessageFrameRecord(i, "session-a", DateTime.UtcNow, FrameDirection.Rx, Encoding.UTF8.GetBytes($"frame {i}"), MessageFormat.Text, "test"))
            .ToArray();
        var query = new FakeQueryService(frames);
        var search = new MessageFrameSearchService(query);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            search.SearchAsync(new MessageFrameSearchQuery("session-a", MessageFrameDataSource.LiveSpool, "frame"), cts.Token));
    }

    private sealed class FakeQueryService : IMessageFrameQueryService
    {
        private readonly IReadOnlyList<MessageFrameRecord> _frames;

        public FakeQueryService(IReadOnlyList<MessageFrameRecord> frames)
        {
            _frames = frames.OrderBy(frame => frame.FrameId).ToArray();
        }

        public MessageFrameQueryResult Query(MessageFrameQuery query)
        {
            var frames = _frames
                .Where(frame => string.Equals(frame.SessionId, query.SessionId, StringComparison.Ordinal))
                .ToArray();
            if (frames.Length == 0)
            {
                return new MessageFrameQueryResult(MessageFrameQueryStatus.NoFrames, Array.Empty<MessageFrameRecord>(), 1, 0);
            }

            var first = frames[0].FrameId;
            var last = frames[^1].FrameId;
            var page = query.Kind switch
            {
                MessageFrameQueryKind.Latest => frames.TakeLast(query.Limit).ToArray(),
                MessageFrameQueryKind.After => frames.Where(frame => frame.FrameId > query.FrameId).Take(query.Limit).ToArray(),
                MessageFrameQueryKind.Before => frames.Where(frame => frame.FrameId < query.FrameId).TakeLast(query.Limit).ToArray(),
                _ => Array.Empty<MessageFrameRecord>()
            };

            return new MessageFrameQueryResult(
                page.Length == 0 ? MessageFrameQueryStatus.NoMoreAfter : MessageFrameQueryStatus.Ok,
                page,
                first,
                last);
        }
    }
}
