using ComCross.Core.Cpdl;
using Xunit;

namespace ComCross.Tests.Core.Cpdl;

public class CpdlLexerTests
{
    [Fact]
    public void Lexer_EmptySource_ReturnsEOF()
    {
        // Arrange
        var lexer = new CpdlLexer("");
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Tokens);
        Assert.Equal(TokenType.EOF, result.Tokens[0].Type);
    }
    
    [Fact]
    public void Lexer_Keywords_RecognizesAllKeywords()
    {
        // Arrange
        var source = "protocol message enum parser when import as extends optional";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.Tokens.Count); // 9 keywords + EOF
        Assert.Equal(TokenType.PROTOCOL, result.Tokens[0].Type);
        Assert.Equal(TokenType.MESSAGE, result.Tokens[1].Type);
        Assert.Equal(TokenType.ENUM, result.Tokens[2].Type);
        Assert.Equal(TokenType.PARSER, result.Tokens[3].Type);
        Assert.Equal(TokenType.WHEN, result.Tokens[4].Type);
    }
    
    [Fact]
    public void Lexer_TypeKeywords_RecognizesAllTypes()
    {
        // Arrange
        var source = "uint8 int8 uint16 int16 uint32 int32 uint64 int64 float32 float64 bool string bytes char void";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(16, result.Tokens.Count); // 15 types + EOF
        Assert.Equal(TokenType.UINT8, result.Tokens[0].Type);
        Assert.Equal(TokenType.INT8, result.Tokens[1].Type);
        Assert.Equal(TokenType.FLOAT32, result.Tokens[8].Type);
        Assert.Equal(TokenType.BOOL, result.Tokens[10].Type);
    }
    
    [Fact]
    public void Lexer_Symbols_RecognizesAllSymbols()
    {
        // Arrange
        // 单字符符号
        var source1 = "{ } [ ] ( ) ; , . % ^ ~ ?";
        var lexer1 = new CpdlLexer(source1);
        var result1 = lexer1.ScanTokens();
        
        Assert.True(result1.Success);
        Assert.Equal(TokenType.LBRACE, result1.Tokens[0].Type);
        Assert.Equal(TokenType.RBRACE, result1.Tokens[1].Type);
        Assert.Equal(TokenType.PERCENT, result1.Tokens[9].Type);
        
        // 双字符符号（包括可能被误判的）
        var source2 = "== != >= <= && || << >> ->";
        var lexer2 = new CpdlLexer(source2);
        var result2 = lexer2.ScanTokens();
        
        Assert.True(result2.Success);
        Assert.Equal(TokenType.EQUAL, result2.Tokens[0].Type);
        Assert.Equal(TokenType.NOT_EQUAL, result2.Tokens[1].Type);
        Assert.Equal(TokenType.GREATER_EQUAL, result2.Tokens[2].Type);
        Assert.Equal(TokenType.LESS_EQUAL, result2.Tokens[3].Type);
        Assert.Equal(TokenType.AND_AND, result2.Tokens[4].Type);
        Assert.Equal(TokenType.OR_OR, result2.Tokens[5].Type);
        Assert.Equal(TokenType.ARROW, result2.Tokens[8].Type);
        
        // 单双混合的符号
        var source3 = "= ! > < & | + - * / :";
        var lexer3 = new CpdlLexer(source3);
        var result3 = lexer3.ScanTokens();
        
        Assert.True(result3.Success);
        Assert.Equal(TokenType.ASSIGN, result3.Tokens[0].Type);
        Assert.Equal(TokenType.BANG, result3.Tokens[1].Type);
        Assert.Equal(TokenType.GREATER, result3.Tokens[2].Type);
        Assert.Equal(TokenType.LESS, result3.Tokens[3].Type);
    }
    
    [Fact]
    public void Lexer_Identifier_RecognizesValidIdentifiers()
    {
        // Arrange
        var source = "device_address functionCode _internal MAX_LENGTH";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.Tokens.Count); // 4 identifiers + EOF
        Assert.All(result.Tokens.Take(4), token => Assert.Equal(TokenType.IDENTIFIER, token.Type));
        Assert.Equal("device_address", result.Tokens[0].Lexeme);
        Assert.Equal("_internal", result.Tokens[2].Lexeme);
    }
    
    [Fact]
    public void Lexer_DecimalNumber_ParsesCorrectly()
    {
        // Arrange
        var source = "0 123 456789";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal(TokenType.NUMBER, result.Tokens[0].Type);
        Assert.Equal(0L, result.Tokens[0].Literal);
        Assert.Equal(123L, result.Tokens[1].Literal);
        Assert.Equal(456789L, result.Tokens[2].Literal);
    }
    
    [Fact]
    public void Lexer_HexNumber_ParsesCorrectly()
    {
        // Arrange
        var source = "0x00 0xFF 0xDEADBEEF";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal(TokenType.NUMBER, result.Tokens[0].Type);
        Assert.Equal(0L, result.Tokens[0].Literal);
        Assert.Equal(255L, result.Tokens[1].Literal);
        Assert.Equal(0xDEADBEEFL, result.Tokens[2].Literal);
    }
    
    [Fact]
    public void Lexer_BinaryNumber_ParsesCorrectly()
    {
        // Arrange
        var source = "0b0 0b1010 0b11111111";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal(0L, result.Tokens[0].Literal);
        Assert.Equal(10L, result.Tokens[1].Literal);
        Assert.Equal(255L, result.Tokens[2].Literal);
    }
    
    [Fact]
    public void Lexer_OctalNumber_ParsesCorrectly()
    {
        // Arrange
        var source = "0o0 0o10 0o755";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal(0L, result.Tokens[0].Literal);
        Assert.Equal(8L, result.Tokens[1].Literal);
        Assert.Equal(493L, result.Tokens[2].Literal); // 0o755 = 493
    }
    
    [Fact]
    public void Lexer_FloatNumber_ParsesCorrectly()
    {
        // Arrange
        var source = "3.14 0.5 1.5e-10 2.5E+3";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.Tokens.Count);
        Assert.Equal(TokenType.FLOAT, result.Tokens[0].Type);
        Assert.Equal(3.14, result.Tokens[0].Literal);
        Assert.Equal(0.5, result.Tokens[1].Literal);
        Assert.Equal(1.5e-10, result.Tokens[2].Literal);
        Assert.Equal(2500.0, result.Tokens[3].Literal);
    }
    
    [Fact]
    public void Lexer_String_ParsesSimpleString()
    {
        // Arrange
        var source = "\"Hello World\"";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.STRING_LITERAL, result.Tokens[0].Type);
        Assert.Equal("Hello World", result.Tokens[0].Literal);
    }
    
    [Fact]
    public void Lexer_String_ParsesEscapeSequences()
    {
        // Arrange
        var source = "\"Line1\\nLine2\\tTabbed\\\\Backslash\\\"Quote\"";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("Line1\nLine2\tTabbed\\Backslash\"Quote", result.Tokens[0].Literal);
    }
    
    [Fact]
    public void Lexer_String_ParsesMultiLineString()
    {
        // Arrange
        var source = "\"First line\nSecond line\nThird line\"";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("First line\nSecond line\nThird line", result.Tokens[0].Literal);
        Assert.Equal(3, result.Tokens[0].Line); // 字符串结束在第3行
    }
    
    [Fact]
    public void Lexer_Char_ParsesSimpleChar()
    {
        // Arrange
        var source = "'A' 'Z' '0'";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal(TokenType.CHAR_LITERAL, result.Tokens[0].Type);
        Assert.Equal('A', result.Tokens[0].Literal);
        Assert.Equal('Z', result.Tokens[1].Literal);
    }
    
    [Fact]
    public void Lexer_Char_ParsesEscapedChar()
    {
        // Arrange
        var source = "'\\n' '\\t' '\\\\' '\\''";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.Tokens.Count);
        Assert.Equal('\n', result.Tokens[0].Literal);
        Assert.Equal('\t', result.Tokens[1].Literal);
        Assert.Equal('\\', result.Tokens[2].Literal);
        Assert.Equal('\'', result.Tokens[3].Literal);
    }
    
    [Fact]
    public void Lexer_SingleLineComment_Ignored()
    {
        // Arrange
        var source = @"
            protocol Test {
                // This is a comment
                message Foo { }
            }
        ";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        // 应该只有: protocol Test { message Foo { } } EOF
        var types = result.Tokens.Select(t => t.Type).ToList();
        Assert.DoesNotContain(TokenType.INVALID, types);
        Assert.Contains(TokenType.PROTOCOL, types);
        Assert.Contains(TokenType.MESSAGE, types);
    }
    
    [Fact]
    public void Lexer_MultiLineComment_Ignored()
    {
        // Arrange
        var source = @"
            protocol Test {
                /* This is a 
                   multi-line
                   comment */
                message Foo { }
            }
        ";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        var types = result.Tokens.Select(t => t.Type).ToList();
        Assert.DoesNotContain(TokenType.INVALID, types);
    }
    
    [Fact]
    public void Lexer_NestedComment_HandlesCorrectly()
    {
        // Arrange
        var source = @"
            /* Outer comment
                /* Inner comment */
            Still in outer */
            message Foo { }
        ";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.MESSAGE);
    }
    
    [Fact]
    public void Lexer_LineAndColumn_TrackedCorrectly()
    {
        // Arrange
        var source = @"protocol Test {
    message Foo {
        uint8 field;
    }
}";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        
        // 检查第一行
        Assert.Equal(1, result.Tokens[0].Line); // protocol
        Assert.Equal(1, result.Tokens[1].Line); // Test
        
        // 检查第二行
        var messageToken = result.Tokens.First(t => t.Type == TokenType.MESSAGE);
        Assert.Equal(2, messageToken.Line);
        
        // 检查第三行
        var uint8Token = result.Tokens.First(t => t.Type == TokenType.UINT8);
        Assert.Equal(3, uint8Token.Line);
    }
    
    [Fact]
    public void Lexer_ComplexProtocol_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                version ""1.0""
                
                enum FunctionCode {
                    READ_HOLDING_REGISTERS = 0x03,
                    WRITE_SINGLE_REGISTER = 0x06
                }
                
                message Request {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 starting_address endian(big);
                    uint16 quantity endian(big);
                    uint16 crc validate(crc16(this[0..-3]));
                }
            }
        ";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        
        // 验证关键Token存在
        var types = result.Tokens.Select(t => t.Type).ToList();
        Assert.Contains(TokenType.PROTOCOL, types);
        Assert.Contains(TokenType.ENUM, types);
        Assert.Contains(TokenType.MESSAGE, types);
        Assert.Contains(TokenType.UINT8, types);
        Assert.Contains(TokenType.RANGE, types);
        Assert.Contains(TokenType.VALIDATE, types);
    }
    
    [Fact]
    public void Lexer_UnclosedString_ReturnsError()
    {
        // Arrange
        var source = "\"Unclosed string";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("未闭合的字符串", result.Errors[0]);
    }
    
    [Fact]
    public void Lexer_UnclosedComment_ReturnsError()
    {
        // Arrange
        var source = "/* Unclosed comment";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("未闭合的多行注释", result.Errors[0]);
    }
    
    [Fact]
    public void Lexer_InvalidHexNumber_ReturnsError()
    {
        // Arrange
        var source = "0x";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("无效的十六进制数字", result.Errors[0]);
    }
    
    [Fact]
    public void Lexer_InvalidBinaryNumber_ReturnsError()
    {
        // Arrange
        var source = "0b";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("无效的二进制数字", result.Errors[0]);
    }
    
    [Fact]
    public void Lexer_UnexpectedCharacter_ReturnsError()
    {
        // Arrange
        var source = "protocol @Test";
        var lexer = new CpdlLexer(source);
        
        // Act
        var result = lexer.ScanTokens();
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("意外的字符", result.Errors[0]);
    }
}
