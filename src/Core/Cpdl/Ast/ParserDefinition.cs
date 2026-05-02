namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// 解析器定义节点
/// 对应 CPDL 中的 parser { ... }
/// </summary>
public class ParserDefinition : AstNode
{
    public override string NodeType => "ParserDefinition";
    
    /// <summary>
    /// 解析规则列表（when条件）
    /// </summary>
    public List<WhenRule> Rules { get; set; } = new();
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitParserDefinition(this);
    }
    
    public override string ToString()
    {
        return $"Parser with {Rules.Count} rules";
    }
}

/// <summary>
/// when规则
/// 对应 when (condition) { actions }
/// </summary>
public class WhenRule : AstNode
{
    public override string NodeType => "WhenRule";
    
    /// <summary>
    /// 条件表达式
    /// </summary>
    public Expression Condition { get; set; } = null!;
    
    /// <summary>
    /// 动作列表（display, color等）
    /// </summary>
    public List<ParserAction> Actions { get; set; } = new();
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitWhenRule(this);
    }
    
    public override string ToString()
    {
        return $"when ({Condition}) with {Actions.Count} actions";
    }
}

/// <summary>
/// 解析器动作
/// 如: display "...", color info
/// </summary>
public class ParserAction
{
    /// <summary>
    /// 动作类型（display, color等）
    /// </summary>
    public string Type { get; set; } = "";
    
    /// <summary>
    /// 动作参数
    /// </summary>
    public List<Expression> Arguments { get; set; } = new();
    
    /// <summary>
    /// 动作在源码中的位置
    /// </summary>
    public int Line { get; set; }
    public int Column { get; set; }
    
    public override string ToString()
    {
        var args = string.Join(", ", Arguments.Select(a => a.ToString()));
        return $"{Type}({args})";
    }
}
