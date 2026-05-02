using System.Collections.ObjectModel;
using System.Text;

namespace ComCross.Shared.Models;

public static class MessageFrameAttributes
{
    public const int SchemaVersion = 1;
    public const int MaxCount = 8;
    public const int MaxKeyBytes = 32;
    public const int MaxValueBytes = 128;

    public static readonly IReadOnlyDictionary<string, string> Empty =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, string> Normalize(
        IReadOnlyDictionary<string, string>? attributes,
        Action<string>? reportDiagnostic = null)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return Empty;
        }

        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in attributes.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            if (accepted.Count >= MaxCount)
            {
                reportDiagnostic?.Invoke($"Message frame attribute limit exceeded. Kept first {MaxCount} valid attributes.");
                break;
            }

            if (!IsValidKey(pair.Key))
            {
                reportDiagnostic?.Invoke($"Message frame attribute key is invalid: '{pair.Key}'.");
                continue;
            }

            if (pair.Value is null)
            {
                reportDiagnostic?.Invoke($"Message frame attribute value is null: '{pair.Key}'.");
                continue;
            }

            var value = pair.Value;
            if (Encoding.UTF8.GetByteCount(value) > MaxValueBytes)
            {
                reportDiagnostic?.Invoke($"Message frame attribute value exceeds {MaxValueBytes} bytes: '{pair.Key}'.");
                continue;
            }

            accepted[pair.Key] = value;
        }

        return accepted.Count == 0
            ? Empty
            : new ReadOnlyDictionary<string, string>(accepted);
    }

    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes)
        {
            return false;
        }

        foreach (var ch in key)
        {
            var valid = ch is >= 'a' and <= 'z'
                || ch is >= '0' and <= '9'
                || ch is '.' or '_' or '-';

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
