using ComCross.Core.Cpdl.Ast;

namespace ComCross.Core.Cpdl.Compiler;

/// <summary>
/// 语义分析器 - 对AST进行语义检查
/// 1. 类型检查
/// 2. 字段引用验证
/// 3. 枚举值范围检查
/// 4. 表达式类型推断
/// </summary>
public sealed class SemanticAnalyzer
{
    private readonly List<SemanticError> _errors = new();
    private readonly Dictionary<string, MessageDefinition> _messages = new();
    private readonly Dictionary<string, EnumDefinition> _enums = new();
    private MessageDefinition? _currentMessage;

    /// <summary>
    /// 分析协议定义
    /// </summary>
    public SemanticAnalysisResult Analyze(ProtocolDefinition protocol)
    {
        _errors.Clear();
        _messages.Clear();
        _enums.Clear();
        _currentMessage = null;

        // 第一遍：收集所有消息和枚举定义
        foreach (var enumDef in protocol.Enums)
        {
            if (_enums.ContainsKey(enumDef.Name))
            {
                AddError($"Duplicate enum definition: {enumDef.Name}", enumDef.Line, enumDef.Column);
            }
            else
            {
                _enums[enumDef.Name] = enumDef;
            }
        }

        foreach (var message in protocol.Messages)
        {
            if (_messages.ContainsKey(message.Name))
            {
                AddError($"Duplicate message definition: {message.Name}", message.Line, message.Column);
            }
            else
            {
                _messages[message.Name] = message;
            }
        }

        // 第二遍：验证枚举
        foreach (var enumDef in protocol.Enums)
        {
            AnalyzeEnum(enumDef);
        }

        // 第三遍：验证消息
        foreach (var message in protocol.Messages)
        {
            AnalyzeMessage(message);
        }

        // 第四遍：验证解析器规则
        if (protocol.Parser != null)
        {
            AnalyzeParser(protocol.Parser);
        }

        return new SemanticAnalysisResult(protocol, _errors);
    }

    /// <summary>
    /// 分析枚举定义
    /// </summary>
    private void AnalyzeEnum(EnumDefinition enumDef)
    {
        var memberNames = new HashSet<string>();

        foreach (var member in enumDef.Members)
        {
            if (memberNames.Contains(member.Name))
            {
                AddError($"Duplicate enum member: {member.Name}", member.Line, member.Column);
            }
            else
            {
                memberNames.Add(member.Name);
            }

            // 验证值表达式
            if (member.Value != null)
            {
                var valueType = InferExpressionType(member.Value);
                if (valueType != "number" && valueType != "unknown")
                {
                    AddError($"Enum value must be a number, got: {valueType}", member.Line, member.Column);
                }
            }
        }
    }

    /// <summary>
    /// 分析消息定义
    /// </summary>
    private void AnalyzeMessage(MessageDefinition message)
    {
        _currentMessage = message;

        // 验证继承
        if (message.BaseMessage != null)
        {
            if (!_messages.ContainsKey(message.BaseMessage))
            {
                AddError($"Base message not found: {message.BaseMessage}", message.Line, message.Column);
            }
            else if (message.BaseMessage == message.Name)
            {
                AddError($"Message cannot inherit from itself: {message.Name}", message.Line, message.Column);
            }
        }

        // 验证字段
        var fieldNames = new HashSet<string>();
        foreach (var field in message.Fields)
        {
            if (fieldNames.Contains(field.Name))
            {
                AddError($"Duplicate field name: {field.Name}", field.Line, field.Column);
            }
            else
            {
                fieldNames.Add(field.Name);
            }

            AnalyzeField(field);
        }

        _currentMessage = null;
    }

    /// <summary>
    /// 分析字段定义
    /// </summary>
    private void AnalyzeField(FieldDefinition field)
    {
        // 验证类型
        if (!IsValidType(field.Type))
        {
            AddError($"Invalid field type: {field.Type}", field.Line, field.Column);
        }

        // 验证修饰符
        foreach (var modifier in field.Modifiers)
        {
            AnalyzeModifier(modifier, field);
        }
    }

    /// <summary>
    /// 分析修饰符
    /// </summary>
    private void AnalyzeModifier(FieldModifier modifier, FieldDefinition field)
    {
        switch (modifier.Name.ToLowerInvariant())
        {
            case "range":
                if (modifier.Arguments.Count != 2)
                {
                    AddError($"range() requires 2 arguments, got {modifier.Arguments.Count}", 
                        modifier.Line, modifier.Column);
                }
                else
                {
                    // 验证范围参数是数字
                    var minType = InferExpressionType(modifier.Arguments[0]);
                    var maxType = InferExpressionType(modifier.Arguments[1]);
                    if (minType != "number" && minType != "unknown")
                    {
                        AddError($"range() min must be a number, got: {minType}", 
                            modifier.Line, modifier.Column);
                    }
                    if (maxType != "number" && maxType != "unknown")
                    {
                        AddError($"range() max must be a number, got: {maxType}", 
                            modifier.Line, modifier.Column);
                    }
                }
                break;

            case "endian":
                if (modifier.Arguments.Count != 1)
                {
                    AddError($"endian() requires 1 argument, got {modifier.Arguments.Count}", 
                        modifier.Line, modifier.Column);
                }
                else
                {
                    // 验证字节序参数
                    if (modifier.Arguments[0] is not IdentifierExpression idExpr ||
                        (idExpr.Name != "big" && idExpr.Name != "little"))
                    {
                        AddError("endian() argument must be 'big' or 'little'", 
                            modifier.Line, modifier.Column);
                    }
                }
                break;

            case "validate":
                if (modifier.Arguments.Count == 0)
                {
                    AddError("validate() requires at least 1 argument", 
                        modifier.Line, modifier.Column);
                }
                else
                {
                    // 验证表达式
                    foreach (var arg in modifier.Arguments)
                    {
                        InferExpressionType(arg);
                    }
                }
                break;

            case "length":
            case "encoding":
            case "format":
                // 这些修饰符参数类型较灵活，只验证有参数
                if (modifier.Arguments.Count == 0)
                {
                    AddError($"{modifier.Name}() requires at least 1 argument", 
                        modifier.Line, modifier.Column);
                }
                break;

            default:
                AddError($"Unknown modifier: {modifier.Name}", modifier.Line, modifier.Column);
                break;
        }
    }

    /// <summary>
    /// 分析解析器定义
    /// </summary>
    private void AnalyzeParser(ParserDefinition parser)
    {
        foreach (var rule in parser.Rules)
        {
            // 验证条件表达式
            var condType = InferExpressionType(rule.Condition);
            if (condType != "bool" && condType != "unknown")
            {
                AddError($"when condition must be boolean, got: {condType}", 
                    rule.Condition.Line, rule.Condition.Column);
            }

            // 验证动作
            foreach (var action in rule.Actions)
            {
                AnalyzeAction(action);
            }
        }
    }

    /// <summary>
    /// 分析动作
    /// </summary>
    private void AnalyzeAction(ParserAction action)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "display":
                if (action.Arguments.Count != 1)
                {
                    AddError($"display requires 1 argument, got {action.Arguments.Count}", 
                        action.Line, action.Column);
                }
                break;

            case "color":
                if (action.Arguments.Count != 1)
                {
                    AddError($"color requires 1 argument, got {action.Arguments.Count}", 
                        action.Line, action.Column);
                }
                break;

            case "validate":
                // validate可以有多个参数
                break;

            default:
                // 未知动作类型，记录警告但不报错（可能是扩展动作）
                break;
        }
    }

    /// <summary>
    /// 推断表达式类型
    /// </summary>
    private string InferExpressionType(Expression expr)
    {
        switch (expr)
        {
            case LiteralExpression lit:
                return lit.LiteralType;

            case IdentifierExpression id:
                return InferIdentifierType(id.Name);

            case BinaryExpression bin:
                return InferBinaryExpressionType(bin);

            case UnaryExpression un:
                return InferUnaryExpressionType(un);

            case MemberAccessExpression mem:
                return InferMemberAccessType(mem);

            case IndexExpression idx:
                return "unknown"; // 索引结果类型难以静态推断

            case CallExpression call:
                return InferCallExpressionType(call);

            default:
                return "unknown";
        }
    }

    /// <summary>
    /// 推断标识符类型
    /// </summary>
    private string InferIdentifierType(string name)
    {
        // 检查是否是当前消息的字段
        if (_currentMessage != null)
        {
            var field = _currentMessage.Fields.FirstOrDefault(f => f.Name == name);
            if (field != null)
            {
                return MapFieldTypeToExprType(field.Type);
            }
        }

        // 检查是否是枚举
        if (_enums.ContainsKey(name))
        {
            return "number"; // 枚举类型视为数字
        }

        return "unknown";
    }

    /// <summary>
    /// 推断二元表达式类型
    /// </summary>
    private string InferBinaryExpressionType(BinaryExpression expr)
    {
        var leftType = InferExpressionType(expr.Left);
        var rightType = InferExpressionType(expr.Right);

        // 逻辑运算符
        if (expr.Operator is "&&" or "||")
        {
            return "bool";
        }

        // 比较运算符
        if (expr.Operator is "==" or "!=" or ">" or "<" or ">=" or "<=")
        {
            return "bool";
        }

        // 算术运算符
        if (expr.Operator is "+" or "-" or "*" or "/" or "%")
        {
            if (leftType == "number" || rightType == "number")
                return "number";
            return "unknown";
        }

        // 位运算符
        if (expr.Operator is "&" or "|" or "^" or "<<" or ">>")
        {
            return "number";
        }

        return "unknown";
    }

    /// <summary>
    /// 推断一元表达式类型
    /// </summary>
    private string InferUnaryExpressionType(UnaryExpression expr)
    {
        var operandType = InferExpressionType(expr.Operand);

        if (expr.Operator is "!" or "not")
        {
            return "bool";
        }

        if (expr.Operator is "-" or "~")
        {
            return "number";
        }

        return operandType;
    }

    /// <summary>
    /// 推断成员访问类型
    /// </summary>
    private string InferMemberAccessType(MemberAccessExpression expr)
    {
        // 简化处理：查找对象是否是已知的消息类型
        if (expr.Object is IdentifierExpression idExpr)
        {
            if (_messages.TryGetValue(idExpr.Name, out var message))
            {
                var field = message.Fields.FirstOrDefault(f => f.Name == expr.MemberName);
                if (field != null)
                {
                    return MapFieldTypeToExprType(field.Type);
                }
            }
        }

        return "unknown";
    }

    /// <summary>
    /// 推断函数调用类型
    /// </summary>
    private string InferCallExpressionType(CallExpression expr)
    {
        // 内置函数类型推断
        switch (expr.FunctionName.ToLowerInvariant())
        {
            case "crc16":
            case "crc32":
            case "checksum":
            case "length":
                return "number";

            case "ascii":
            case "utf8":
            case "hex":
                return "string";

            default:
                return "unknown";
        }
    }

    /// <summary>
    /// 将字段类型映射到表达式类型
    /// </summary>
    private static string MapFieldTypeToExprType(string fieldType)
    {
        return fieldType.ToLowerInvariant() switch
        {
            "uint8" or "int8" or "uint16" or "int16" or "uint32" or "int32" or 
            "uint64" or "int64" or "float32" or "float64" => "number",
            "bool" => "bool",
            "string" or "char" => "string",
            "bytes" => "bytes",
            _ => "unknown"
        };
    }

    /// <summary>
    /// 验证类型是否有效
    /// </summary>
    private static bool IsValidType(string type)
    {
        var validTypes = new[]
        {
            "uint8", "int8", "uint16", "int16", "uint32", "int32", "uint64", "int64",
            "float32", "float64", "bool", "string", "bytes", "char", "void"
        };

        return validTypes.Contains(type.ToLowerInvariant());
    }

    /// <summary>
    /// 添加错误
    /// </summary>
    private void AddError(string message, int line, int column)
    {
        _errors.Add(new SemanticError(message, line, column));
    }
}

/// <summary>
/// 语义分析结果
/// </summary>
public sealed class SemanticAnalysisResult
{
    public ProtocolDefinition Protocol { get; }
    public IReadOnlyList<SemanticError> Errors { get; }
    public bool Success => Errors.Count == 0;

    public SemanticAnalysisResult(ProtocolDefinition protocol, List<SemanticError> errors)
    {
        Protocol = protocol;
        Errors = errors;
    }
}

/// <summary>
/// 语义错误
/// </summary>
public sealed class SemanticError
{
    public string Message { get; }
    public int Line { get; }
    public int Column { get; }

    public SemanticError(string message, int line, int column)
    {
        Message = message;
        Line = line;
        Column = column;
    }

    public override string ToString()
    {
        return $"{Message} at Line {Line}, Column {Column}";
    }
}
