namespace ComCross.PluginSdk;

/// <summary>
/// 背压等级
/// 主进程通过此枚举通知插件当前内存压力
/// </summary>
public enum BackpressureLevel
{
    /// <summary>
    /// 无压力（使用率 < 60%）
    /// </summary>
    None,
    
    /// <summary>
    /// 中等压力（使用率 60% - 80%）
    /// 建议降低写入速度
    /// </summary>
    Medium,
    
    /// <summary>
    /// 高压力（使用率 > 80%）
    /// 必须降低写入速度或暂停
    /// </summary>
    High
}
