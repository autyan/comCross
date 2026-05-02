namespace ComCross.Core.Application;

public sealed record ComCrossInstanceIdentity(
    int SchemaVersion,
    string Product,
    string Channel,
    string DirectoryName,
    string InstanceId,
    string SchemaLine,
    string? ManifestPath)
{
    public static ComCrossInstanceIdentity Stable(string? manifestPath = null)
        => new(1, "ComCross", "Stable", "ComCross", "comcross-stable", "v0", manifestPath);
}
