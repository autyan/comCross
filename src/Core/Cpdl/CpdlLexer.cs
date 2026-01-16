using System.Text;

namespace ComCross.Core.Cpdl;

/// <summary>
/// CPDL 词法分析器（Lexer/Scanner）
/// 将源代码文本转换为 Token 流
/// </summary>
public sealed class CpdlLexer
{
    private readonly string _source;
    private readonly List<Token> _tokens = new();
    private readonly List<string> _errors = new();
    
    private int _start = 0;        // 当前Token的起始位置
    private int _current = 0;      // 当前扫描位置
    private int _line = 1;         // 当前行号
    private int _column = 1;       // 当前列号
    private int _lineStart = 0;    // 当前行的起始位置
    
    // 关键字映射表
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        // 基本关键字
        ["protocol"] = TokenType.PROTOCOL,
        ["message"] = TokenType.MESSAGE,
        ["enum"] = TokenType.ENUM,
        ["parser"] = TokenType.PARSER,
        ["when"] = TokenType.WHEN,
        ["import"] = TokenType.IMPORT,
        ["as"] = TokenType.AS,
        ["extends"] = TokenType.EXTENDS,
        ["optional"] = TokenType.OPTIONAL,
        
        // 修饰符关键字
        ["validate"] = TokenType.VALIDATE,
        ["range"] = TokenType.RANGE,
        ["endian"] = TokenType.ENDIAN,
        ["length"] = TokenType.LENGTH,
        ["encoding"] = TokenType.ENCODING,
        ["format"] = TokenType.FORMAT,
        ["display"] = TokenType.DISPLAY,
        ["color"] = TokenType.COLOR,
        
        // 控制流关键字
        ["break"] = TokenType.BREAK,
        ["continue"] = TokenType.CONTINUE,
        ["if"] = TokenType.IF,
        ["else"] = TokenType.ELSE,
        ["for"] = TokenType.FOR,
        ["while"] = TokenType.WHILE,
        ["return"] = TokenType.RETURN,
        
        // 字面量关键字
        ["true"] = TokenType.TRUE,
        ["false"] = TokenType.FALSE,
        ["null"] = TokenType.NULL,
        
        // 类型关键字
        ["uint8"] = TokenType.UINT8,
        ["int8"] = TokenType.INT8,
        ["uint16"] = TokenType.UINT16,
        ["int16"] = TokenType.INT16,
        ["uint32"] = TokenType.UINT32,
        ["int32"] = TokenType.INT32,
        ["uint64"] = TokenType.UINT64,
        ["int64"] = TokenType.INT64,
        ["float32"] = TokenType.FLOAT32,
        ["float64"] = TokenType.FLOAT64,
        ["bool"] = TokenType.BOOL,
        ["string"] = TokenType.STRING,
        ["bytes"] = TokenType.BYTES,
        ["char"] = TokenType.CHAR,
        ["void"] = TokenType.VOID,
    };
    
    public CpdlLexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }
    
    /// <summary>
    /// 扫描所有Token
    /// </summary>
    public LexerResult ScanTokens()
    {
        while (!IsAtEnd())
        {
            _start = _current;
            ScanToken();
        }
        
        // 添加EOF Token
        _tokens.Add(new Token(TokenType.EOF, string.Empty, _line, _column, _current));
        
        return new LexerResult(_tokens, _errors);
    }
    
    /// <summary>
    /// 扫描单个Token
    /// </summary>
    private void ScanToken()
    {
        char c = Advance();
        
        switch (c)
        {
            // 单字符Token
            case '{': AddToken(TokenType.LBRACE); break;
            case '}': AddToken(TokenType.RBRACE); break;
            case '[': AddToken(TokenType.LBRACKET); break;
            case ']': AddToken(TokenType.RBRACKET); break;
            case '(': AddToken(TokenType.LPAREN); break;
            case ')': AddToken(TokenType.RPAREN); break;
            case ';': AddToken(TokenType.SEMICOLON); break;
            case ',': AddToken(TokenType.COMMA); break;
            case '.': AddToken(TokenType.DOT); break;
            case '+': AddToken(TokenType.PLUS); break;
            case '%': AddToken(TokenType.PERCENT); break;
            case '^': AddToken(TokenType.XOR); break;
            case '~': AddToken(TokenType.NOT); break;
            case '?': AddToken(TokenType.QUESTION); break;
            case ':': AddToken(TokenType.COLON); break;
            
            // 可能双字符Token
            case '-':
                AddToken(Match('>') ? TokenType.ARROW : TokenType.MINUS);
                break;
            case '*':
                AddToken(TokenType.STAR);
                break;
            case '!':
                AddToken(Match('=') ? TokenType.NOT_EQUAL : TokenType.BANG);
                break;
            case '=':
                AddToken(Match('=') ? TokenType.EQUAL : TokenType.ASSIGN);
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LESS_EQUAL :
                        Match('<') ? TokenType.SHIFT_LEFT : TokenType.LESS);
                break;
            case '>':
                AddToken(Match('=') ? TokenType.GREATER_EQUAL :
                        Match('>') ? TokenType.SHIFT_RIGHT : TokenType.GREATER);
                break;
            case '&':
                AddToken(Match('&') ? TokenType.AND_AND : TokenType.AND);
                break;
            case '|':
                AddToken(Match('|') ? TokenType.OR_OR : TokenType.OR);
                break;
            
            // 注释和除号
            case '/':
                if (Match('/'))
                {
                    // 单行注释：跳到行尾
                    while (Peek() != '\n' && !IsAtEnd())
                        Advance();
                }
                else if (Match('*'))
                {
                    // 多行注释
                    ScanMultiLineComment();
                }
                else
                {
                    AddToken(TokenType.SLASH);
                }
                break;
            
            // 空白字符
            case ' ':
            case '\r':
            case '\t':
                // 忽略空白
                break;
            
            case '\n':
                _line++;
                _lineStart = _current;
                _column = 1;
                break;
            
            // 字符串字面量
            case '"':
                ScanString();
                break;
            
            // 字符字面量
            case '\'':
                ScanChar();
                break;
            
            default:
                if (IsDigit(c))
                {
                    ScanNumber();
                }
                else if (IsAlpha(c))
                {
                    ScanIdentifier();
                }
                else
                {
                    AddError($"意外的字符: '{c}'");
                }
                break;
        }
    }
    
    /// <summary>
    /// 扫描多行注释
    /// </summary>
    private void ScanMultiLineComment()
    {
        int nestLevel = 1; // 支持嵌套注释
        
        while (!IsAtEnd() && nestLevel > 0)
        {
            if (Peek() == '/' && PeekNext() == '*')
            {
                Advance(); // 跳过 '/'
                Advance(); // 跳过 '*'
                nestLevel++;
            }
            else if (Peek() == '*' && PeekNext() == '/')
            {
                Advance(); // 跳过 '*'
                Advance(); // 跳过 '/'
                nestLevel--;
            }
            else if (Peek() == '\n')
            {
                _line++;
                _lineStart = _current + 1;
                _column = 1;
                Advance();
            }
            else
            {
                Advance();
            }
        }
        
        if (nestLevel > 0)
        {
            AddError("未闭合的多行注释");
        }
    }
    
    /// <summary>
    /// 扫描字符串字面量
    /// </summary>
    private void ScanString()
    {
        var sb = new StringBuilder();
        
        while (Peek() != '"' && !IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _lineStart = _current + 1;
                _column = 1;
            }
            
            if (Peek() == '\\' && PeekNext() != '\0')
            {
                Advance(); // 跳过 '\'
                char escaped = Advance();
                
                // 处理转义字符
                sb.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    '0' => '\0',
                    _ => escaped // 不识别的转义字符保持原样
                });
            }
            else
            {
                sb.Append(Advance());
            }
        }
        
        if (IsAtEnd())
        {
            AddError("未闭合的字符串");
            return;
        }
        
        // 跳过结束的 "
        Advance();
        
        AddToken(TokenType.STRING_LITERAL, sb.ToString());
    }
    
    /// <summary>
    /// 扫描字符字面量
    /// </summary>
    private void ScanChar()
    {
        if (IsAtEnd())
        {
            AddError("未闭合的字符字面量");
            return;
        }
        
        char value;
        
        if (Peek() == '\\')
        {
            Advance(); // 跳过 '\'
            char escaped = Advance();
            
            value = escaped switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '\'' => '\'',
                '0' => '\0',
                _ => escaped
            };
        }
        else
        {
            value = Advance();
        }
        
        if (Peek() != '\'')
        {
            AddError("字符字面量必须用单引号闭合");
            return;
        }
        
        Advance(); // 跳过结束的 '
        
        AddToken(TokenType.CHAR_LITERAL, value);
    }
    
    /// <summary>
    /// 扫描数字字面量（支持十进制、十六进制、二进制、八进制、浮点数）
    /// </summary>
    private void ScanNumber()
    {
        // 检查进制前缀
        if (_source[_start] == '0' && _current < _source.Length)
        {
            char next = _source[_current];
            
            if (next == 'x' || next == 'X')
            {
                // 十六进制
                Advance(); // 跳过 'x'
                while (IsHexDigit(Peek()))
                    Advance();
                
                string hexStr = _source.Substring(_start + 2, _current - _start - 2);
                if (hexStr.Length == 0)
                {
                    AddError("无效的十六进制数字");
                    return;
                }
                
                long value = Convert.ToInt64(hexStr, 16);
                AddToken(TokenType.NUMBER, value);
                return;
            }
            else if (next == 'b' || next == 'B')
            {
                // 二进制
                Advance(); // 跳过 'b'
                while (Peek() == '0' || Peek() == '1')
                    Advance();
                
                string binStr = _source.Substring(_start + 2, _current - _start - 2);
                if (binStr.Length == 0)
                {
                    AddError("无效的二进制数字");
                    return;
                }
                
                long value = Convert.ToInt64(binStr, 2);
                AddToken(TokenType.NUMBER, value);
                return;
            }
            else if (next == 'o' || next == 'O')
            {
                // 八进制
                Advance(); // 跳过 'o'
                while (Peek() >= '0' && Peek() <= '7')
                    Advance();
                
                string octStr = _source.Substring(_start + 2, _current - _start - 2);
                if (octStr.Length == 0)
                {
                    AddError("无效的八进制数字");
                    return;
                }
                
                long value = Convert.ToInt64(octStr, 8);
                AddToken(TokenType.NUMBER, value);
                return;
            }
        }
        
        // 十进制整数或浮点数
        while (IsDigit(Peek()))
            Advance();
        
        // 检查小数点
        if (Peek() == '.' && IsDigit(PeekNext()))
        {
            Advance(); // 跳过 '.'
            while (IsDigit(Peek()))
                Advance();
            
            // 检查科学计数法
            if (Peek() == 'e' || Peek() == 'E')
            {
                Advance(); // 跳过 'e'
                if (Peek() == '+' || Peek() == '-')
                    Advance();
                
                while (IsDigit(Peek()))
                    Advance();
            }
            
            string floatStr = _source.Substring(_start, _current - _start);
            double floatValue = double.Parse(floatStr);
            AddToken(TokenType.FLOAT, floatValue);
        }
        else
        {
            string intStr = _source.Substring(_start, _current - _start);
            long intValue = long.Parse(intStr);
            AddToken(TokenType.NUMBER, intValue);
        }
    }
    
    /// <summary>
    /// 扫描标识符或关键字
    /// </summary>
    private void ScanIdentifier()
    {
        while (IsAlphaNumeric(Peek()))
            Advance();
        
        string text = _source.Substring(_start, _current - _start);
        
        // 检查是否为关键字
        if (Keywords.TryGetValue(text, out TokenType type))
        {
            AddToken(type);
        }
        else
        {
            AddToken(TokenType.IDENTIFIER);
        }
    }
    
    // ===== 辅助方法 =====
    
    private char Advance()
    {
        _column++;
        return _source[_current++];
    }
    
    private bool Match(char expected)
    {
        if (IsAtEnd()) return false;
        if (_source[_current] != expected) return false;
        
        _current++;
        _column++;
        return true;
    }
    
    private char Peek()
    {
        if (IsAtEnd()) return '\0';
        return _source[_current];
    }
    
    private char PeekNext()
    {
        if (_current + 1 >= _source.Length) return '\0';
        return _source[_current + 1];
    }
    
    private bool IsAtEnd() => _current >= _source.Length;
    
    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    
    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    
    private static bool IsAlpha(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    
    private static bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);
    
    private void AddToken(TokenType type)
    {
        string text = _source.Substring(_start, _current - _start);
        int column = _column - (_current - _start);
        _tokens.Add(new Token(type, text, _line, column, _start));
    }
    
    private void AddToken(TokenType type, object literal)
    {
        string text = _source.Substring(_start, _current - _start);
        int column = _column - (_current - _start);
        _tokens.Add(new Token(type, text, literal, _line, column, _start));
    }
    
    private void AddError(string message)
    {
        _errors.Add($"[Line {_line}, Col {_column}] 词法错误: {message}");
        
        // 添加INVALID Token以便继续解析
        _tokens.Add(new Token(TokenType.INVALID, 
            _source.Substring(_start, _current - _start), 
            _line, _column - (_current - _start), _start));
    }
}

/// <summary>
/// 词法分析结果
/// </summary>
public sealed class LexerResult
{
    /// <summary>
    /// Token列表
    /// </summary>
    public IReadOnlyList<Token> Tokens { get; }
    
    /// <summary>
    /// 错误列表
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
    
    /// <summary>
    /// 是否成功（无错误）
    /// </summary>
    public bool Success => Errors.Count == 0;
    
    public LexerResult(List<Token> tokens, List<string> errors)
    {
        Tokens = tokens;
        Errors = errors;
    }
}
