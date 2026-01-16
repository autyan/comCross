namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// 协议定义节点
/// 对应 CPDL 中的 protocol { ... }
/// </summary>
public class ProtocolDefinition : AstNode
{
    public override string NodeType => "ProtocolDefinition";
    
    /// <summary>
    /// 协议名称
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// 协议版本（可选）
    /// </summary>
    public string? Version { get; set; }
    
    /// <summary>
    /// 协议描述（可选）
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 导入的其他协议（可选）
    /// </summary>
    public List<ImportStatement> Imports { get; set; } = new();
    
    /// <summary>
    /// 枚举定义列表
    /// </summary>
    public List<EnumDefinition> Enums { get; set; } = new();
    
    /// <summary>
    /// 消息定义列表
    /// </summary>
    public List<MessageDefinition> Messages { get; set; } = new();
    
    /// <summary>
    /// 解析器定义（可选）
    /// </summary>
    public ParserDefinition? Parser { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProtocolDefinition(this);
    }
    
    public override string ToString()
    {
        return $"Protocol '{Name}' (Version: {Version ?? "N/A"}) with {Messages.Count} messages";
    }
}

/// <summary>
/// 导入语句
/// 对应 import "other-protocol.cpdl" as OtherProtocol;
/// </summary>
public class ImportStatement : AstNode
{
    public override string NodeType => "ImportStatement";
    
    /// <summary>
    /// 导入的文件路径
    /// </summary>
    public string FilePath { get; set; } = "";
    
    /// <summary>
    /// 别名（可选）
    /// </summary>
    public string? Alias { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException("ImportStatement visitor not yet implemented");
    }
}
