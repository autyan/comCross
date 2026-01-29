using System;
using ComCross.PluginSdk.UI;

namespace ComCross.Shell.Services;

public static class ShellUiServices
{
    private static IObjectFactory? _objectFactory;
    private static PluginUiStateManager? _pluginUiStateManager;
    private static ITextInputDialogFactory? _textInputDialogFactory;

    public static void Initialize(
        IObjectFactory objectFactory,
        PluginUiStateManager pluginUiStateManager,
        ITextInputDialogFactory textInputDialogFactory)
    {
        _objectFactory = objectFactory;
        _pluginUiStateManager = pluginUiStateManager;
        _textInputDialogFactory = textInputDialogFactory;
    }

    public static IObjectFactory ObjectFactory => _objectFactory ?? throw new InvalidOperationException("ShellUiServices not initialized");

    public static PluginUiStateManager PluginUiStateManager
        => _pluginUiStateManager ?? throw new InvalidOperationException("ShellUiServices not initialized");

    public static ITextInputDialogFactory TextInputDialogFactory
        => _textInputDialogFactory ?? throw new InvalidOperationException("ShellUiServices not initialized");
}
