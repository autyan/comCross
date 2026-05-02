using Xunit;
using ComCross.Core.Cpdl.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Tests.Core.Cpdl;

/// <summary>
/// AT Commands协议测试
/// </summary>
public class AtCommandsProtocolTests
{
    private CpdlCompiler CreateCompiler()
    {
        return new CpdlCompiler(
            NullLogger<CpdlCompiler>.Instance,
            NullLogger<CpdlMessageParser>.Instance
        );
    }

    [Fact]
    public void AtCommands_BasicCommand_Compiles()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message BasicCommand {
                    uint8 A;
                    uint8 T;
                    uint8 cr;
                }
            }
        ";

        var compiler = CreateCompiler();

        // Act
        var result = compiler.Compile(source);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage ?? "");
        Assert.NotNull(result.Parser);
        Assert.Equal("cpdl-atcommands", result.Parser!.Id);
    }

    [Fact]
    public void AtCommands_BasicCommand_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message BasicCommand {
                    uint8 A;
                    uint8 T;
                    uint8 cr;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // AT命令: "AT\r"
        var data = new byte[] { 0x41, 0x54, 0x0D }; // A, T, CR

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(3, parseResult.Fields.Count);
        Assert.Equal((byte)0x41, parseResult.Fields["A"]); // 'A'
        Assert.Equal((byte)0x54, parseResult.Fields["T"]); // 'T'
        Assert.Equal((byte)0x0D, parseResult.Fields["cr"]); // '\r'
    }

    [Fact]
    public void AtCommands_QueryCommand_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message QueryCommand {
                    uint8 A;
                    uint8 T;
                    uint8 plus;
                    uint8 C;
                    uint8 G;
                    uint8 M;
                    uint8 I;
                    uint8 question;
                    uint8 cr;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // AT命令: "AT+CGMI?\r"
        var data = new byte[] 
        { 
            0x41, 0x54, 0x2B, // AT+
            0x43, 0x47, 0x4D, 0x49, // CGMI
            0x3F, // ?
            0x0D  // \r
        };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(9, parseResult.Fields.Count);
        Assert.Equal((byte)0x41, parseResult.Fields["A"]);  // 'A'
        Assert.Equal((byte)0x54, parseResult.Fields["T"]);  // 'T'
        Assert.Equal((byte)0x2B, parseResult.Fields["plus"]); // '+'
        Assert.Equal((byte)0x43, parseResult.Fields["C"]);  // 'C'
        Assert.Equal((byte)0x47, parseResult.Fields["G"]);  // 'G'
        Assert.Equal((byte)0x4D, parseResult.Fields["M"]);  // 'M'
        Assert.Equal((byte)0x49, parseResult.Fields["I"]);  // 'I'
        Assert.Equal((byte)0x3F, parseResult.Fields["question"]); // '?'
        Assert.Equal((byte)0x0D, parseResult.Fields["cr"]); // '\r'
    }

    [Fact]
    public void AtCommands_OkResponse_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message OkResponse {
                    uint8 O;
                    uint8 K;
                    uint8 cr;
                    uint8 lf;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // AT响应: "OK\r\n"
        var data = new byte[] { 0x4F, 0x4B, 0x0D, 0x0A }; // OK\r\n

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(4, parseResult.Fields.Count);
        Assert.Equal((byte)0x4F, parseResult.Fields["O"]);  // 'O'
        Assert.Equal((byte)0x4B, parseResult.Fields["K"]);  // 'K'
        Assert.Equal((byte)0x0D, parseResult.Fields["cr"]); // '\r'
        Assert.Equal((byte)0x0A, parseResult.Fields["lf"]); // '\n'
    }

    [Fact]
    public void AtCommands_ErrorResponse_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message ErrorResponse {
                    uint8 E;
                    uint8 R;
                    uint8 R2;
                    uint8 O;
                    uint8 R3;
                    uint8 cr;
                    uint8 lf;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // AT响应: "ERROR\r\n"
        var data = new byte[] 
        { 
            0x45, 0x52, 0x52, 0x4F, 0x52, // ERROR
            0x0D, 0x0A // \r\n
        };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(7, parseResult.Fields.Count);
        Assert.Equal((byte)0x45, parseResult.Fields["E"]);   // 'E'
        Assert.Equal((byte)0x52, parseResult.Fields["R"]);   // 'R'
        Assert.Equal((byte)0x52, parseResult.Fields["R2"]);  // 'R'
        Assert.Equal((byte)0x4F, parseResult.Fields["O"]);   // 'O'
        Assert.Equal((byte)0x52, parseResult.Fields["R3"]);  // 'R'
        Assert.Equal((byte)0x0D, parseResult.Fields["cr"]);  // '\r'
        Assert.Equal((byte)0x0A, parseResult.Fields["lf"]);  // '\n'
    }

    [Fact]
    public void AtCommands_DataResponse_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message DataResponse {
                    uint8 plus;
                    uint8 C;
                    uint8 G;
                    uint8 M;
                    uint8 I;
                    uint8 colon;
                    uint8 space;
                    uint8 cr;
                    uint8 lf;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // AT响应: "+CGMI: \r\n" (简化版，实际会有厂商名称)
        var data = new byte[] 
        { 
            0x2B, 0x43, 0x47, 0x4D, 0x49, // +CGMI
            0x3A, 0x20, // : (space)
            0x0D, 0x0A // \r\n
        };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(9, parseResult.Fields.Count);
        Assert.Equal((byte)0x2B, parseResult.Fields["plus"]);  // '+'
        Assert.Equal((byte)0x43, parseResult.Fields["C"]);     // 'C'
        Assert.Equal((byte)0x47, parseResult.Fields["G"]);     // 'G'
        Assert.Equal((byte)0x4D, parseResult.Fields["M"]);     // 'M'
        Assert.Equal((byte)0x49, parseResult.Fields["I"]);     // 'I'
        Assert.Equal((byte)0x3A, parseResult.Fields["colon"]); // ':'
        Assert.Equal((byte)0x20, parseResult.Fields["space"]); // ' '
        Assert.Equal((byte)0x0D, parseResult.Fields["cr"]);    // '\r'
        Assert.Equal((byte)0x0A, parseResult.Fields["lf"]);    // '\n'
    }

    [Fact]
    public void AtCommands_SignalQualityResponse_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ATCommands {
                message SignalQualityResponse {
                    uint8 plus;
                    uint8 C;
                    uint8 S;
                    uint8 Q;
                    uint8 colon;
                    uint8 space;
                    uint8 rssi_tens;
                    uint8 rssi_ones;
                    uint8 comma;
                    uint8 ber;
                    uint8 cr;
                    uint8 lf;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // AT响应: "+CSQ: 24,0\r\n"
        // RSSI = 24 (good signal), BER = 0 (no bit error)
        var data = new byte[] 
        { 
            0x2B, 0x43, 0x53, 0x51, // +CSQ
            0x3A, 0x20, // : (space)
            0x32, 0x34, // 24 (ASCII '2', '4')
            0x2C, // ,
            0x30, // 0 (ASCII '0')
            0x0D, 0x0A // \r\n
        };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(12, parseResult.Fields.Count);
        Assert.Equal((byte)0x2B, parseResult.Fields["plus"]);      // '+'
        Assert.Equal((byte)0x43, parseResult.Fields["C"]);         // 'C'
        Assert.Equal((byte)0x53, parseResult.Fields["S"]);         // 'S'
        Assert.Equal((byte)0x51, parseResult.Fields["Q"]);         // 'Q'
        Assert.Equal((byte)0x3A, parseResult.Fields["colon"]);     // ':'
        Assert.Equal((byte)0x20, parseResult.Fields["space"]);     // ' '
        Assert.Equal((byte)0x32, parseResult.Fields["rssi_tens"]); // '2'
        Assert.Equal((byte)0x34, parseResult.Fields["rssi_ones"]); // '4'
        Assert.Equal((byte)0x2C, parseResult.Fields["comma"]);     // ','
        Assert.Equal((byte)0x30, parseResult.Fields["ber"]);       // '0'
        Assert.Equal((byte)0x0D, parseResult.Fields["cr"]);        // '\r'
        Assert.Equal((byte)0x0A, parseResult.Fields["lf"]);        // '\n'
    }
}
