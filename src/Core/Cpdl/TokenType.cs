namespace ComCross.Core.Cpdl;

/// <summary>
/// CPDL Token 类型
/// </summary>
public enum TokenType
{
    // ===== 关键字 (Keywords) =====
    /// <summary>协议定义</summary>
    PROTOCOL,
    
    /// <summary>消息定义</summary>
    MESSAGE,
    
    /// <summary>枚举定义</summary>
    ENUM,
    
    /// <summary>解析器定义</summary>
    PARSER,
    
    /// <summary>条件判断</summary>
    WHEN,
    
    /// <summary>导入</summary>
    IMPORT,
    
    /// <summary>别名</summary>
    AS,
    
    /// <summary>继承</summary>
    EXTENDS,
    
    /// <summary>可选</summary>
    OPTIONAL,
    
    /// <summary>校验</summary>
    VALIDATE,
    
    /// <summary>范围</summary>
    RANGE,
    
    /// <summary>字节序</summary>
    ENDIAN,
    
    /// <summary>长度</summary>
    LENGTH,
    
    /// <summary>编码</summary>
    ENCODING,
    
    /// <summary>格式</summary>
    FORMAT,
    
    /// <summary>显示</summary>
    DISPLAY,
    
    /// <summary>颜色</summary>
    COLOR,
    
    /// <summary>中断</summary>
    BREAK,
    
    /// <summary>继续</summary>
    CONTINUE,
    
    /// <summary>条件</summary>
    IF,
    
    /// <summary>否则</summary>
    ELSE,
    
    /// <summary>循环</summary>
    FOR,
    
    /// <summary>循环</summary>
    WHILE,
    
    /// <summary>返回</summary>
    RETURN,
    
    /// <summary>真</summary>
    TRUE,
    
    /// <summary>假</summary>
    FALSE,
    
    /// <summary>空</summary>
    NULL,
    
    // ===== 类型关键字 (Type Keywords) =====
    /// <summary>无符号8位整数</summary>
    UINT8,
    
    /// <summary>有符号8位整数</summary>
    INT8,
    
    /// <summary>无符号16位整数</summary>
    UINT16,
    
    /// <summary>有符号16位整数</summary>
    INT16,
    
    /// <summary>无符号32位整数</summary>
    UINT32,
    
    /// <summary>有符号32位整数</summary>
    INT32,
    
    /// <summary>无符号64位整数</summary>
    UINT64,
    
    /// <summary>有符号64位整数</summary>
    INT64,
    
    /// <summary>32位浮点数</summary>
    FLOAT32,
    
    /// <summary>64位浮点数</summary>
    FLOAT64,
    
    /// <summary>布尔</summary>
    BOOL,
    
    /// <summary>字符串</summary>
    STRING,
    
    /// <summary>字节数组</summary>
    BYTES,
    
    /// <summary>字符</summary>
    CHAR,
    
    /// <summary>无类型</summary>
    VOID,
    
    // ===== 符号 (Symbols) =====
    /// <summary>{</summary>
    LBRACE,
    
    /// <summary>}</summary>
    RBRACE,
    
    /// <summary>[</summary>
    LBRACKET,
    
    /// <summary>]</summary>
    RBRACKET,
    
    /// <summary>(</summary>
    LPAREN,
    
    /// <summary>)</summary>
    RPAREN,
    
    /// <summary>;</summary>
    SEMICOLON,
    
    /// <summary>,</summary>
    COMMA,
    
    /// <summary>.</summary>
    DOT,
    
    /// <summary>=</summary>
    ASSIGN,
    
    /// <summary>==</summary>
    EQUAL,
    
    /// <summary>!=</summary>
    NOT_EQUAL,
    
    /// <summary>&gt;</summary>
    GREATER,
    
    /// <summary>&lt;</summary>
    LESS,
    
    /// <summary>&gt;=</summary>
    GREATER_EQUAL,
    
    /// <summary>&lt;=</summary>
    LESS_EQUAL,
    
    /// <summary>+</summary>
    PLUS,
    
    /// <summary>-</summary>
    MINUS,
    
    /// <summary>*</summary>
    STAR,
    
    /// <summary>/</summary>
    SLASH,
    
    /// <summary>%</summary>
    PERCENT,
    
    /// <summary>&amp;</summary>
    AND,
    
    /// <summary>|</summary>
    OR,
    
    /// <summary>^</summary>
    XOR,
    
    /// <summary>~</summary>
    NOT,
    
    /// <summary>!</summary>
    BANG,
    
    /// <summary>&amp;&amp;</summary>
    AND_AND,
    
    /// <summary>||</summary>
    OR_OR,
    
    /// <summary>&lt;&lt;</summary>
    SHIFT_LEFT,
    
    /// <summary>&gt;&gt;</summary>
    SHIFT_RIGHT,
    
    /// <summary>?</summary>
    QUESTION,
    
    /// <summary>:</summary>
    COLON,
    
    /// <summary>-&gt;</summary>
    ARROW,
    
    // ===== 字面量 (Literals) =====
    /// <summary>标识符</summary>
    IDENTIFIER,
    
    /// <summary>整数字面量</summary>
    NUMBER,
    
    /// <summary>浮点数字面量</summary>
    FLOAT,
    
    /// <summary>字符串字面量</summary>
    STRING_LITERAL,
    
    /// <summary>字符字面量</summary>
    CHAR_LITERAL,
    
    // ===== 特殊 (Special) =====
    /// <summary>文件结束</summary>
    EOF,
    
    /// <summary>无效Token</summary>
    INVALID
}
