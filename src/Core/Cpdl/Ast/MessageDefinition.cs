namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// 消息定义节点
/// 对应 CPDL 中的 message { ... }
/// </summary>
public class MessageDefinition : AstNode
{
    public override string NodeType => "MessageDefinition";
    
    /// <summary>
    /// 消息名称
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// 继承的父消息（可选）
    /// </summary>
    public string? BaseMessage { get; set; }
    
    /// <summary>
    /// 字段列表
    /// </summary>
    public List<FieldDefinition> Fields { get; set; } = new();
    
    /// <summary>
    /// 消息描述（可选）
    /// </summary>
    public string? Description { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitMessageDefinition(this);
    }
    
    public override string ToString()
    {
        var baseInfo = BaseMessage != null ? $" extends {BaseMessage}" : "";
        return $"Message '{Name}'{baseInfo} with {Fields.Count} fields";
    }
}
