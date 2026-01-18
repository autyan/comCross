using ComCross.PluginSdk;

namespace ComCross.Core.Services;

/// <summary>
/// 插件安全验证器
/// 根据插件类型执行不同的验证规则
/// </summary>
public sealed class PluginSecurityValidator
{
    /// <summary>
    /// 验证插件元数据和实现
    /// </summary>
    /// <param name="plugin">待验证的插件实例</param>
    /// <returns>验证结果</returns>
    public PluginValidationResult Validate(IDevicePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(plugin.Metadata);
        
        var result = new PluginValidationResult
        {
            PluginId = plugin.Metadata.Id,
            PluginType = plugin.Metadata.Type
        };
        
        // 验证基本元数据
        ValidateMetadata(plugin.Metadata, result);
        
        // 根据插件类型执行特定验证
        switch (plugin.Metadata.Type)
        {
            case PluginType.BusAdapter:
                ValidateBusAdapter(plugin, result);
                break;
                
            case PluginType.FlowProcessor:
            case PluginType.Statistics:
            case PluginType.UIExtension:
            case PluginType.Extension:
                // 其他类型插件：仅基本验证
                break;
                
            default:
                result.AddError("PluginType", $"未知的插件类型: {plugin.Metadata.Type}");
                break;
        }
        
        return result;
    }
    
    /// <summary>
    /// 验证插件元数据完整性
    /// </summary>
    private void ValidateMetadata(PluginMetadata metadata, PluginValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(metadata.Id))
        {
            result.AddError("Id", "插件ID不能为空");
        }
        
        if (string.IsNullOrWhiteSpace(metadata.Name))
        {
            result.AddError("Name", "插件名称不能为空");
        }
        
        if (string.IsNullOrWhiteSpace(metadata.Version))
        {
            result.AddError("Version", "插件版本不能为空");
        }
        else if (!System.Version.TryParse(metadata.Version, out _))
        {
            result.AddError("Version", $"插件版本格式无效: {metadata.Version}");
        }
    }
    
    /// <summary>
    /// 验证总线适配器插件（严格验证）
    /// BusAdapter 插件必须正确实现共享内存接口
    /// </summary>
    private void ValidateBusAdapter(IDevicePlugin plugin, PluginValidationResult result)
    {
        // If the plugin supports schema-driven capability description, validate it.
        if (plugin is IPluginCapabilityProvider provider)
        {
            try
            {
                var capabilities = provider.GetCapabilities();
                if (capabilities is null || capabilities.Count == 0)
                {
                    result.AddError("BusAdapter", "BusAdapter 插件必须至少声明一个 capability");
                    return;
                }

                foreach (var capability in capabilities)
                {
                    if (string.IsNullOrWhiteSpace(capability.Id) || string.IsNullOrWhiteSpace(capability.Name))
                    {
                        result.AddError("BusAdapter", "Capability 必须包含 Id 与 Name");
                        return;
                    }

                    capability.SharedMemoryRequest?.Validate();
                }

                result.AddInfo("BusAdapter", $"Capabilities 已声明：{capabilities.Count} 个");
            }
            catch (Exception ex)
            {
                result.AddError("BusAdapter", $"Capabilities 声明无效: {ex.Message}");
                return;
            }
        }

        // 验证初始化方法存在性
        var initMethod = plugin.GetType().GetMethod(nameof(IDevicePlugin.SetSharedMemoryWriter));
        if (initMethod == null)
        {
            result.AddError("BusAdapter", "BusAdapter 插件必须实现 SetSharedMemoryWriter 方法");
            return;
        }
        
        // 验证反压方法存在性
        var backpressureMethod = plugin.GetType().GetMethod(nameof(IDevicePlugin.SetBackpressureLevel));
        if (backpressureMethod == null)
        {
            result.AddError("BusAdapter", "BusAdapter 插件必须实现 SetBackpressureLevel 方法");
            return;
        }
        
        // 测试共享内存写入接口（模拟调用）
        try
        {
            // 创建测试用的写入器（空实现）
            var testWriter = new TestSharedMemoryWriter();
            plugin.SetSharedMemoryWriter(testWriter);
            
            // 验证插件能接受反压信号
            plugin.SetBackpressureLevel(BackpressureLevel.None);
            plugin.SetBackpressureLevel(BackpressureLevel.Medium);
            plugin.SetBackpressureLevel(BackpressureLevel.High);
            
            result.AddInfo("BusAdapter", "BusAdapter 插件接口验证通过");
        }
        catch (Exception ex)
        {
            result.AddError("BusAdapter", $"BusAdapter 插件接口测试失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 测试用的共享内存写入器（空实现）
    /// </summary>
    private sealed class TestSharedMemoryWriter : ISharedMemoryWriter
    {
        public bool TryWriteFrame(ReadOnlySpan<byte> data, out long frameId)
        {
            frameId = 1;
            return true;
        }
        
        public long GetFreeSpace() => long.MaxValue;
        public double GetUsageRatio() => 0.0;
    }
}

/// <summary>
/// 插件验证结果
/// </summary>
public sealed class PluginValidationResult
{
    private readonly List<ValidationMessage> _messages = new();
    
    /// <summary>
    /// 插件ID
    /// </summary>
    public string? PluginId { get; init; }
    
    /// <summary>
    /// 插件类型
    /// </summary>
    public PluginType PluginType { get; init; }
    
    /// <summary>
    /// 是否验证通过（无错误）
    /// </summary>
    public bool IsValid => !_messages.Any(m => m.Level == ValidationLevel.Error);
    
    /// <summary>
    /// 所有验证消息
    /// </summary>
    public IReadOnlyList<ValidationMessage> Messages => _messages;
    
    /// <summary>
    /// 错误消息列表
    /// </summary>
    public IEnumerable<ValidationMessage> Errors => _messages.Where(m => m.Level == ValidationLevel.Error);
    
    /// <summary>
    /// 警告消息列表
    /// </summary>
    public IEnumerable<ValidationMessage> Warnings => _messages.Where(m => m.Level == ValidationLevel.Warning);
    
    /// <summary>
    /// 信息消息列表
    /// </summary>
    public IEnumerable<ValidationMessage> Infos => _messages.Where(m => m.Level == ValidationLevel.Info);
    
    /// <summary>
    /// 添加错误消息
    /// </summary>
    public void AddError(string category, string message)
    {
        _messages.Add(new ValidationMessage
        {
            Level = ValidationLevel.Error,
            Category = category,
            Message = message
        });
    }
    
    /// <summary>
    /// 添加警告消息
    /// </summary>
    public void AddWarning(string category, string message)
    {
        _messages.Add(new ValidationMessage
        {
            Level = ValidationLevel.Warning,
            Category = category,
            Message = message
        });
    }
    
    /// <summary>
    /// 添加信息消息
    /// </summary>
    public void AddInfo(string category, string message)
    {
        _messages.Add(new ValidationMessage
        {
            Level = ValidationLevel.Info,
            Category = category,
            Message = message
        });
    }
    
    /// <summary>
    /// 获取验证摘要
    /// </summary>
    public string GetSummary()
    {
        if (IsValid)
        {
            return $"插件 '{PluginId}' ({PluginType}) 验证通过";
        }
        
        var errorCount = Errors.Count();
        var warningCount = Warnings.Count();
        return $"插件 '{PluginId}' ({PluginType}) 验证失败: {errorCount} 个错误, {warningCount} 个警告";
    }
}

/// <summary>
/// 验证消息
/// </summary>
public sealed class ValidationMessage
{
    /// <summary>
    /// 消息级别
    /// </summary>
    public ValidationLevel Level { get; init; }
    
    /// <summary>
    /// 消息分类
    /// </summary>
    public required string Category { get; init; }
    
    /// <summary>
    /// 消息内容
    /// </summary>
    public required string Message { get; init; }
    
    public override string ToString()
    {
        return $"[{Level}] {Category}: {Message}";
    }
}

/// <summary>
/// 验证级别
/// </summary>
public enum ValidationLevel
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,
    
    /// <summary>
    /// 警告
    /// </summary>
    Warning,
    
    /// <summary>
    /// 错误
    /// </summary>
    Error
}
