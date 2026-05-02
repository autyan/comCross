namespace ComCross.Core.Cpdl.Ast;

/// <summary>
/// AST（抽象语法树）节点基类
/// 所有CPDL语法结构都派生自此类
/// </summary>
public abstract class AstNode
{
    /// <summary>
    /// 节点在源码中的起始行号
    /// </summary>
    public int Line { get; set; }
    
    /// <summary>
    /// 节点在源码中的起始列号
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// 节点类型（用于调试和序列化）
    /// </summary>
    public abstract string NodeType { get; }
    
    /// <summary>
    /// 接受访问者模式（用于编译器后端）
    /// </summary>
    public abstract T Accept<T>(IAstVisitor<T> visitor);
    
    /// <summary>
    /// 返回节点的字符串表示（用于调试）
    /// </summary>
    public override string ToString()
    {
        return $"{NodeType} at Line {Line}, Column {Column}";
    }
}

/// <summary>
/// AST访问者接口（用于编译器遍历AST）
/// </summary>
public interface IAstVisitor<T>
{
    T VisitProtocolDefinition(ProtocolDefinition node);
    T VisitMessageDefinition(MessageDefinition node);
    T VisitFieldDefinition(FieldDefinition node);
    T VisitEnumDefinition(EnumDefinition node);
    T VisitEnumMember(EnumMember node);
    T VisitParserDefinition(ParserDefinition node);
    T VisitWhenRule(WhenRule node);
    T VisitBinaryExpression(BinaryExpression node);
    T VisitUnaryExpression(UnaryExpression node);
    T VisitLiteralExpression(LiteralExpression node);
    T VisitIdentifierExpression(IdentifierExpression node);
    T VisitCallExpression(CallExpression node);
    T VisitMemberAccessExpression(MemberAccessExpression node);
    T VisitIndexExpression(IndexExpression node);
}
