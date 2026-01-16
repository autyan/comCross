namespace ComCross.PluginSdk;

/// <summary>
/// 共享内存写入器接口
/// 插件通过此接口安全地写入数据到共享内存
/// </summary>
public interface ISharedMemoryWriter
{
    /// <summary>
    /// 尝试写入物理帧到共享内存
    /// </summary>
    /// <param name="data">物理帧数据</param>
    /// <param name="frameId">成功时返回帧ID</param>
    /// <returns>true=成功; false=空间不足或其他错误</returns>
    bool TryWriteFrame(ReadOnlySpan<byte> data, out long frameId);
    
    /// <summary>
    /// 获取剩余可用空间（字节）
    /// </summary>
    long GetFreeSpace();
    
    /// <summary>
    /// 获取内存使用率（0.0 - 1.0）
    /// </summary>
    double GetUsageRatio();
}
