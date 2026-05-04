using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class MessageFrameQueryService : IMessageFrameQueryService
{
    private const int ScanBatchSize = 512;

    private readonly IFrameStore _frameStore;
    private readonly ILogger<MessageFrameQueryService> _logger;

    public MessageFrameQueryService(IFrameStore frameStore, ILogger<MessageFrameQueryService> logger)
    {
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MessageFrameQueryResult Query(MessageFrameQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.SessionId) || query.Limit <= 0)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.InvalidQuery, Array.Empty<MessageFrameRecord>());
        }

        if (query.Source == MessageFrameDataSource.Archive)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.ArchiveDisabled, Array.Empty<MessageFrameRecord>());
        }

        if (query.Source != MessageFrameDataSource.LiveSpool)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.InvalidQuery, Array.Empty<MessageFrameRecord>());
        }

        try
        {
            return query.Kind switch
            {
                MessageFrameQueryKind.Latest => QueryLatest(query),
                MessageFrameQueryKind.After => QueryAfter(query),
                MessageFrameQueryKind.Before => QueryBefore(query),
                _ => new MessageFrameQueryResult(MessageFrameQueryStatus.InvalidQuery, Array.Empty<MessageFrameRecord>())
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Message frame query failed: SessionId={SessionId} Source={Source} Kind={Kind}", query.SessionId, query.Source, query.Kind);
            return new MessageFrameQueryResult(MessageFrameQueryStatus.SourceUnavailable, Array.Empty<MessageFrameRecord>(), ErrorCode: "query-failed");
        }
    }

    private MessageFrameQueryResult QueryLatest(MessageFrameQuery query)
    {
        var info = _frameStore.GetWindowInfo(query.SessionId);
        if (info.LastFrameId <= 0)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.NoFrames, Array.Empty<MessageFrameRecord>(), info.FirstAvailableFrameId, info.LastFrameId);
        }

        var afterFrameId = Math.Max(info.FirstAvailableFrameId - 1, info.LastFrameId - query.Limit);
        var frames = _frameStore.ReadAfter(query.SessionId, afterFrameId, query.Limit, out var firstAvailable)
            .Select(static frame => frame.ToMessageFrameRecord())
            .ToList();

        return new MessageFrameQueryResult(
            frames.Count == 0 ? MessageFrameQueryStatus.NoFrames : MessageFrameQueryStatus.Ok,
            frames,
            firstAvailable,
            info.LastFrameId);
    }

    private MessageFrameQueryResult QueryAfter(MessageFrameQuery query)
    {
        var info = _frameStore.GetWindowInfo(query.SessionId);
        if (info.LastFrameId <= 0)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.NoFrames, Array.Empty<MessageFrameRecord>(), info.FirstAvailableFrameId, info.LastFrameId);
        }

        if (query.FrameId >= info.LastFrameId)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.NoMoreAfter, Array.Empty<MessageFrameRecord>(), info.FirstAvailableFrameId, info.LastFrameId);
        }

        var frames = _frameStore.ReadAfter(query.SessionId, query.FrameId, query.Limit, out var firstAvailable)
            .Select(static frame => frame.ToMessageFrameRecord())
            .ToList();
        var status = query.FrameId + 1 < firstAvailable
            ? MessageFrameQueryStatus.DataEvicted
            : frames.Count == 0
                ? MessageFrameQueryStatus.NoMoreAfter
                : MessageFrameQueryStatus.Ok;

        return new MessageFrameQueryResult(status, frames, firstAvailable, info.LastFrameId);
    }

    private MessageFrameQueryResult QueryBefore(MessageFrameQuery query)
    {
        if (query.FrameId <= 0)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.InvalidQuery, Array.Empty<MessageFrameRecord>());
        }

        var info = _frameStore.GetWindowInfo(query.SessionId);
        if (info.LastFrameId <= 0)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.NoFrames, Array.Empty<MessageFrameRecord>(), info.FirstAvailableFrameId, info.LastFrameId);
        }

        if (query.FrameId <= info.FirstAvailableFrameId)
        {
            var status = info.FirstAvailableFrameId > 1
                ? MessageFrameQueryStatus.DataEvicted
                : MessageFrameQueryStatus.NoMoreBefore;
            return new MessageFrameQueryResult(status, Array.Empty<MessageFrameRecord>(), info.FirstAvailableFrameId, info.LastFrameId);
        }

        var exclusiveUpper = Math.Min(query.FrameId, info.LastFrameId + 1);
        var cursor = info.FirstAvailableFrameId - 1;
        var window = new Queue<MessageFrameRecord>(query.Limit);
        long firstAvailable = info.FirstAvailableFrameId;

        while (cursor < exclusiveUpper - 1)
        {
            var frames = _frameStore.ReadAfter(query.SessionId, cursor, ScanBatchSize, out firstAvailable);
            if (frames.Count == 0)
            {
                break;
            }

            foreach (var frame in frames)
            {
                if (frame.FrameId >= exclusiveUpper)
                {
                    break;
                }

                if (window.Count == query.Limit)
                {
                    window.Dequeue();
                }

                window.Enqueue(frame.ToMessageFrameRecord());
                cursor = frame.FrameId;
            }

            var last = frames[^1].FrameId;
            if (last >= exclusiveUpper - 1 || last == cursor)
            {
                if (last == cursor)
                {
                    cursor = last;
                }

                if (last >= exclusiveUpper - 1)
                {
                    break;
                }
            }
        }

        if (window.Count == 0)
        {
            return new MessageFrameQueryResult(MessageFrameQueryStatus.NoMoreBefore, Array.Empty<MessageFrameRecord>(), firstAvailable, info.LastFrameId);
        }

        return new MessageFrameQueryResult(MessageFrameQueryStatus.Ok, window.ToList(), firstAvailable, info.LastFrameId);
    }
}
