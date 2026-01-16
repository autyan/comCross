namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// 枚举定义节点
/// 对应 CPDL 中的 enum { ... }
/// </summary>
public class EnumDefinition : AstNode
{
    public override string NodeType => "EnumDefinition";
    
    /// <summary>
    /// 枚举名称
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// 基础类型（默认为 int32）
    /// </summary>
    public string BaseType { get; set; } = "int32";
    
    /// <summary>
    /// 枚举成员列表
    /// </summary>
    public List<EnumMember> Members { get; set; } = new();
    
    /// <summary>
    /// 枚举描述（可选）
    /// </summary>
    public string? Description { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitEnumDefinition(this);
    }
    
    public override string ToString()
    {
        return $"Enum '{Name}' ({BaseType}) with {Members.Count} members";
    }
}

/// <summary>
/// 枚举成员
/// 如: READ_HOLDING_REGISTERS = 0x03
/// </summary>
public class EnumMember : AstNode
{
    public override string NodeType => "EnumMember";
    
    /// <summary>
    /// 成员名称
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// 成员值（可以是整数或表达式）
    /// </summary>
    public Expression Value { get; set; } = null!;
    
    /// <summary>
    /// 成员描述（可选）
    /// </summary>
    public string? Description { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitEnumMember(this);
    }
    
    public override string ToString()
    {
        return $"{Name} = {Value}";
    }
}
