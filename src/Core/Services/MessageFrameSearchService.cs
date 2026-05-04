using System.Text;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed record MessageFrameSearchQuery(
    string SessionId,
    MessageFrameDataSource Source,
    string Text,
    FrameDirection? Direction = null);

public sealed record MessageFrameSearchMatch(
    long FrameId,
    DateTime TimestampUtc,
    FrameDirection Direction,
    string Preview);

public sealed record MessageFrameSearchResult(
    MessageFrameQueryStatus Status,
    IReadOnlyList<MessageFrameSearchMatch> Matches,
    long? FirstAvailableFrameId = null,
    long? LastAvailableFrameId = null);

public interface IMessageFrameSearchService
{
    Task<MessageFrameSearchResult> SearchAsync(
        MessageFrameSearchQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class MessageFrameSearchService : IMessageFrameSearchService
{
    private const int SearchBatchSize = 512;
    private const int PreviewMaxLength = 160;

    private static readonly UTF8Encoding Utf8 = new(false, false);

    private readonly IMessageFrameQueryService _queryService;

    public MessageFrameSearchService(IMessageFrameQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public Task<MessageFrameSearchResult> SearchAsync(
        MessageFrameSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.SessionId) || string.IsNullOrWhiteSpace(query.Text))
        {
            return Task.FromResult(new MessageFrameSearchResult(MessageFrameQueryStatus.InvalidQuery, Array.Empty<MessageFrameSearchMatch>()));
        }

        var criteria = SearchCriteria.Parse(query);
        if (criteria.IsEmpty)
        {
            return Task.FromResult(new MessageFrameSearchResult(MessageFrameQueryStatus.InvalidQuery, Array.Empty<MessageFrameSearchMatch>()));
        }

        var capture = _queryService.Query(new MessageFrameQuery(
            query.SessionId,
            query.Source,
            MessageFrameQueryKind.Latest,
            0,
            1));

        if (capture.Status is MessageFrameQueryStatus.InvalidQuery
            or MessageFrameQueryStatus.SourceUnavailable
            or MessageFrameQueryStatus.ArchiveDisabled
            or MessageFrameQueryStatus.ArchiveError)
        {
            return Task.FromResult(new MessageFrameSearchResult(capture.Status, Array.Empty<MessageFrameSearchMatch>(), capture.FirstAvailableFrameId, capture.LastAvailableFrameId));
        }

        var firstAvailable = capture.FirstAvailableFrameId ?? 1;
        var lastAvailable = capture.LastAvailableFrameId ?? 0;
        if (lastAvailable <= 0)
        {
            return Task.FromResult(new MessageFrameSearchResult(MessageFrameQueryStatus.NoFrames, Array.Empty<MessageFrameSearchMatch>(), firstAvailable, lastAvailable));
        }

        var matches = new List<MessageFrameSearchMatch>();
        var cursor = firstAvailable - 1;
        var status = MessageFrameQueryStatus.Ok;

        while (cursor < lastAvailable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = _queryService.Query(new MessageFrameQuery(
                query.SessionId,
                query.Source,
                MessageFrameQueryKind.After,
                cursor,
                SearchBatchSize));

            if (page.Status == MessageFrameQueryStatus.DataEvicted)
            {
                status = MessageFrameQueryStatus.DataEvicted;
                if (page.FirstAvailableFrameId is { } available && available > cursor + 1)
                {
                    cursor = available - 1;
                }
            }
            else if (page.Status is MessageFrameQueryStatus.SourceUnavailable
                     or MessageFrameQueryStatus.InvalidQuery
                     or MessageFrameQueryStatus.ArchiveDisabled
                     or MessageFrameQueryStatus.ArchiveError)
            {
                status = page.Status;
                break;
            }

            if (page.Frames.Count == 0)
            {
                break;
            }

            foreach (var frame in page.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (frame.FrameId > lastAvailable)
                {
                    break;
                }

                if (IsMatch(frame, criteria))
                {
                    matches.Add(new MessageFrameSearchMatch(
                        frame.FrameId,
                        frame.TimestampUtc,
                        frame.Direction,
                        BuildPreview(frame)));
                }

                cursor = frame.FrameId;
            }
        }

        return Task.FromResult(new MessageFrameSearchResult(status, matches, firstAvailable, lastAvailable));
    }

    private static bool IsMatch(MessageFrameRecord frame, SearchCriteria criteria)
    {
        if (criteria.Direction is { } direction && frame.Direction != direction)
        {
            return false;
        }

        foreach (var filter in criteria.AttributeFilters)
        {
            if (!MatchesAttributeFilter(frame, filter))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(criteria.Text))
        {
            return true;
        }

        var text = criteria.Text;
        if (Contains(RawUtf8(frame.RawData), text))
        {
            return true;
        }

        if (Contains(frame.Direction.ToString(), text))
        {
            return true;
        }

        return frame.Attributes.Any(pair => Contains(pair.Key, text) || Contains(pair.Value, text));
    }

    private static bool MatchesAttributeFilter(MessageFrameRecord frame, AttributeSearchFilter filter)
        => frame.Attributes.Any(pair =>
            string.Equals(pair.Key, filter.Key, StringComparison.CurrentCultureIgnoreCase)
            && (filter.Value is null || Contains(pair.Value, filter.Value)));

    private static bool Contains(string value, string query)
        => value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string BuildPreview(MessageFrameRecord frame)
    {
        var raw = RawUtf8(frame.RawData);
        if (raw.Length <= PreviewMaxLength)
        {
            return raw;
        }

        return raw[..PreviewMaxLength];
    }

    private static string RawUtf8(byte[] data)
        => data.Length == 0 ? string.Empty : Utf8.GetString(data);

    private sealed record AttributeSearchFilter(string Key, string? Value);

    private sealed record SearchCriteria(
        string Text,
        FrameDirection? Direction,
        IReadOnlyList<AttributeSearchFilter> AttributeFilters)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Direction is null && AttributeFilters.Count == 0;

        public static SearchCriteria Parse(MessageFrameSearchQuery query)
        {
            var direction = query.Direction;
            var attributes = new List<AttributeSearchFilter>();
            var textParts = new List<string>();

            foreach (var token in SplitTokens(query.Text))
            {
                if (TryParseDirection(token, out var parsedDirection))
                {
                    direction = parsedDirection;
                    continue;
                }

                if (TryParseAttribute(token, out var attribute))
                {
                    attributes.Add(attribute);
                    continue;
                }

                textParts.Add(token);
            }

            return new SearchCriteria(string.Join(' ', textParts).Trim(), direction, attributes);
        }

        private static IEnumerable<string> SplitTokens(string text)
            => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool TryParseDirection(string token, out FrameDirection direction)
        {
            direction = default;
            if (!token.StartsWith("dir:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = token[4..];
            if (value.Equals("rx", StringComparison.OrdinalIgnoreCase))
            {
                direction = FrameDirection.Rx;
                return true;
            }

            if (value.Equals("tx", StringComparison.OrdinalIgnoreCase))
            {
                direction = FrameDirection.Tx;
                return true;
            }

            return false;
        }

        private static bool TryParseAttribute(string token, out AttributeSearchFilter attribute)
        {
            attribute = new AttributeSearchFilter(string.Empty, null);
            if (!token.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var expression = token[5..];
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            var equalsIndex = expression.IndexOf('=');
            if (equalsIndex < 0)
            {
                attribute = new AttributeSearchFilter(expression, null);
                return true;
            }

            var key = expression[..equalsIndex];
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            attribute = new AttributeSearchFilter(key, expression[(equalsIndex + 1)..]);
            return true;
        }
    }
}
