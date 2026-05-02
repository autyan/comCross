using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using ComCross.Core.Cpdl.Compiler;

namespace ComCross.Tests.Core.Cpdl;

public class CpdlCompilerTests
{
    private CpdlCompiler CreateCompiler()
    {
        return new CpdlCompiler(
            NullLogger<CpdlCompiler>.Instance,
            NullLogger<CpdlMessageParser>.Instance
        );
    }

    [Fact]
    public void Compiler_SimpleMessage_Compiles()
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

        var compiler = CreateCompiler();

        // Act
        var result = compiler.Compile(source);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage ?? "");
        Assert.NotNull(result.Parser);
        Assert.Equal("cpdl-test", result.Parser.Id);
        Assert.Equal("Test", result.Parser.Name);
    }

    [Fact]
    public void Compiler_ParsesData_Successfully()
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

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;
        var data = new byte[] { 0x01, 0x03 };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(2, parseResult.Fields.Count);
        Assert.Equal((byte)0x01, parseResult.Fields["device_address"]);
        Assert.Equal((byte)0x03, parseResult.Fields["function_code"]);
    }

    [Fact]
    public void Compiler_UInt16BigEndian_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint16 value endian(big);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess, compResult.ErrorMessage ?? "");

        var parser = compResult.Parser!;
        var data = new byte[] { 0x01, 0x02 }; // Big endian: 0x0102 = 258

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess);
        Assert.Equal((ushort)258, parseResult.Fields["value"]);
    }

    [Fact]
    public void Compiler_UInt16LittleEndian_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint16 value endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;
        var data = new byte[] { 0x01, 0x02 }; // Little endian: 0x0201 = 513

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess);
        Assert.Equal((ushort)513, parseResult.Fields["value"]);
    }

    [Fact]
    public void Compiler_RangeModifier_ValidatesRange()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 device_address range(1, 247);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Act & Assert - Valid value
        var validData = new byte[] { 100 };
        var validResult = parser.Parse(validData);
        Assert.True(validResult.IsSuccess, $"Valid value should pass. Error: {validResult.ErrorMessage}");

        // Act & Assert - Invalid value (out of range)
        var invalidData = new byte[] { 0 };
        var invalidResult = parser.Parse(invalidData);
        Assert.False(invalidResult.IsSuccess, "Invalid value should fail");
        Assert.Contains("out of range", invalidResult.ErrorMessage ?? "");
    }

    [Fact]
    public void Compiler_MultipleFields_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 device_address;
                    uint8 function_code;
                    uint16 starting_address endian(big);
                    uint16 quantity endian(big);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;
        var data = new byte[] { 0x01, 0x03, 0x00, 0x64, 0x00, 0x0A };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess);
        Assert.Equal(4, parseResult.Fields.Count);
        Assert.Equal((byte)0x01, parseResult.Fields["device_address"]);
        Assert.Equal((byte)0x03, parseResult.Fields["function_code"]);
        Assert.Equal((ushort)100, parseResult.Fields["starting_address"]);
        Assert.Equal((ushort)10, parseResult.Fields["quantity"]);
    }

    [Fact]
    public void Compiler_InvalidSyntax_ReturnsError()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    invalid_type field;
                }
            }
        ";

        var compiler = CreateCompiler();

        // Act
        var result = compiler.Compile(source);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Compiler_SemanticError_ReturnsError()
    {
        // Arrange
        var source = @"
            protocol Test {
                message Request {
                    uint8 field range(a, b);
                }
            }
        ";

        var compiler = CreateCompiler();

        // Act
        var result = compiler.Compile(source);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown identifier", result.ErrorMessage ?? "");  // 代码生成阶段发现标识符错误
    }
}
