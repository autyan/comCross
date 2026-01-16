namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// 表达式基类
/// CPDL中所有表达式都派生自此类
/// </summary>
public abstract class Expression : AstNode
{
}

/// <summary>
/// 二元表达式
/// 如: a + b, x == y, count > 10
/// </summary>
public class BinaryExpression : Expression
{
    public override string NodeType => "BinaryExpression";
    
    /// <summary>
    /// 左操作数
    /// </summary>
    public Expression Left { get; set; } = null!;
    
    /// <summary>
    /// 运算符（+, -, *, /, ==, !=, >, <, >=, <=, &&, ||等）
    /// </summary>
    public string Operator { get; set; } = "";
    
    /// <summary>
    /// 右操作数
    /// </summary>
    public Expression Right { get; set; } = null!;
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinaryExpression(this);
    }
    
    public override string ToString()
    {
        return $"({Left} {Operator} {Right})";
    }
}

/// <summary>
/// 一元表达式
/// 如: -x, !flag, ~mask
/// </summary>
public class UnaryExpression : Expression
{
    public override string NodeType => "UnaryExpression";
    
    /// <summary>
    /// 运算符（-, !, ~）
    /// </summary>
    public string Operator { get; set; } = "";
    
    /// <summary>
    /// 操作数
    /// </summary>
    public Expression Operand { get; set; } = null!;
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitUnaryExpression(this);
    }
    
    public override string ToString()
    {
        return $"{Operator}{Operand}";
    }
}

/// <summary>
/// 字面量表达式
/// 如: 123, 3.14, "hello", true, false, null
/// </summary>
public class LiteralExpression : Expression
{
    public override string NodeType => "LiteralExpression";
    
    /// <summary>
    /// 字面量值
    /// </summary>
    public object? Value { get; set; }
    
    /// <summary>
    /// 字面量类型（用于类型检查）
    /// </summary>
    public string LiteralType { get; set; } = "";
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitLiteralExpression(this);
    }
    
    public override string ToString()
    {
        if (Value == null) return "null";
        if (LiteralType == "string") return $"\"{Value}\"";
        return Value.ToString() ?? "";
    }
}

/// <summary>
/// 标识符表达式
/// 如: device_address, function_code
/// </summary>
public class IdentifierExpression : Expression
{
    public override string NodeType => "IdentifierExpression";
    
    /// <summary>
    /// 标识符名称
    /// </summary>
    public string Name { get; set; } = "";
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIdentifierExpression(this);
    }
    
    public override string ToString()
    {
        return Name;
    }
}

/// <summary>
/// 函数调用表达式
/// 如: crc16(data), validate(this[0..-3])
/// </summary>
public class CallExpression : Expression
{
    public override string NodeType => "CallExpression";
    
    /// <summary>
    /// 函数名称
    /// </summary>
    public string FunctionName { get; set; } = "";
    
    /// <summary>
    /// 参数列表
    /// </summary>
    public List<Expression> Arguments { get; set; } = new();
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCallExpression(this);
    }
    
    public override string ToString()
    {
        var args = string.Join(", ", Arguments.Select(a => a.ToString()));
        return $"{FunctionName}({args})";
    }
}

/// <summary>
/// 成员访问表达式
/// 如: message.field, protocol.message
/// </summary>
public class MemberAccessExpression : Expression
{
    public override string NodeType => "MemberAccessExpression";
    
    /// <summary>
    /// 对象表达式
    /// </summary>
    public Expression Object { get; set; } = null!;
    
    /// <summary>
    /// 成员名称
    /// </summary>
    public string MemberName { get; set; } = "";
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitMemberAccessExpression(this);
    }
    
    public override string ToString()
    {
        return $"{Object}.{MemberName}";
    }
}

/// <summary>
/// 索引表达式
/// 如: data[0], this[0..-3] (数组切片)
/// </summary>
public class IndexExpression : Expression
{
    public override string NodeType => "IndexExpression";
    
    /// <summary>
    /// 被索引的对象
    /// </summary>
    public Expression Object { get; set; } = null!;
    
    /// <summary>
    /// 索引表达式
    /// </summary>
    public Expression Index { get; set; } = null!;
    
    /// <summary>
    /// 切片结束索引（可选，用于 data[start..end]）
    /// </summary>
    public Expression? EndIndex { get; set; }
    
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIndexExpression(this);
    }
    
    public override string ToString()
    {
        if (EndIndex != null)
            return $"{Object}[{Index}..{EndIndex}]";
        return $"{Object}[{Index}]";
    }
}
