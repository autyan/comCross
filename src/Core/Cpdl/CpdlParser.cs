using ComCross.Core.Cpdl.Ast;

namespace ComCross.Core.Cpdl;

/// <summary>
/// CPDL递归下降解析器
/// 将Token流转换为抽象语法树（AST）
/// </summary>
public class CpdlParser
{
    private readonly List<Token> _tokens;
    private readonly List<string> _errors;
    private int _current = 0;
    
    public CpdlParser(List<Token> tokens)
    {
        _tokens = tokens;
        _errors = new List<string>();
    }
    
    /// <summary>
    /// 解析整个协议定义
    /// </summary>
    public ParserResult Parse()
    {
        try
        {
            var protocol = ParseProtocol();
            return new ParserResult(protocol, _errors);
        }
        catch (ParseException ex)
        {
            _errors.Add(ex.Message);
            return new ParserResult(null, _errors);
        }
    }
    
    #region 顶层解析
    
    /// <summary>
    /// 解析协议定义: protocol Name { ... }
    /// </summary>
    private ProtocolDefinition ParseProtocol()
    {
        var protocol = new ProtocolDefinition();
        
        // protocol关键字
        var protocolToken = Expect(TokenType.PROTOCOL, "Expected 'protocol' keyword");
        protocol.Line = protocolToken.Line;
        protocol.Column = protocolToken.Column;
        
        // 协议名称（错误恢复：如果缺少名称，使用占位符并继续）
        if (Check(TokenType.IDENTIFIER))
        {
            var nameToken = Advance();
            protocol.Name = nameToken.Lexeme;
        }
        else
        {
            AddError("Expected protocol name", Peek());
            protocol.Name = "<missing>";
        }
        
        // 左大括号
        Expect(TokenType.LBRACE, "Expected '{' after protocol name");
        
        // 解析协议体
        while (!Check(TokenType.RBRACE) && !IsAtEnd())
        {
            if (Match(TokenType.IDENTIFIER))
            {
                var identifier = Previous();
                
                if (identifier.Lexeme == "version")
                {
                    // version "1.0"
                    var versionToken = Expect(TokenType.STRING_LITERAL, "Expected version string");
                    protocol.Version = versionToken.Literal?.ToString();
                }
                else if (identifier.Lexeme == "description")
                {
                    // description "..."
                    var descToken = Expect(TokenType.STRING_LITERAL, "Expected description string");
                    protocol.Description = descToken.Literal?.ToString();
                }
                else
                {
                    AddError($"Unknown protocol-level identifier: {identifier.Lexeme}", identifier);
                }
            }
            else if (Match(TokenType.IMPORT))
            {
                protocol.Imports.Add(ParseImport());
            }
            else if (Match(TokenType.ENUM))
            {
                protocol.Enums.Add(ParseEnum());
            }
            else if (Match(TokenType.MESSAGE))
            {
                protocol.Messages.Add(ParseMessage());
            }
            else if (Match(TokenType.PARSER))
            {
                protocol.Parser = ParseParser();
            }
            else
            {
                AddError($"Unexpected token in protocol body: {Peek().Lexeme}", Peek());
                Advance(); // 跳过错误的token
            }
        }
        
        // 右大括号
        Expect(TokenType.RBRACE, "Expected '}' at end of protocol");
        
        return protocol;
    }
    
    /// <summary>
    /// 解析导入语句: import "file.cpdl" as Alias;
    /// </summary>
    private ImportStatement ParseImport()
    {
        var import = new ImportStatement
        {
            Line = Previous().Line,
            Column = Previous().Column
        };
        
        var fileToken = Expect(TokenType.STRING_LITERAL, "Expected file path after 'import'");
        import.FilePath = fileToken.Literal?.ToString() ?? "";
        
        if (Match(TokenType.AS))
        {
            var aliasToken = Expect(TokenType.IDENTIFIER, "Expected alias after 'as'");
            import.Alias = aliasToken.Lexeme;
        }
        
        Expect(TokenType.SEMICOLON, "Expected ';' after import statement");
        
        return import;
    }
    
    #endregion
    
    #region 枚举解析
    
    /// <summary>
    /// 解析枚举定义: enum Name { ... }
    /// </summary>
    private EnumDefinition ParseEnum()
    {
        var enumDef = new EnumDefinition
        {
            Line = Previous().Line,
            Column = Previous().Column
        };
        
        var nameToken = Expect(TokenType.IDENTIFIER, "Expected enum name");
        enumDef.Name = nameToken.Lexeme;
        
        // 可选的基础类型: enum Name : uint16 { ... }
        if (Match(TokenType.COLON))
        {
            var baseTypeToken = ExpectTypeKeyword("Expected base type for enum");
            enumDef.BaseType = baseTypeToken.Lexeme;
        }
        
        Expect(TokenType.LBRACE, "Expected '{' after enum name");
        
        // 解析枚举成员
        while (!Check(TokenType.RBRACE) && !IsAtEnd())
        {
            enumDef.Members.Add(ParseEnumMember());
            
            // 可选的逗号分隔符
            if (!Check(TokenType.RBRACE))
            {
                if (!Match(TokenType.COMMA))
                {
                    // 如果没有逗号，检查是否是右括号（允许最后一个成员后不加逗号）
                    if (!Check(TokenType.RBRACE))
                    {
                        AddError("Expected ',' or '}' after enum member", Peek());
                    }
                }
            }
        }
        
        Expect(TokenType.RBRACE, "Expected '}' at end of enum");
        
        return enumDef;
    }
    
    /// <summary>
    /// 解析枚举成员: NAME = value
    /// </summary>
    private EnumMember ParseEnumMember()
    {
        var member = new EnumMember();
        
        var nameToken = Expect(TokenType.IDENTIFIER, "Expected enum member name");
        member.Name = nameToken.Lexeme;
        member.Line = nameToken.Line;
        member.Column = nameToken.Column;
        
        Expect(TokenType.ASSIGN, "Expected '=' after enum member name");
        
        member.Value = ParseExpression();
        
        return member;
    }
    
    #endregion
    
    #region 消息解析
    
    /// <summary>
    /// 解析消息定义: message Name { ... }
    /// </summary>
    private MessageDefinition ParseMessage()
    {
        var message = new MessageDefinition
        {
            Line = Previous().Line,
            Column = Previous().Column
        };
        
        // 消息名称（错误恢复）
        if (Check(TokenType.IDENTIFIER))
        {
            var nameToken = Advance();
            message.Name = nameToken.Lexeme;
        }
        else
        {
            AddError("Expected message name", Peek());
            message.Name = "<missing>";
        }
        
        // 可选的继承: message Name extends BaseMessage { ... }
        if (Match(TokenType.EXTENDS))
        {
            if (Check(TokenType.IDENTIFIER))
            {
                var baseToken = Advance();
                message.BaseMessage = baseToken.Lexeme;
            }
            else
            {
                AddError("Expected base message name after 'extends'", Peek());
            }
        }
        
        Expect(TokenType.LBRACE, "Expected '{' after message name");
        
        // 解析字段
        while (!Check(TokenType.RBRACE) && !IsAtEnd())
        {
            try
            {
                message.Fields.Add(ParseField());
            }
            catch (ParseException ex)
            {
                AddError(ex.Message, Peek());
                // 错误恢复：跳到下一个可能的字段开始或消息结束
                while (!IsAtEnd() && !Check(TokenType.RBRACE) && 
                       !Check(TokenType.SEMICOLON) && !IsTypeKeyword())
                {
                    Advance();
                }
                if (Match(TokenType.SEMICOLON))
                {
                    // 跳过分号，继续下一个字段
                }
            }
        }
        
        Expect(TokenType.RBRACE, "Expected '}' at end of message");
        
        return message;
    }
    
    /// <summary>
    /// 检查当前token是否是类型关键字
    /// </summary>
    private bool IsTypeKeyword()
    {
        var token = Peek();
        return token.Type == TokenType.UINT8 || token.Type == TokenType.INT8 ||
               token.Type == TokenType.UINT16 || token.Type == TokenType.INT16 ||
               token.Type == TokenType.UINT32 || token.Type == TokenType.INT32 ||
               token.Type == TokenType.UINT64 || token.Type == TokenType.INT64 ||
               token.Type == TokenType.FLOAT32 || token.Type == TokenType.FLOAT64 ||
               token.Type == TokenType.BOOL || token.Type == TokenType.STRING ||
               token.Type == TokenType.BYTES || token.Type == TokenType.CHAR ||
               token.Type == TokenType.OPTIONAL;
    }
    
    /// <summary>
    /// 解析字段定义: [optional] type name [modifiers...];
    /// </summary>
    private FieldDefinition ParseField()
    {
        var field = new FieldDefinition();
        
        // 可选的optional关键字
        if (Match(TokenType.OPTIONAL))
        {
            field.IsOptional = true;
            field.Line = Previous().Line;
            field.Column = Previous().Column;
        }
        
        // 字段类型
        var typeToken = ExpectTypeKeyword("Expected field type");
        field.Type = typeToken.Lexeme;
        if (!field.IsOptional)
        {
            field.Line = typeToken.Line;
            field.Column = typeToken.Column;
        }
        
        // 字段名称
        var nameToken = Expect(TokenType.IDENTIFIER, "Expected field name");
        field.Name = nameToken.Lexeme;
        
        // 解析修饰符（只在确实是修饰符时解析）
        while (CheckModifier())
        {
            field.Modifiers.Add(ParseModifier());
        }
        
        Expect(TokenType.SEMICOLON, "Expected ';' after field definition");
        
        return field;
    }
    
    /// <summary>
    /// 检查是否是修饰符关键字
    /// </summary>
    private bool CheckModifier()
    {
        if (IsAtEnd()) return false;
        var token = Peek();
        
        // 修饰符可以是关键字或标识符
        return token.Type == TokenType.RANGE ||
               token.Type == TokenType.ENDIAN ||
               token.Type == TokenType.LENGTH ||
               token.Type == TokenType.VALIDATE ||
               token.Type == TokenType.ENCODING ||
               token.Type == TokenType.FORMAT ||
               token.Type == TokenType.DISPLAY ||
               token.Type == TokenType.COLOR;
    }
    
    /// <summary>
    /// 解析修饰符: name(args...)
    /// </summary>
    private FieldModifier ParseModifier()
    {
        var modifier = new FieldModifier();
        
        // 修饰符名称（可能是关键字或标识符）
        var token = Advance();
        modifier.Name = token.Lexeme;
        modifier.Line = token.Line;
        modifier.Column = token.Column;
        
        Expect(TokenType.LPAREN, $"Expected '(' after modifier '{modifier.Name}'");
        
        // 解析参数
        if (!Check(TokenType.RPAREN))
        {
            do
            {
                modifier.Arguments.Add(ParseExpression());
            }
            while (Match(TokenType.COMMA));
        }
        
        Expect(TokenType.RPAREN, "Expected ')' after modifier arguments");
        
        return modifier;
    }
    
    #endregion
    
    #region 解析器定义
    
    /// <summary>
    /// 解析parser定义: parser { when(...) { ... } }
    /// </summary>
    private ParserDefinition ParseParser()
    {
        var parser = new ParserDefinition
        {
            Line = Previous().Line,
            Column = Previous().Column
        };
        
        Expect(TokenType.LBRACE, "Expected '{' after 'parser'");
        
        // 解析when规则
        while (!Check(TokenType.RBRACE) && !IsAtEnd())
        {
            if (Match(TokenType.WHEN))
            {
                parser.Rules.Add(ParseWhenRule());
            }
            else
            {
                AddError($"Expected 'when' in parser body, got: {Peek().Lexeme}", Peek());
                Advance();
            }
        }
        
        Expect(TokenType.RBRACE, "Expected '}' at end of parser");
        
        return parser;
    }
    
    /// <summary>
    /// 解析when规则: when (condition) { actions... }
    /// </summary>
    private WhenRule ParseWhenRule()
    {
        var rule = new WhenRule
        {
            Line = Previous().Line,
            Column = Previous().Column
        };
        
        Expect(TokenType.LPAREN, "Expected '(' after 'when'");
        rule.Condition = ParseExpression();
        Expect(TokenType.RPAREN, "Expected ')' after when condition");
        
        Expect(TokenType.LBRACE, "Expected '{' after when condition");
        
        // 解析动作
        while (!Check(TokenType.RBRACE) && !IsAtEnd())
        {
            // Action可以是关键字(DISPLAY, COLOR, VALIDATE)或标识符
            if (Check(TokenType.IDENTIFIER) || Check(TokenType.DISPLAY) || 
                Check(TokenType.COLOR) || Check(TokenType.VALIDATE))
            {
                rule.Actions.Add(ParseAction());
            }
            else
            {
                AddError($"Expected action in when block, got: {Peek().Lexeme}", Peek());
                Advance();
            }
        }
        
        Expect(TokenType.RBRACE, "Expected '}' at end of when block");
        
        return rule;
    }
    
    /// <summary>
    /// 解析动作: display "...", color info
    /// </summary>
    private ParserAction ParseAction()
    {
        var action = new ParserAction();
        
        // Action类型可以是关键字(DISPLAY, COLOR)或标识符
        var token = Peek();
        if (token.Type == TokenType.DISPLAY || token.Type == TokenType.COLOR ||
            token.Type == TokenType.VALIDATE || token.Type == TokenType.IDENTIFIER)
        {
            Advance();
            action.Type = token.Lexeme;
            action.Line = token.Line;
            action.Column = token.Column;
        }
        else
        {
            throw new ParseException($"Expected action type, got: {token.Lexeme} at Line {token.Line}, Column {token.Column}");
        }
        
        // 如果有参数（某些动作可能不需要参数）
        if (!Check(TokenType.SEMICOLON))
        {
            // display需要字符串或表达式
            if (action.Type == "display" || action.Type == "validate")
            {
                action.Arguments.Add(ParseExpression());
            }
            // color需要标识符或字符串
            else if (action.Type == "color")
            {
                if (Check(TokenType.IDENTIFIER) || Check(TokenType.STRING_LITERAL))
                {
                    action.Arguments.Add(ParsePrimary());
                }
            }
        }
        
        Expect(TokenType.SEMICOLON, $"Expected ';' after {action.Type} action");
        
        return action;
    }
    
    #endregion
    
    #region 表达式解析（优先级从低到高）
    
    /// <summary>
    /// 解析表达式入口
    /// </summary>
    private Expression ParseExpression()
    {
        return ParseLogicalOr();
    }
    
    /// <summary>
    /// 解析逻辑或: expr || expr
    /// </summary>
    private Expression ParseLogicalOr()
    {
        var expr = ParseLogicalAnd();
        
        while (Match(TokenType.OR_OR))
        {
            var op = Previous().Lexeme;
            var right = ParseLogicalAnd();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析逻辑与: expr && expr
    /// </summary>
    private Expression ParseLogicalAnd()
    {
        var expr = ParseEquality();
        
        while (Match(TokenType.AND_AND))
        {
            var op = Previous().Lexeme;
            var right = ParseEquality();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析相等性: expr == expr, expr != expr
    /// </summary>
    private Expression ParseEquality()
    {
        var expr = ParseComparison();
        
        while (Match(TokenType.EQUAL, TokenType.NOT_EQUAL))
        {
            var op = Previous().Lexeme;
            var right = ParseComparison();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析比较: expr > expr, expr >= expr, etc.
    /// </summary>
    private Expression ParseComparison()
    {
        var expr = ParseBitwise();
        
        while (Match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL))
        {
            var op = Previous().Lexeme;
            var right = ParseBitwise();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析位运算: expr & expr, expr | expr, expr ^ expr
    /// </summary>
    private Expression ParseBitwise()
    {
        var expr = ParseShift();
        
        while (Match(TokenType.AND, TokenType.OR, TokenType.XOR))
        {
            var op = Previous().Lexeme;
            var right = ParseShift();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析移位: expr << expr, expr >> expr
    /// </summary>
    private Expression ParseShift()
    {
        var expr = ParseAdditive();
        
        while (Match(TokenType.SHIFT_LEFT, TokenType.SHIFT_RIGHT))
        {
            var op = Previous().Lexeme;
            var right = ParseAdditive();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析加减: expr + expr, expr - expr
    /// </summary>
    private Expression ParseAdditive()
    {
        var expr = ParseMultiplicative();
        
        while (Match(TokenType.PLUS, TokenType.MINUS))
        {
            var op = Previous().Lexeme;
            var right = ParseMultiplicative();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析乘除模: expr * expr, expr / expr, expr % expr
    /// </summary>
    private Expression ParseMultiplicative()
    {
        var expr = ParseUnary();
        
        while (Match(TokenType.STAR, TokenType.SLASH, TokenType.PERCENT))
        {
            var op = Previous().Lexeme;
            var right = ParseUnary();
            expr = new BinaryExpression
            {
                Left = expr,
                Operator = op,
                Right = right,
                Line = expr.Line,
                Column = expr.Column
            };
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析一元运算: -expr, !expr, ~expr
    /// </summary>
    private Expression ParseUnary()
    {
        if (Match(TokenType.MINUS, TokenType.BANG, TokenType.NOT))
        {
            var op = Previous().Lexeme;
            var operand = ParseUnary();
            return new UnaryExpression
            {
                Operator = op,
                Operand = operand,
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        return ParsePostfix();
    }
    
    /// <summary>
    /// 解析后缀运算: expr.member, expr[index], expr(args)
    /// </summary>
    private Expression ParsePostfix()
    {
        var expr = ParsePrimary();
        
        while (true)
        {
            if (Check(TokenType.DOT) && PeekNext().Type != TokenType.DOT)
            {
                // 成员访问（但不是切片的..）
                Advance(); // 消费DOT
                var memberToken = Expect(TokenType.IDENTIFIER, "Expected member name after '.'");
                expr = new MemberAccessExpression
                {
                    Object = expr,
                    MemberName = memberToken.Lexeme,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            else if (Match(TokenType.LBRACKET))
            {
                // 索引访问
                var index = ParseExpression();
                
                // 检查是否是切片 [start..end]
                Expression? endIndex = null;
                if (Match(TokenType.DOT))
                {
                    if (Match(TokenType.DOT))
                    {
                        // [start..end]
                        endIndex = ParseExpression();
                    }
                    else
                    {
                        AddError("Expected second '.' for slice notation", Peek());
                    }
                }
                
                Expect(TokenType.RBRACKET, "Expected ']' after index");
                
                expr = new IndexExpression
                {
                    Object = expr,
                    Index = index,
                    EndIndex = endIndex,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            else if (Match(TokenType.LPAREN))
            {
                // 函数调用（如果expr是标识符）
                if (expr is IdentifierExpression idExpr)
                {
                    var args = new List<Expression>();
                    if (!Check(TokenType.RPAREN))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        }
                        while (Match(TokenType.COMMA));
                    }
                    
                    Expect(TokenType.RPAREN, "Expected ')' after function arguments");
                    
                    expr = new CallExpression
                    {
                        FunctionName = idExpr.Name,
                        Arguments = args,
                        Line = idExpr.Line,
                        Column = idExpr.Column
                    };
                }
                else
                {
                    AddError("Only identifiers can be called as functions", Peek());
                    // 跳过参数
                    while (!Check(TokenType.RPAREN) && !IsAtEnd())
                        Advance();
                    if (!IsAtEnd()) Advance(); // 跳过 ')'
                }
            }
            else
            {
                break;
            }
        }
        
        return expr;
    }
    
    /// <summary>
    /// 解析基础表达式: 字面量, 标识符, (expr)
    /// </summary>
    private Expression ParsePrimary()
    {
        var token = Peek();
        
        // 数字字面量
        if (Match(TokenType.NUMBER))
        {
            return new LiteralExpression
            {
                Value = Previous().Literal,
                LiteralType = "number",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // 浮点数字面量
        if (Match(TokenType.FLOAT))
        {
            return new LiteralExpression
            {
                Value = Previous().Literal,
                LiteralType = "float",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // 字符串字面量
        if (Match(TokenType.STRING_LITERAL))
        {
            return new LiteralExpression
            {
                Value = Previous().Literal,
                LiteralType = "string",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // 字符字面量
        if (Match(TokenType.CHAR_LITERAL))
        {
            return new LiteralExpression
            {
                Value = Previous().Literal,
                LiteralType = "char",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // 布尔字面量
        if (Match(TokenType.TRUE))
        {
            return new LiteralExpression
            {
                Value = true,
                LiteralType = "bool",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        if (Match(TokenType.FALSE))
        {
            return new LiteralExpression
            {
                Value = false,
                LiteralType = "bool",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // null字面量
        if (Match(TokenType.NULL))
        {
            return new LiteralExpression
            {
                Value = null,
                LiteralType = "null",
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // 标识符（某些关键字在表达式中可作为标识符）
        if (Match(TokenType.IDENTIFIER) || IsIdentifierLikeKeyword())
        {
            return new IdentifierExpression
            {
                Name = Previous().Lexeme,
                Line = Previous().Line,
                Column = Previous().Column
            };
        }
        
        // 括号表达式
        if (Match(TokenType.LPAREN))
        {
            var expr = ParseExpression();
            Expect(TokenType.RPAREN, "Expected ')' after expression");
            return expr;
        }
        
        throw new ParseException($"Unexpected token in expression: {token.Lexeme} at Line {token.Line}, Column {token.Column}");
    }
    
    #endregion
    
    #region 辅助方法
    
    /// <summary>
    /// 期望特定类型的token
    /// </summary>
    private Token Expect(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }
        
        var current = Peek();
        throw new ParseException($"{message}. Got '{current.Lexeme}' at Line {current.Line}, Column {current.Column}");
    }
    
    /// <summary>
    /// 期望类型关键字
    /// </summary>
    private Token ExpectTypeKeyword(string message)
    {
        var token = Peek();
        if (token.IsTypeKeyword)
        {
            return Advance();
        }
        
        throw new ParseException($"{message}. Got '{token.Lexeme}' at Line {token.Line}, Column {token.Column}");
    }
    
    /// <summary>
    /// 匹配任一token类型
    /// </summary>
    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// 检查当前token类型
    /// </summary>
    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }
    
    /// <summary>
    /// 前进一个token
    /// </summary>
    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }
    
    /// <summary>
    /// 是否到达结尾
    /// </summary>
    private bool IsAtEnd()
    {
        return Peek().Type == TokenType.EOF;
    }
    
    /// <summary>
    /// 查看当前token
    /// </summary>
    private Token Peek()
    {
        return _tokens[_current];
    }
    
    /// <summary>
    /// 查看下一个token
    /// </summary>
    private Token PeekNext()
    {
        if (_current + 1 >= _tokens.Count)
            return _tokens[_tokens.Count - 1]; // 返回EOF
        return _tokens[_current + 1];
    }
    
    /// <summary>
    /// 查看前一个token
    /// </summary>
    /// <summary>
    /// 检查当前token是否是可以作为标识符使用的关键字，如果是则消费它
    /// 在表达式中，某些结构性关键字（message, enum, parser等）可以作为标识符
    /// </summary>
    private bool IsIdentifierLikeKeyword()
    {
        var token = Peek();
        var isKeywordId = token.Type == TokenType.MESSAGE ||
                          token.Type == TokenType.ENUM ||
                          token.Type == TokenType.PARSER ||
                          token.Type == TokenType.PROTOCOL ||
                          token.Type == TokenType.VALIDATE ||
                          token.Type == TokenType.RANGE ||
                          token.Type == TokenType.ENDIAN ||
                          token.Type == TokenType.LENGTH ||
                          token.Type == TokenType.ENCODING ||
                          token.Type == TokenType.FORMAT ||
                          token.Type == TokenType.DISPLAY ||
                          token.Type == TokenType.COLOR;
        
        if (isKeywordId)
        {
            Advance(); // 消费token
            return true;
        }
        
        return false;
    }
    
    private Token Previous()
    {
        return _tokens[_current - 1];
    }
    
    /// <summary>
    /// 添加错误
    /// </summary>
    private void AddError(string message, Token token)
    {
        _errors.Add($"{message} at Line {token.Line}, Column {token.Column}");
    }
    
    #endregion
}

/// <summary>
/// 解析结果
/// </summary>
public class ParserResult
{
    public ProtocolDefinition? Protocol { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool Success => Errors.Count == 0 && Protocol != null;
    
    public ParserResult(ProtocolDefinition? protocol, List<string> errors)
    {
        Protocol = protocol;
        Errors = errors;
    }
}

/// <summary>
/// 解析异常
/// </summary>
public class ParseException : Exception
{
    public ParseException(string message) : base(message)
    {
    }
}
