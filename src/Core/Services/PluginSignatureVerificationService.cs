using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Entry point for plugin package signature/trust verification.
///
/// Today this is intentionally conservative and disabled by default.
/// In the future this can be extended to perform cryptographic verification
/// of plugin packages before they are started.
/// </summary>
public sealed class PluginSignatureVerificationService
{
    private readonly SettingsService _settings;
    private readonly ILogger<PluginSignatureVerificationService> _logger;

    public PluginSignatureVerificationService(SettingsService settings, ILogger<PluginSignatureVerificationService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsTrusted(PluginInfo plugin, out string? error)
    {
        error = null;

        var policy = _settings.Current.Plugins.SignatureVerification;
        if (!policy.Enabled)
        {
            return true;
        }

        var id = plugin.Manifest.Id;
        if (!string.IsNullOrWhiteSpace(id)
            && policy.AllowUnsignedPluginIds.Contains(id, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "Plugin signature verification is enabled, but plugin '{PluginId}' is allow-listed as unsigned (temporary rollout mode).",
                id);
            return true;
        }

        // Future: verify cryptographic signature of the plugin package.
        error = "Plugin signature verification is enabled. This plugin is not trusted (no signature verifier configured).";
        return false;
    }
}
