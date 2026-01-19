using ComCross.PluginSdk.UI;

namespace ComCross.Shell.Plugins.UI;

public class AvaloniaPluginUiRenderer : PluginUiRenderer
{
    public AvaloniaPluginUiRenderer(IPluginUiControlFactory factory, PluginUiStateManager stateManager, PluginActionExecutor actionExecutor) 
        : base(factory, stateManager, actionExecutor)
    {
    }

    protected override IPluginUiContainer CreateNewContainer()
    {
        return new AvaloniaPluginUiContainer();
    }
}
