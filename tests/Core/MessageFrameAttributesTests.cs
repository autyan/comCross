using ComCross.Shared.Models;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class MessageFrameAttributesTests
{
    [Fact]
    public void Normalize_DropsInvalidAndOversizedAttributes()
    {
        var valueTooLong = new string('x', MessageFrameAttributes.MaxValueBytes + 1);
        var diagnostics = new List<string>();
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["valid.key"] = "ok",
            ["InvalidKey"] = "drop",
            ["too-long"] = valueTooLong
        };

        var result = MessageFrameAttributes.Normalize(source, diagnostics.Add);

        Assert.Single(result);
        Assert.Equal("ok", result["valid.key"]);
        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    public void Normalize_KeepsFirstEightSortedValidAttributes()
    {
        var source = Enumerable.Range(0, MessageFrameAttributes.MaxCount + 2)
            .ToDictionary(i => $"k{i:00}", i => i.ToString(), StringComparer.Ordinal);

        var result = MessageFrameAttributes.Normalize(source);

        Assert.Equal(MessageFrameAttributes.MaxCount, result.Count);
        Assert.Equal("k00", result.Keys.First());
        Assert.Equal("k07", result.Keys.Last());
    }
}
