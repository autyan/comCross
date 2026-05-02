using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComCross.PluginSdk;

public static class PluginResourceKinds
{
    public const string Pending = "pending";
}

public static class PluginResourceIds
{
    public const string All = "all";
}

public static class PluginResourceActionKinds
{
    public const string ConnectScopedResource = "connect-scoped-resource";
    public const string ExecuteAction = "execute-action";
}

public static class PluginResourceActionIds
{
    public const string Accept = "accept";
    public const string Reject = "reject";
    public const string RejectAll = "reject-all";
}

public sealed record PluginResourceListState(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("resourceKind")] string ResourceKind,
    [property: JsonPropertyName("items")] IReadOnlyList<PluginManagedResourceItem> Items,
    [property: JsonPropertyName("bulkActions")] IReadOnlyList<PluginResourceActionDescriptor>? BulkActions = null);

public sealed record PluginManagedResourceItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("actions")] IReadOnlyList<PluginResourceActionDescriptor>? Actions = null);

public sealed record PluginResourceActionDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("labelKey")] string? LabelKey = null,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("actionName")] string? ActionName = null,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters = null,
    [property: JsonPropertyName("sessionName")] string? SessionName = null);
