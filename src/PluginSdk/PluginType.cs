namespace ComCross.PluginSdk;

/// <summary>
/// 插件类型
/// </summary>
public enum PluginType
{
    /// <summary>
    /// 总线适配器（如串口、TCP、USB等）
    /// 必须严格验证共享内存API使用
    /// </summary>
    BusAdapter,
    
    /// <summary>
    /// 数据流处理器
    /// </summary>
    FlowProcessor,
    
    /// <summary>
    /// 统计分析工具
    /// </summary>
    Statistics,
    
    /// <summary>
    /// UI扩展
    /// </summary>
    UIExtension,
    
    /// <summary>
    /// 其他扩展
    /// </summary>
    Extension
}
