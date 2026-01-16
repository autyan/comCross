using System;

namespace ComCross.Shell.Models;

/// <summary>
/// Information about a bus adapter plugin
/// </summary>
public sealed class BusAdapterInfo
{
    /// <summary>
    /// Unique identifier for the adapter (e.g., "serial", "tcp-client", "udp")
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the adapter (e.g., "Serial (RS232)", "TCP/IP Client")
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Icon/emoji for the adapter (e.g., "🔌", "🌐", "📡")
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Type of the configuration panel UserControl for this adapter
    /// Set to null if no custom config panel is needed
    /// </summary>
    public Type? ConfigPanelType { get; init; }

    /// <summary>
    /// Description of the adapter (optional)
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Is this adapter currently available/enabled?
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Display text for ComboBox (Icon + Name)
    /// </summary>
    public string DisplayText => $"{Icon} {Name}";
}
