using System.Linq.Expressions;
using System.Reflection;
using ComCross.Core.Cpdl.Ast;
using ComCross.Shared.Interfaces;
using Expression = System.Linq.Expressions.Expression;
using CpdlExpression = ComCross.Core.Cpdl.Ast.Expression;
using CpdlBinaryExpression = ComCross.Core.Cpdl.Ast.BinaryExpression;
using CpdlUnaryExpression = ComCross.Core.Cpdl.Ast.UnaryExpression;
using CpdlIndexExpression = ComCross.Core.Cpdl.Ast.IndexExpression;

namespace ComCross.Core.Cpdl.Compiler;

/// <summary>
/// 解析委托 - 使用byte[]因为Expression Tree不支持ReadOnlySpan<byte>(ref struct)
/// </summary>
public delegate ParseResult ParseDelegate(byte[] data);

/// <summary>
/// 代码生成器 - 将AST编译为Expression Tree
/// 生成高性能的字节流解析代码
/// </summary>
public sealed class CodeGenerator
{
    private readonly MessageDefinition _message;
    private ParameterExpression _dataParam = null!;
    private ParameterExpression _offsetParam = null!;
    private readonly Dictionary<string, Expression> _fieldValues = new();

    public CodeGenerator(MessageDefinition message)
    {
        _message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// 生成消息解析委托
    /// </summary>
    public ParseDelegate GenerateParser()
    {
        // 参数：data (byte[])
        _dataParam = Expression.Parameter(typeof(byte[]), "data");
        _offsetParam = Expression.Variable(typeof(int), "offset");

        var expressions = new List<Expression>();
        var variables = new List<ParameterExpression> { _offsetParam };

        // offset = 0
        expressions.Add(Expression.Assign(_offsetParam, Expression.Constant(0)));

        // 字段字典
        var fieldsVar = Expression.Variable(typeof(Dictionary<string, object?>), "fields");
        variables.Add(fieldsVar);
        
        var dictCtor = typeof(Dictionary<string, object?>).GetConstructor(Type.EmptyTypes)!;
        expressions.Add(Expression.Assign(fieldsVar, Expression.New(dictCtor)));

        // 解析每个字段
        foreach (var field in _message.Fields)
        {
            var (fieldVar, fieldExprs) = GenerateFieldParser(field, fieldsVar);
            if (fieldVar != null)
            {
                variables.Add(fieldVar);
            }
            expressions.AddRange(fieldExprs);
        }

        // 返回成功结果
        var rawDataExpr = _dataParam; // 直接使用byte[]数组

        var successMethod = typeof(ParseResult).GetMethod(
            nameof(ParseResult.Success), 
            BindingFlags.Public | BindingFlags.Static)!;

        var resultExpr = Expression.Call(
            successMethod,
            fieldsVar,
            rawDataExpr
        );

        expressions.Add(resultExpr);

        // 构建lambda
        var body = Expression.Block(variables, expressions);
        var lambda = Expression.Lambda<ParseDelegate>(body, _dataParam);

        return lambda.Compile();
    }

    /// <summary>
    /// 生成字段解析代码，返回(变量, 表达式列表)
    /// </summary>
    private (ParameterExpression?, List<Expression>) GenerateFieldParser(FieldDefinition field, Expression fieldsDict)
    {
        var expressions = new List<Expression>();

        // 根据字段类型生成读取代码
        Expression valueExpr = field.Type.ToLowerInvariant() switch
        {
            "uint8" => GenerateReadUInt8(),
            "int8" => GenerateReadInt8(),
            "uint16" => GenerateReadUInt16(field),
            "int16" => GenerateReadInt16(field),
            "uint32" => GenerateReadUInt32(field),
            "int32" => GenerateReadInt32(field),
            "uint64" => GenerateReadUInt64(field),
            "int64" => GenerateReadInt64(field),
            "float32" => GenerateReadFloat32(field),
            "float64" => GenerateReadFloat64(field),
            "bool" => GenerateReadBool(),
            "string" => GenerateReadString(field),
            "bytes" => GenerateReadBytes(field),
            _ => throw new NotSupportedException($"Unsupported field type: {field.Type}")
        };

        // 保存字段值（用于后续验证）
        var fieldVar = Expression.Variable(typeof(object), $"field_{field.Name}");
        _fieldValues[field.Name] = fieldVar;

        expressions.Add(Expression.Assign(fieldVar, Expression.Convert(valueExpr, typeof(object))));

        // 应用修饰符（验证、转换等）
        foreach (var modifier in field.Modifiers)
        {
            var modifierExprs = GenerateModifier(modifier, fieldVar, field);
            expressions.AddRange(modifierExprs);
        }

        // 添加到字典
        var addMethod = typeof(Dictionary<string, object?>).GetMethod("Add")!;
        expressions.Add(Expression.Call(
            fieldsDict,
            addMethod,
            Expression.Constant(field.Name),
            fieldVar
        ));

        return (fieldVar, expressions);
    }

    /// <summary>
    /// 生成修饰符验证代码
    /// </summary>
    private List<Expression> GenerateModifier(FieldModifier modifier, Expression fieldValue, FieldDefinition field)
    {
        var expressions = new List<Expression>();

        switch (modifier.Name.ToLowerInvariant())
        {
            case "range":
                if (modifier.Arguments.Count == 2)
                {
                    var minExpr = GenerateExpression(modifier.Arguments[0]);
                    var maxExpr = GenerateExpression(modifier.Arguments[1]);
                    
                    // fieldValue是object类型，需要先unbox再转换为long
                    // 使用Convert.ToInt64 helper方法
                    var toInt64Method = typeof(Convert).GetMethod(nameof(Convert.ToInt64), new[] { typeof(object) })!;
                    var valueNum = Expression.Call(toInt64Method, fieldValue);
                    var minLong = Expression.Convert(minExpr, typeof(long));
                    var maxLong = Expression.Convert(maxExpr, typeof(long));

                    var condition = Expression.OrElse(
                        Expression.LessThan(valueNum, minLong),
                        Expression.GreaterThan(valueNum, maxLong)
                    );

                    var throwExpr = Expression.Throw(
                        Expression.New(
                            typeof(InvalidDataException).GetConstructor(new[] { typeof(string) })!,
                            Expression.Constant($"Field '{field.Name}' out of range")
                        )
                    );

                    expressions.Add(Expression.IfThen(condition, throwExpr));
                }
                break;

            case "validate":
                // validate修饰符生成验证表达式
                if (modifier.Arguments.Count > 0)
                {
                    var validateExpr = GenerateExpression(modifier.Arguments[0]);
                    var boolExpr = Expression.Convert(validateExpr, typeof(bool));
                    
                    var throwExpr = Expression.Throw(
                        Expression.New(
                            typeof(InvalidDataException).GetConstructor(new[] { typeof(string) })!,
                            Expression.Constant($"Field '{field.Name}' validation failed")
                        )
                    );

                    expressions.Add(Expression.IfThen(Expression.Not(boolExpr), throwExpr));
                }
                break;

            // endian, length, encoding等修饰符在字段读取时已处理
            default:
                break;
        }

        return expressions;
    }

    /// <summary>
    /// 生成CPDL表达式对应的C#表达式树
    /// </summary>
    private Expression GenerateExpression(CpdlExpression expr)
    {
        return expr switch
        {
            LiteralExpression lit => GenerateLiteral(lit),
            IdentifierExpression id => GenerateIdentifier(id),
            CpdlBinaryExpression bin => GenerateBinaryExpression(bin),
            CpdlUnaryExpression un => GenerateUnaryExpression(un),
            MemberAccessExpression mem => GenerateMemberAccess(mem),
            CpdlIndexExpression idx => GenerateIndexExpression(idx),
            CallExpression call => GenerateCallExpression(call),
            _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}")
        };
    }

    /// <summary>
    /// 生成字面量表达式
    /// </summary>
    private Expression GenerateLiteral(LiteralExpression lit)
    {
        return Expression.Constant(lit.Value);
    }

    /// <summary>
    /// 生成标识符表达式（字段引用）
    /// </summary>
    private Expression GenerateIdentifier(IdentifierExpression id)
    {
        if (_fieldValues.TryGetValue(id.Name, out var fieldExpr))
        {
            return fieldExpr;
        }

        throw new InvalidOperationException($"Unknown identifier: {id.Name}");
    }

    /// <summary>
    /// 生成二元表达式
    /// </summary>
    private Expression GenerateBinaryExpression(CpdlBinaryExpression bin)
    {
        var left = GenerateExpression(bin.Left);
        var right = GenerateExpression(bin.Right);

        // 确保类型匹配
        if (left.Type != right.Type)
        {
            if (IsNumericType(left.Type) && IsNumericType(right.Type))
            {
                // 提升到更大的类型
                var targetType = GetLargerNumericType(left.Type, right.Type);
                left = Expression.Convert(left, targetType);
                right = Expression.Convert(right, targetType);
            }
        }

        return bin.Operator switch
        {
            "+" => Expression.Add(left, right),
            "-" => Expression.Subtract(left, right),
            "*" => Expression.Multiply(left, right),
            "/" => Expression.Divide(left, right),
            "%" => Expression.Modulo(left, right),
            "==" => Expression.Equal(left, right),
            "!=" => Expression.NotEqual(left, right),
            ">" => Expression.GreaterThan(left, right),
            "<" => Expression.LessThan(left, right),
            ">=" => Expression.GreaterThanOrEqual(left, right),
            "<=" => Expression.LessThanOrEqual(left, right),
            "&&" => Expression.AndAlso(left, right),
            "||" => Expression.OrElse(left, right),
            "&" => Expression.And(left, right),
            "|" => Expression.Or(left, right),
            "^" => Expression.ExclusiveOr(left, right),
            "<<" => Expression.LeftShift(left, right),
            ">>" => Expression.RightShift(left, right),
            _ => throw new NotSupportedException($"Unsupported operator: {bin.Operator}")
        };
    }

    /// <summary>
    /// 生成一元表达式
    /// </summary>
    private Expression GenerateUnaryExpression(CpdlUnaryExpression un)
    {
        var operand = GenerateExpression(un.Operand);

        return un.Operator switch
        {
            "-" => Expression.Negate(operand),
            "!" or "not" => Expression.Not(operand),
            "~" => Expression.Not(operand),
            _ => throw new NotSupportedException($"Unsupported unary operator: {un.Operator}")
        };
    }

    /// <summary>
    /// 生成成员访问表达式
    /// </summary>
    private Expression GenerateMemberAccess(MemberAccessExpression mem)
    {
        var obj = GenerateExpression(mem.Object);
        // 简化：假设对象是字典，成员名是键
        return obj; // TODO: 实现真正的成员访问
    }

    /// <summary>
    /// 生成索引表达式
    /// </summary>
    private Expression GenerateIndexExpression(CpdlIndexExpression idx)
    {
        var obj = GenerateExpression(idx.Object);
        var index = GenerateExpression(idx.Index);

        if (idx.EndIndex != null)
        {
            // 切片操作
            var endIndex = GenerateExpression(idx.EndIndex);
            // TODO: 生成切片代码
            return obj;
        }
        else
        {
            // 索引操作
            return Expression.ArrayIndex(obj, index);
        }
    }

    /// <summary>
    /// 生成函数调用表达式
    /// </summary>
    private Expression GenerateCallExpression(CallExpression call)
    {
        var funcName = call.FunctionName.ToLowerInvariant();
        var args = call.Arguments.Select(GenerateExpression).ToList();

        return funcName switch
        {
            "crc16" => GenerateCrc16Call(args),
            "crc32" => GenerateCrc32Call(args),
            "length" => GenerateLengthCall(args),
            _ => throw new NotSupportedException($"Unsupported function: {call.FunctionName}")
        };
    }

    // ===== 字段读取方法 =====

    private Expression GenerateReadUInt8()
    {
        // data[offset], 然后 offset++
        // 先保存值，再递增
        var resultVar = Expression.Variable(typeof(byte), "byteValue");
        var readExpr = Expression.Assign(resultVar, Expression.ArrayIndex(_dataParam, _offsetParam));
        var incrementExpr = Expression.PostIncrementAssign(_offsetParam);
        
        return Expression.Block(
            new[] { resultVar },
            readExpr,
            incrementExpr,
            resultVar
        );
    }

    private Expression GenerateReadInt8()
    {
        var byteExpr = GenerateReadUInt8();
        return Expression.Convert(byteExpr, typeof(sbyte));
    }

    private Expression GenerateReadUInt16(FieldDefinition field)
    {
        var isBigEndian = GetEndianness(field);
        
        // 读取2字节
        var byte0 = Expression.ArrayIndex(_dataParam, _offsetParam);
        var offset1 = Expression.Add(_offsetParam, Expression.Constant(1));
        var byte1 = Expression.ArrayIndex(_dataParam, offset1);
        
        var resultVar = Expression.Variable(typeof(ushort), "uint16Value");
        Expression calcExpr;
        if (isBigEndian)
        {
            // (byte0 << 8) | byte1
            calcExpr = Expression.Assign(resultVar, Expression.Or(
                Expression.LeftShift(Expression.Convert(byte0, typeof(ushort)), Expression.Constant(8)),
                Expression.Convert(byte1, typeof(ushort))
            ));
        }
        else
        {
            // (byte1 << 8) | byte0
            calcExpr = Expression.Assign(resultVar, Expression.Or(
                Expression.LeftShift(Expression.Convert(byte1, typeof(ushort)), Expression.Constant(8)),
                Expression.Convert(byte0, typeof(ushort))
            ));
        }

        // offset += 2
        var incrementExpr = Expression.AddAssign(_offsetParam, Expression.Constant(2));

        return Expression.Block(new[] { resultVar }, calcExpr, incrementExpr, resultVar);
    }

    private Expression GenerateReadInt16(FieldDefinition field)
    {
        var uint16Expr = GenerateReadUInt16(field);
        return Expression.Convert(uint16Expr, typeof(short));
    }

    private Expression GenerateReadUInt32(FieldDefinition field)
    {
        var isBigEndian = GetEndianness(field);
        
        // 读取4字节
        var bytes = new List<Expression>();
        for (int i = 0; i < 4; i++)
        {
            var offset = Expression.Add(_offsetParam, Expression.Constant(i));
            bytes.Add(Expression.ArrayIndex(_dataParam, offset));
        }
        
        // offset += 4
        var incrementExpr = Expression.AddAssign(_offsetParam, Expression.Constant(4));

        Expression resultExpr;
        if (isBigEndian)
        {
            resultExpr = Expression.Or(
                Expression.Or(
                    Expression.LeftShift(Expression.Convert(bytes[0], typeof(uint)), Expression.Constant(24)),
                    Expression.LeftShift(Expression.Convert(bytes[1], typeof(uint)), Expression.Constant(16))
                ),
                Expression.Or(
                    Expression.LeftShift(Expression.Convert(bytes[2], typeof(uint)), Expression.Constant(8)),
                    Expression.Convert(bytes[3], typeof(uint))
                )
            );
        }
        else
        {
            resultExpr = Expression.Or(
                Expression.Or(
                    Expression.LeftShift(Expression.Convert(bytes[3], typeof(uint)), Expression.Constant(24)),
                    Expression.LeftShift(Expression.Convert(bytes[2], typeof(uint)), Expression.Constant(16))
                ),
                Expression.Or(
                    Expression.LeftShift(Expression.Convert(bytes[1], typeof(uint)), Expression.Constant(8)),
                    Expression.Convert(bytes[0], typeof(uint))
                )
            );
        }

        return Expression.Block(new Expression[] { incrementExpr, resultExpr });
    }

    private Expression GenerateReadInt32(FieldDefinition field)
    {
        var uint32Expr = GenerateReadUInt32(field);
        return Expression.Convert(uint32Expr, typeof(int));
    }

    private Expression GenerateReadUInt64(FieldDefinition field)
    {
        // 使用BitConverter.ToUInt64(byte[], int)
        var resultVar = Expression.Variable(typeof(ulong), "uint64Value");
        
        var convertMethod = typeof(BitConverter).GetMethod(nameof(BitConverter.ToUInt64), 
            new[] { typeof(byte[]), typeof(int) })!;
        
        var calcExpr = Expression.Assign(resultVar, Expression.Call(convertMethod, _dataParam, _offsetParam));
        var incrementExpr = Expression.AddAssign(_offsetParam, Expression.Constant(8));

        return Expression.Block(new[] { resultVar }, calcExpr, incrementExpr, resultVar);
    }

    private Expression GenerateReadInt64(FieldDefinition field)
    {
        var uint64Expr = GenerateReadUInt64(field);
        return Expression.Convert(uint64Expr, typeof(long));
    }

    private Expression GenerateReadFloat32(FieldDefinition field)
    {
        var resultVar = Expression.Variable(typeof(float), "float32Value");
        
        var convertMethod = typeof(BitConverter).GetMethod(nameof(BitConverter.ToSingle), 
            new[] { typeof(byte[]), typeof(int) })!;
        
        var calcExpr = Expression.Assign(resultVar, Expression.Call(convertMethod, _dataParam, _offsetParam));
        var incrementExpr = Expression.AddAssign(_offsetParam, Expression.Constant(4));

        return Expression.Block(new[] { resultVar }, calcExpr, incrementExpr, resultVar);
    }

    private Expression GenerateReadFloat64(FieldDefinition field)
    {
        var resultVar = Expression.Variable(typeof(double), "float64Value");
        
        var convertMethod = typeof(BitConverter).GetMethod(nameof(BitConverter.ToDouble), 
            new[] { typeof(byte[]), typeof(int) })!;
        
        var calcExpr = Expression.Assign(resultVar, Expression.Call(convertMethod, _dataParam, _offsetParam));
        var incrementExpr = Expression.AddAssign(_offsetParam, Expression.Constant(8));

        return Expression.Block(new[] { resultVar }, calcExpr, incrementExpr, resultVar);
    }

    private Expression GenerateReadBool()
    {
        var byteExpr = GenerateReadUInt8();
        return Expression.NotEqual(byteExpr, Expression.Constant((byte)0));
    }

    private Expression GenerateReadString(FieldDefinition field)
    {
        // 简化：读取固定长度字符串（需要length修饰符）
        var lengthModifier = field.Modifiers.FirstOrDefault(m => m.Name.ToLowerInvariant() == "length");
        if (lengthModifier == null || lengthModifier.Arguments.Count == 0)
        {
            throw new InvalidOperationException($"String field '{field.Name}' requires length() modifier");
        }

        var lengthExpr = GenerateExpression(lengthModifier.Arguments[0]);
        var lengthInt = Expression.Convert(lengthExpr, typeof(int));

        var resultVar = Expression.Variable(typeof(string), "stringValue");
        
        // Encoding.ASCII.GetString(data, offset, length)
        var encodingProp = Expression.Property(null, typeof(System.Text.Encoding), "ASCII");
        var getStringMethod = typeof(System.Text.Encoding).GetMethod("GetString", 
            new[] { typeof(byte[]), typeof(int), typeof(int) })!;
        var calcExpr = Expression.Assign(resultVar, Expression.Call(encodingProp, getStringMethod, _dataParam, _offsetParam, lengthInt));
        
        // offset += length
        var incrementExpr = Expression.AddAssign(_offsetParam, lengthInt);

        return Expression.Block(new[] { resultVar }, calcExpr, incrementExpr, resultVar);
    }

    private Expression GenerateReadBytes(FieldDefinition field)
    {
        var lengthModifier = field.Modifiers.FirstOrDefault(m => m.Name.ToLowerInvariant() == "length");
        if (lengthModifier == null || lengthModifier.Arguments.Count == 0)
        {
            throw new InvalidOperationException($"Bytes field '{field.Name}' requires length() modifier");
        }

        var lengthExpr = GenerateExpression(lengthModifier.Arguments[0]);
        var lengthInt = Expression.Convert(lengthExpr, typeof(int));

        // Array.Copy to new byte array
        var resultVar = Expression.Variable(typeof(byte[]), "result");
        var newArray = Expression.NewArrayBounds(typeof(byte), lengthInt);
        var assignResult = Expression.Assign(resultVar, newArray);
        
        var arrayCopyMethod = typeof(Array).GetMethod("Copy", 
            new[] { typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int) })!;
        var copyExpr = Expression.Call(arrayCopyMethod, 
            _dataParam, _offsetParam, resultVar, Expression.Constant(0), lengthInt);
        
        var incrementExpr = Expression.AddAssign(_offsetParam, lengthInt);

        return Expression.Block(
            new[] { resultVar },
            assignResult,
            copyExpr,
            incrementExpr,
            resultVar
        );
    }

    // ===== 内置函数 =====

    private Expression GenerateCrc16Call(List<Expression> args)
    {
        // 调用ChecksumHelper.Crc16Modbus
        if (args.Count != 1)
        {
            throw new InvalidOperationException("crc16() requires exactly 1 argument");
        }

        // args[0]应该是byte[]类型（data的切片）
        // 简化：目前直接对整个data计算CRC
        var crc16Method = typeof(ChecksumHelper).GetMethod(nameof(ChecksumHelper.Crc16Modbus))!;
        return Expression.Call(crc16Method, _dataParam);
    }

    private Expression GenerateCrc32Call(List<Expression> args)
    {
        // 调用ChecksumHelper.Crc32
        if (args.Count != 1)
        {
            throw new InvalidOperationException("crc32() requires exactly 1 argument");
        }

        var crc32Method = typeof(ChecksumHelper).GetMethod(nameof(ChecksumHelper.Crc32))!;
        return Expression.Call(crc32Method, _dataParam);
    }

    private Expression GenerateLengthCall(List<Expression> args)
    {
        if (args.Count > 0)
        {
            var arrayExpr = args[0];
            return Expression.Property(arrayExpr, "Length");
        }
        
        return Expression.Property(_dataParam, "Length");
    }

    // ===== 辅助方法 =====

    private bool GetEndianness(FieldDefinition field)
    {
        var endianModifier = field.Modifiers.FirstOrDefault(m => m.Name.ToLowerInvariant() == "endian");
        if (endianModifier != null && endianModifier.Arguments.Count > 0)
        {
            if (endianModifier.Arguments[0] is IdentifierExpression idExpr)
            {
                return idExpr.Name.ToLowerInvariant() == "big";
            }
        }
        
        return false; // 默认小端序
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double);
    }

    private static Type GetLargerNumericType(Type t1, Type t2)
    {
        if (t1 == typeof(double) || t2 == typeof(double)) return typeof(double);
        if (t1 == typeof(float) || t2 == typeof(float)) return typeof(float);
        if (t1 == typeof(ulong) || t2 == typeof(ulong)) return typeof(ulong);
        if (t1 == typeof(long) || t2 == typeof(long)) return typeof(long);
        if (t1 == typeof(uint) || t2 == typeof(uint)) return typeof(uint);
        if (t1 == typeof(int) || t2 == typeof(int)) return typeof(int);
        if (t1 == typeof(ushort) || t2 == typeof(ushort)) return typeof(ushort);
        return typeof(short);
    }
}
