namespace ComCross.PluginSdk;

/// <summary>
/// 设备插件接口（插件SDK版本）
/// 第三方插件必须实现此接口
/// </summary>
public interface IDevicePlugin
{
    /// <summary>
    /// 插件元数据
    /// </summary>
    PluginMetadata Metadata { get; }
    
    /// <summary>
    /// 设置共享内存写入器（由主进程注入）
    /// </summary>
    /// <param name="writer">安全的共享内存写入接口</param>
    void SetSharedMemoryWriter(ISharedMemoryWriter writer);
    
    /// <summary>
    /// 设置背压等级（由主进程通知）
    /// </summary>
    /// <param name="level">当前背压等级</param>
    void SetBackpressureLevel(BackpressureLevel level);
}

/// <summary>
/// 插件元数据
/// </summary>
public class PluginMetadata
{
    /// <summary>
    /// 插件唯一标识
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// 插件名称
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// 版本号
    /// </summary>
    public required string Version { get; init; }
    
    /// <summary>
    /// 插件类型
    /// </summary>
    public PluginType Type { get; init; } = PluginType.Extension;
    
    /// <summary>
    /// 作者
    /// </summary>
    public string? Author { get; init; }
    
    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; init; }
}
