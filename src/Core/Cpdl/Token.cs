namespace ComCross.Core.Cpdl;

/// <summary>
/// CPDL Token - 词法单元
/// </summary>
public readonly struct Token
{
    /// <summary>
    /// Token 类型
    /// </summary>
    public TokenType Type { get; init; }
    
    /// <summary>
    /// Token 文本（原始字符串）
    /// </summary>
    public string Lexeme { get; init; }
    
    /// <summary>
    /// Token 值（解析后的值，可能是数字、字符串等）
    /// </summary>
    public object? Literal { get; init; }
    
    /// <summary>
    /// 行号（1-based）
    /// </summary>
    public int Line { get; init; }
    
    /// <summary>
    /// 列号（1-based）
    /// </summary>
    public int Column { get; init; }
    
    /// <summary>
    /// 在源文件中的起始位置
    /// </summary>
    public int Position { get; init; }
    
    /// <summary>
    /// 创建新Token
    /// </summary>
    public Token(TokenType type, string lexeme, object? literal, int line, int column, int position)
    {
        Type = type;
        Lexeme = lexeme;
        Literal = literal;
        Line = line;
        Column = column;
        Position = position;
    }
    
    /// <summary>
    /// 创建简单Token（无字面量）
    /// </summary>
    public Token(TokenType type, string lexeme, int line, int column, int position)
        : this(type, lexeme, null, line, column, position)
    {
    }
    
    /// <summary>
    /// 是否为关键字
    /// </summary>
    public bool IsKeyword => Type >= TokenType.PROTOCOL && Type <= TokenType.NULL;
    
    /// <summary>
    /// 是否为类型关键字
    /// </summary>
    public bool IsTypeKeyword => Type >= TokenType.UINT8 && Type <= TokenType.VOID;
    
    /// <summary>
    /// 是否为符号
    /// </summary>
    public bool IsSymbol => Type >= TokenType.LBRACE && Type <= TokenType.ARROW;
    
    /// <summary>
    /// 是否为字面量
    /// </summary>
    public bool IsLiteral => Type >= TokenType.IDENTIFIER && Type <= TokenType.CHAR_LITERAL;
    
    /// <summary>
    /// 是否为特殊Token
    /// </summary>
    public bool IsSpecial => Type == TokenType.EOF || Type == TokenType.INVALID;
    
    public override string ToString()
    {
        if (Literal != null)
        {
            return $"Token({Type}, '{Lexeme}', {Literal}, Line {Line}, Col {Column})";
        }
        return $"Token({Type}, '{Lexeme}', Line {Line}, Col {Column})";
    }
    
    /// <summary>
    /// 获取简短描述（用于错误消息）
    /// </summary>
    public string ToShortString()
    {
        if (Type == TokenType.IDENTIFIER || Type == TokenType.NUMBER || 
            Type == TokenType.FLOAT || Type == TokenType.STRING_LITERAL)
        {
            return $"'{Lexeme}'";
        }
        return Type.ToString();
    }
}
