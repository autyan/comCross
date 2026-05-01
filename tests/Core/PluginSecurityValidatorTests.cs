using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.PluginSdk;
using Xunit;

namespace ComCross.Tests.Core;

public class PluginSecurityValidatorTests
{
    private readonly PluginSecurityValidator _validator = new();
    
    [Fact]
    public void Validate_ValidBusAdapter_PassesValidation()
    {
        // Arrange
        var plugin = new TestBusAdapterPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-bus-adapter",
                Name = "Test Bus Adapter",
                Version = "1.0.0",
                Type = PluginType.BusAdapter
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(PluginType.BusAdapter, result.PluginType);
        Assert.Contains(result.Infos, m => m.Message.Contains("BusAdapter 插件接口验证通过"));
    }

    [Fact]
    public void Validate_BusAdapterWithoutDeviceContract_FailsValidation()
    {
        var plugin = new TestBusAdapterMetadataOnlyPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "invalid-bus-adapter",
                Name = "Invalid Bus Adapter",
                Version = "1.0.0",
                Type = PluginType.BusAdapter
            }
        };

        var result = _validator.Validate(plugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == "BusAdapter" && e.Message.Contains("IDevicePlugin"));
    }
    
    [Fact]
    public void Validate_BusAdapterWithInvalidMetadata_FailsValidation()
    {
        // Arrange - 缺少必需字段
        var plugin = new TestBusAdapterPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "", // 无效：空ID
                Name = "Test",
                Version = "1.0.0",
                Type = PluginType.BusAdapter
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == "Id");
    }
    
    [Fact]
    public void Validate_BusAdapterWithInvalidVersion_FailsValidation()
    {
        // Arrange
        var plugin = new TestBusAdapterPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-plugin",
                Name = "Test",
                Version = "invalid-version", // 无效版本格式
                Type = PluginType.BusAdapter
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == "Version");
    }
    
    [Fact]
    public void Validate_FlowProcessor_PassesWithBasicValidation()
    {
        // Arrange - FlowProcessor 不需要严格验证
        var plugin = new TestFlowProcessorPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-flow-processor",
                Name = "Test Flow Processor",
                Version = "1.0.0",
                Type = PluginType.FlowProcessor
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(PluginType.FlowProcessor, result.PluginType);
    }
    
    [Fact]
    public void Validate_Statistics_PassesWithBasicValidation()
    {
        // Arrange
        var plugin = new TestStatisticsPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-stats",
                Name = "Test Statistics",
                Version = "2.0.0",
                Type = PluginType.Statistics
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(PluginType.Statistics, result.PluginType);
    }
    
    [Fact]
    public void Validate_UIExtension_PassesWithBasicValidation()
    {
        // Arrange
        var plugin = new TestUIExtensionPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-ui-ext",
                Name = "Test UI Extension",
                Version = "1.5.0",
                Type = PluginType.UIExtension
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(PluginType.UIExtension, result.PluginType);
    }
    
    [Fact]
    public void Validate_Extension_PassesWithBasicValidation()
    {
        // Arrange
        var plugin = new TestExtensionPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-extension",
                Name = "Test Extension",
                Version = "3.0.0",
                Type = PluginType.Extension
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(PluginType.Extension, result.PluginType);
    }

    [Fact]
    public void Validate_WithManifestTypeMismatch_FailsValidation()
    {
        var plugin = new TestExtensionPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-extension",
                Name = "Test Extension",
                Version = "3.0.0",
                Type = PluginType.Extension
            }
        };
        var manifest = new PluginManifest
        {
            Id = "test-extension",
            Name = "Test Extension",
            Version = "3.0.0",
            EntryPoint = "Test.Extension",
            PluginType = PluginType.FlowProcessor
        };

        var result = _validator.Validate(plugin, manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == "Manifest" && e.Message.Contains("不一致"));
    }

    [Fact]
    public void Validate_WithMissingManifestType_AddsWarning()
    {
        var plugin = new TestExtensionPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-extension",
                Name = "Test Extension",
                Version = "3.0.0",
                Type = PluginType.Extension
            }
        };
        var manifest = new PluginManifest
        {
            Id = "test-extension",
            Name = "Test Extension",
            Version = "3.0.0",
            EntryPoint = "Test.Extension"
        };

        var result = _validator.Validate(plugin, manifest);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Category == "Manifest" && w.Message.Contains("pluginType"));
    }
    
    [Fact]
    public void ValidationResult_GetSummary_ReturnsCorrectMessage()
    {
        // Arrange
        var plugin = new TestBusAdapterPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "test-plugin",
                Name = "Test",
                Version = "1.0.0",
                Type = PluginType.BusAdapter
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        var summary = result.GetSummary();
        
        // Assert
        Assert.Contains("test-plugin", summary);
        Assert.Contains("BusAdapter", summary);
        Assert.Contains("验证通过", summary);
    }
    
    [Fact]
    public void ValidationResult_WithErrors_ReturnsFailureSummary()
    {
        // Arrange
        var plugin = new TestBusAdapterPlugin
        {
            Metadata = new PluginMetadata
            {
                Id = "invalid-plugin",
                Name = "", // 无效：空名称
                Version = "1.0.0",
                Type = PluginType.BusAdapter
            }
        };
        
        // Act
        var result = _validator.Validate(plugin);
        var summary = result.GetSummary();
        
        // Assert
        Assert.Contains("验证失败", summary);
        Assert.Contains("错误", summary);
    }
    
    [Fact]
    public void Validate_NullPlugin_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!));
    }
    
    // 测试用插件实现
    
    private class TestBusAdapterPlugin : IDevicePlugin
    {
        public required PluginMetadata Metadata { get; init; }
        
        public void SetSharedMemoryWriter(ISharedMemoryWriter writer)
        {
            // 模拟实现
        }
        
        public void SetBackpressureLevel(BackpressureLevel level)
        {
            // 模拟实现
        }
    }

    private class TestBusAdapterMetadataOnlyPlugin : IPlugin
    {
        public required PluginMetadata Metadata { get; init; }
    }

    private class TestFlowProcessorPlugin : IExtensionPlugin
    {
        public required PluginMetadata Metadata { get; init; }
    }
    
    private class TestStatisticsPlugin : IExtensionPlugin
    {
        public required PluginMetadata Metadata { get; init; }
    }
    
    private class TestUIExtensionPlugin : IExtensionPlugin
    {
        public required PluginMetadata Metadata { get; init; }
    }
    
    private class TestExtensionPlugin : IExtensionPlugin
    {
        public required PluginMetadata Metadata { get; init; }
    }
}
