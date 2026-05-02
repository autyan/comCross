using Microsoft.Extensions.Logging;
using ComCross.Core.Cpdl.Ast;
using ComCross.Shared.Interfaces;

namespace ComCross.Core.Cpdl.Compiler;

/// <summary>
/// CPDL编译的消息解析器
/// </summary>
public sealed class CpdlMessageParser : BaseMessageParser
{
    private readonly ProtocolDefinition _protocol;
    private readonly ParseDelegate _parseFunc;

    public CpdlMessageParser(
        ProtocolDefinition protocol,
        ParseDelegate parseFunc,
        ILogger<CpdlMessageParser> logger) 
        : base(logger)
    {
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _parseFunc = parseFunc ?? throw new ArgumentNullException(nameof(parseFunc));
    }

    public override string Id => $"cpdl-{_protocol.Name.ToLowerInvariant()}";
    public override string Name => _protocol.Name;
    public override string Version => _protocol.Version ?? "1.0.0";
    public override string Description => _protocol.Description ?? $"CPDL Protocol: {_protocol.Name}";
    public override string Category => "CPDL";
    public override bool IsBuiltIn => false;

    protected override ParseResult ParseCore(ReadOnlySpan<byte> data)
    {
        try
        {
            // 将ReadOnlySpan<byte>转换为byte[]以调用编译后的delegate
            return _parseFunc(data.ToArray());
        }
        catch (Exception ex)
        {
            return ParseResult.Failure($"Parsing failed: {ex.Message}", data.ToArray());
        }
    }

    protected override string FormatCore(ParseResult result, FormatOptions options)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{Name} Message:");
        
        foreach (var (key, value) in result.Fields)
        {
            sb.AppendLine($"  {key}: {value}");
        }
        
        return sb.ToString();
    }
}
