namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// 字段定义节点
/// 对应 CPDL 中的字段声明，如: uint8 device_address range(1, 247);
/// </summary>
public class FieldDefinition : AstNode
{
    public override string NodeType => "FieldDefinition";
    
    /// <summary>
    /// 字段类型（uint8, int16, string等）
    /// </summary>
    public string Type { get; set; } = "";
    
    /// <summary>
    /// 字段名称
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// 是否可选字段
    /// </summary>
    public bool IsOptional { get; set; }
    
    /// <summary>
    /// 字段修饰符列表（range, endian, validate等）
    /// </summary>
    public List<FieldModifier> Modifiers { get; set; } = new();
    
    /// <summary>
    /// 字段描述（可选）
    /// </summary>
    public string? Description { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFieldDefinition(this);
    }
    
    public override string ToString()
    {
        var optional = IsOptional ? "optional " : "";
        var modifiers = Modifiers.Count > 0 ? $" with {Modifiers.Count} modifiers" : "";
        return $"{optional}{Type} {Name}{modifiers}";
    }
}

/// <summary>
/// 字段修饰符
/// 如: range(1, 247), endian(big), validate(crc16(...))
/// </summary>
public class FieldModifier
{
    /// <summary>
    /// 修饰符名称（range, endian, validate等）
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// 修饰符参数
    /// </summary>
    public List<Expression> Arguments { get; set; } = new();
    
    /// <summary>
    /// 修饰符在源码中的位置
    /// </summary>
    public int Line { get; set; }
    public int Column { get; set; }
    
    public override string ToString()
    {
        var args = string.Join(", ", Arguments.Select(a => a.ToString()));
        return $"{Name}({args})";
    }
}
