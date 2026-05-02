using ComCross.Core.Cpdl;
using ComCross.Core.Cpdl.Ast;
using Xunit;

namespace ComCross.Tests.Core.Cpdl;

public class CpdlParserTests
{
    private ParserResult Parse(string source)
    {
        var lexer = new CpdlLexer(source);
        var lexerResult = lexer.ScanTokens();
        
        Assert.True(lexerResult.Success, $"Lexer failed: {string.Join(", ", lexerResult.Errors)}");
        
        var parser = new CpdlParser(lexerResult.Tokens.ToList());
        return parser.Parse();
    }
    
    [Fact]
    public void Parser_EmptyProtocol_Parses()
    {
        // Arrange
        var source = "protocol Test { }";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Protocol);
        Assert.Equal("Test", result.Protocol.Name);
        Assert.Empty(result.Protocol.Messages);
    }
    
    [Fact]
    public void Parser_ProtocolWithVersion_Parses()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                version ""1.0""
                description ""Modbus RTU Protocol""
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal("ModbusRTU", result.Protocol!.Name);
        Assert.Equal("1.0", result.Protocol.Version);
        Assert.Equal("Modbus RTU Protocol", result.Protocol.Description);
    }
    
    [Fact]
    public void Parser_SimpleEnum_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                enum Status {
                    OK = 0,
                    ERROR = 1
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Protocol!.Enums);
        
        var enumDef = result.Protocol.Enums[0];
        Assert.Equal("Status", enumDef.Name);
        Assert.Equal(2, enumDef.Members.Count);
        Assert.Equal("OK", enumDef.Members[0].Name);
        Assert.Equal("ERROR", enumDef.Members[1].Name);
    }
    
    [Fact]
    public void Parser_EnumWithHexValues_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                enum FunctionCode {
                    READ_HOLDING_REGISTERS = 0x03,
                    WRITE_SINGLE_REGISTER = 0x06
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        var enumDef = result.Protocol!.Enums[0];
        Assert.Equal("FunctionCode", enumDef.Name);
        Assert.Equal(2, enumDef.Members.Count);
        
        // 验证值是LiteralExpression
        Assert.IsType<LiteralExpression>(enumDef.Members[0].Value);
        Assert.IsType<LiteralExpression>(enumDef.Members[1].Value);
    }
    
    [Fact]
    public void Parser_SimpleMessage_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 device_address;
                    uint8 function_code;
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Protocol!.Messages);
        
        var message = result.Protocol.Messages[0];
        Assert.Equal("Request", message.Name);
        Assert.Equal(2, message.Fields.Count);
        Assert.Equal("uint8", message.Fields[0].Type);
        Assert.Equal("device_address", message.Fields[0].Name);
    }
    
    [Fact]
    public void Parser_FieldWithModifiers_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 device_address range(1, 247);
                    uint16 crc endian(big) validate(crc16(this[0..-3]));
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success, $"Parse failed: {string.Join("\n", result.Errors)}");
        var message = result.Protocol!.Messages[0];
        
        // 第一个字段
        var field1 = message.Fields[0];
        Assert.Single(field1.Modifiers);
        Assert.Equal("range", field1.Modifiers[0].Name);
        Assert.Equal(2, field1.Modifiers[0].Arguments.Count);
        
        // 第二个字段
        var field2 = message.Fields[1];
        Assert.Equal(2, field2.Modifiers.Count);
        Assert.Equal("endian", field2.Modifiers[0].Name);
        Assert.Equal("validate", field2.Modifiers[1].Name);
    }
    
    [Fact]
    public void Parser_OptionalField_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 device_address;
                    optional string description;
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        var message = result.Protocol!.Messages[0];
        Assert.False(message.Fields[0].IsOptional);
        Assert.True(message.Fields[1].IsOptional);
    }
    
    [Fact]
    public void Parser_MessageWithInheritance_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                message BaseMessage {
                    uint8 type;
                }
                message DerivedMessage extends BaseMessage {
                    uint8 data;
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Protocol!.Messages.Count);
        Assert.Null(result.Protocol.Messages[0].BaseMessage);
        Assert.Equal("BaseMessage", result.Protocol.Messages[1].BaseMessage);
    }
    
    [Fact]
    public void Parser_ParserWithWhenRule_Parses()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 function_code;
                }
                
                parser {
                    when (function_code == 0x03) {
                        display ""Read Holding Registers"";
                        color info;
                    }
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success, $"Parse failed: {string.Join("\n", result.Errors)}");
        Assert.NotNull(result.Protocol!.Parser);
        Assert.Single(result.Protocol.Parser.Rules);
        
        var rule = result.Protocol.Parser.Rules[0];
        Assert.IsType<BinaryExpression>(rule.Condition);
        Assert.Equal(2, rule.Actions.Count);
        Assert.Equal("display", rule.Actions[0].Type);
        Assert.Equal("color", rule.Actions[1].Type);
    }
    
    [Fact]
    public void Parser_BinaryExpression_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                enum Test {
                    A = 1 + 2 * 3
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        var enumValue = result.Protocol!.Enums[0].Members[0].Value;
        
        // 应该是: 1 + (2 * 3) 由于优先级
        Assert.IsType<BinaryExpression>(enumValue);
        var binary = (BinaryExpression)enumValue;
        Assert.Equal("+", binary.Operator);
        Assert.IsType<LiteralExpression>(binary.Left);
        Assert.IsType<BinaryExpression>(binary.Right);
    }
    
    [Fact]
    public void Parser_ComparisonExpression_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request { uint8 value; }
                parser {
                    when (value > 10 && value < 100) {
                        display ""In range"";
                    }
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        var condition = result.Protocol!.Parser!.Rules[0].Condition;
        
        Assert.IsType<BinaryExpression>(condition);
        var andExpr = (BinaryExpression)condition;
        Assert.Equal("&&", andExpr.Operator);
        Assert.IsType<BinaryExpression>(andExpr.Left);  // value > 10
        Assert.IsType<BinaryExpression>(andExpr.Right); // value < 100
    }
    
    [Fact]
    public void Parser_UnaryExpression_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                enum Test {
                    NEG = -5,
                    NOT = !true
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        var members = result.Protocol!.Enums[0].Members;
        
        Assert.IsType<UnaryExpression>(members[0].Value);
        var negExpr = (UnaryExpression)members[0].Value;
        Assert.Equal("-", negExpr.Operator);
        
        Assert.IsType<UnaryExpression>(members[1].Value);
        var notExpr = (UnaryExpression)members[1].Value;
        Assert.Equal("!", notExpr.Operator);
    }
    
    [Fact]
    public void Parser_MemberAccess_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request { uint8 value; }
                parser {
                    when (message.value == 10) {
                        display ""Match"";
                    }
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success, $"Parse failed: {string.Join("\n", result.Errors)}");
        var condition = result.Protocol!.Parser!.Rules[0].Condition;
        
        var binary = (BinaryExpression)condition;
        Assert.IsType<MemberAccessExpression>(binary.Left);
        var memberAccess = (MemberAccessExpression)binary.Left;
        Assert.IsType<IdentifierExpression>(memberAccess.Object);
        Assert.Equal("value", memberAccess.MemberName);
    }
    
    [Fact]
    public void Parser_IndexExpression_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request { bytes data; }
                parser {
                    when (data[0] == 0xFF) {
                        display ""Valid header"";
                    }
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success, $"Parse failed: {string.Join("\n", result.Errors)}");
        var condition = result.Protocol!.Parser!.Rules[0].Condition;
        
        var binary = (BinaryExpression)condition;
        Assert.IsType<IndexExpression>(binary.Left);
        var indexExpr = (IndexExpression)binary.Left;
        Assert.IsType<IdentifierExpression>(indexExpr.Object);
        Assert.IsType<LiteralExpression>(indexExpr.Index);
    }
    
    [Fact]
    public void Parser_SliceExpression_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                enum Test {
                    VALUE = data[0..3]
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success, $"Parse failed: {string.Join("\n", result.Errors)}");
        var value = result.Protocol!.Enums[0].Members[0].Value;
        
        Assert.IsType<IndexExpression>(value);
        var indexExpr = (IndexExpression)value;
        Assert.NotNull(indexExpr.EndIndex);
    }
    
    [Fact]
    public void Parser_CallExpression_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request { 
                    bytes data;
                    uint16 crc validate(crc16(data));
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success);
        var field = result.Protocol!.Messages[0].Fields[1];
        var modifier = field.Modifiers[0];
        
        Assert.Equal("validate", modifier.Name);
        Assert.Single(modifier.Arguments);
        Assert.IsType<CallExpression>(modifier.Arguments[0]);
        
        var callExpr = (CallExpression)modifier.Arguments[0];
        Assert.Equal("crc16", callExpr.FunctionName);
        Assert.Single(callExpr.Arguments);
    }
    
    [Fact]
    public void Parser_ComplexProtocol_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                version ""1.0""
                description ""Modbus RTU Protocol""
                
                enum FunctionCode {
                    READ_HOLDING_REGISTERS = 0x03,
                    WRITE_SINGLE_REGISTER = 0x06
                }
                
                message Request {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 starting_address endian(big);
                    uint16 quantity endian(big) range(1, 125);
                    uint16 crc endian(little) validate(crc16(this[0..-3]));
                }
                
                message Response {
                    uint8 device_address;
                    uint8 function_code;
                    uint8 byte_count length(data);
                    bytes data length(byte_count);
                    uint16 crc endian(little);
                }
                
                parser {
                    when (function_code == READ_HOLDING_REGISTERS) {
                        display ""Read {quantity} registers from device {device_address}"";
                        color info;
                    }
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal("ModbusRTU", result.Protocol!.Name);
        Assert.Equal("1.0", result.Protocol.Version);
        Assert.Single(result.Protocol.Enums);
        Assert.Equal(2, result.Protocol.Messages.Count);
        Assert.NotNull(result.Protocol.Parser);
        
        // 验证enum
        var enumDef = result.Protocol.Enums[0];
        Assert.Equal("FunctionCode", enumDef.Name);
        Assert.Equal(2, enumDef.Members.Count);
        
        // 验证message
        var request = result.Protocol.Messages[0];
        Assert.Equal("Request", request.Name);
        Assert.Equal(5, request.Fields.Count);
        
        // 验证parser
        Assert.Single(result.Protocol.Parser.Rules);
    }
    
    [Fact]
    public void Parser_MissingProtocolKeyword_ReturnsError()
    {
        // Arrange
        var source = "Test { }";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("protocol", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void Parser_MissingBrace_ReturnsError()
    {
        // Arrange
        var source = "protocol Test {";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }
    
    [Fact]
    public void Parser_InvalidFieldType_ReturnsError()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Msg {
                    invalid_type field;
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("field type", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void Parser_MissingSemicolon_ReturnsError()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Msg {
                    uint8 field
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(";", result.Errors[0]);
    }
    
    [Fact]
    public void Parser_MultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var source = @"
            protocol {
                message {
                    invalid_type field
                }
            }
        ";
        
        // Act
        var result = Parse(source);
        
        // Assert
        Assert.False(result.Success);
        // 应该有多个错误（缺少protocol名称、缺少message名称、无效类型、缺少分号）
        Assert.True(result.Errors.Count >= 2, $"Expected >= 2 errors, got {result.Errors.Count}: {string.Join("; ", result.Errors)}");
    }
}
