namespace ComCross.PluginSdk;

/// <summary>
/// 物理帧 - 插件从设备读取的原始数据单元
/// 由插件负责物理分帧（根据设备特性切分）
/// </summary>
public class PhysicalFrame
{
    /// <summary>
    /// 帧ID（自增序列号）
    /// </summary>
    public long FrameId { get; set; }
    
    /// <summary>
    /// Session ID
    /// </summary>
    public required string SessionId { get; set; }
    
    /// <summary>
    /// 原始数据（字节数组）
    /// </summary>
    public required byte[] Data { get; set; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 方向（TX=发送, RX=接收）
    /// </summary>
    public MessageDirection Direction { get; set; }
    
    /// <summary>
    /// 数据长度（字节）
    /// </summary>
    public int Length => Data.Length;
}
