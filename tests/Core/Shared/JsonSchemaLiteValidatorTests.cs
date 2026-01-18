using System.Text.Json;
using ComCross.Shared.Services;
using Xunit;

namespace ComCross.Tests.Core.Shared;

public sealed class JsonSchemaLiteValidatorTests
{
    [Fact]
    public void TryParseSchema_ShouldRejectEmpty()
    {
        Assert.False(JsonSchemaLiteValidator.TryParseSchema("", out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryValidate_ObjectRequired_ShouldFailWhenMissing()
    {
        var schemaJson = "{\"type\":\"object\",\"required\":[\"port\"],\"properties\":{\"port\":{\"type\":\"string\"}}}";
        Assert.True(JsonSchemaLiteValidator.TryParseSchema(schemaJson, out var schema, out var parseError), parseError);

        using var doc = JsonDocument.Parse("{\"baud\":\"9600\"}");
        Assert.False(JsonSchemaLiteValidator.TryValidate(schema, doc.RootElement, out var error));
        Assert.Contains("port", error);
    }

    [Fact]
    public void TryValidate_TypeMismatch_ShouldFail()
    {
        var schemaJson = "{\"type\":\"object\",\"properties\":{\"baud\":{\"type\":\"integer\"}}}";
        Assert.True(JsonSchemaLiteValidator.TryParseSchema(schemaJson, out var schema, out var parseError), parseError);

        using var doc = JsonDocument.Parse("{\"baud\":\"9600\"}");
        Assert.False(JsonSchemaLiteValidator.TryValidate(schema, doc.RootElement, out var error));
        Assert.Contains("Expected integer", error);
    }

    [Fact]
    public void TryValidate_Enum_ShouldPassWhenMatch()
    {
        var schemaJson = "{\"type\":\"string\",\"enum\":[\"A\",\"B\"]}";
        Assert.True(JsonSchemaLiteValidator.TryParseSchema(schemaJson, out var schema, out var parseError), parseError);

        using var doc = JsonDocument.Parse("\"B\"");
        Assert.True(JsonSchemaLiteValidator.TryValidate(schema, doc.RootElement, out var error), error);
    }
}
