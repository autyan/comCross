using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

internal static class LanguageChangedHandler
{
    public static PluginHostResponse Handle(PluginHostRequest request)
    {
        // Forward-compatibility hook: allow the main process to notify plugin host about UI language changes.
        // Currently a no-op acknowledgement.
        return new PluginHostResponse(request.Id, true);
    }
}
