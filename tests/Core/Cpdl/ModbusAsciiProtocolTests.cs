using Xunit;
using ComCross.Core.Cpdl.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Tests.Core.Cpdl;

/// <summary>
/// Modbus-ASCII协议测试
/// </summary>
public class ModbusAsciiProtocolTests
{
    private CpdlCompiler CreateCompiler()
    {
        return new CpdlCompiler(
            NullLogger<CpdlCompiler>.Instance,
            NullLogger<CpdlMessageParser>.Instance
        );
    }

    [Fact]
    public void ModbusAscii_ReadHoldingRegistersRequest_Compiles()
    {
        // Arrange
        var source = @"
            protocol ModbusASCII {
                message ReadHoldingRegistersRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big) range(1, 125);
                    uint8 lrc;
                }
            }
        ";

        var compiler = CreateCompiler();

        // Act
        var result = compiler.Compile(source);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage ?? "");
        Assert.NotNull(result.Parser);
        Assert.Equal("cpdl-modbusascii", result.Parser!.Id);
    }

    [Fact]
    public void ModbusAscii_LRC_CalculatesCorrectly()
    {
        // Arrange - 测试LRC校验算法
        // 数据: 01 03 00 00 00 02
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 };

        // Act
        var lrc = ChecksumHelper.LrcModbus(data);

        // Assert
        // LRC = -(01 + 03 + 00 + 00 + 00 + 02) & 0xFF = -06 & 0xFF = 0xFA
        Assert.Equal((byte)0xFA, lrc);
    }

    [Fact]
    public void ModbusAscii_ReadHoldingRegistersRequest_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusASCII {
                message ReadHoldingRegistersRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big) range(1, 125);
                    uint8 lrc;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Modbus ASCII读保持寄存器请求(解码后的二进制)
        // ASCII: :010300000002FA\r\n
        // Binary: 01 03 00 00 00 02 FA
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02, 0xFA };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(5, parseResult.Fields.Count);
        Assert.Equal((byte)1, parseResult.Fields["device_address"]);
        Assert.Equal((byte)3, parseResult.Fields["function_code"]);
        Assert.Equal((ushort)0, parseResult.Fields["start_address"]);
        Assert.Equal((ushort)2, parseResult.Fields["register_count"]);
        Assert.Equal((byte)0xFA, parseResult.Fields["lrc"]);
    }

    [Fact]
    public void ModbusAscii_WriteSingleRegisterRequest_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusASCII {
                message WriteSingleRegisterRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 register_address endian(big);
                    uint16 register_value endian(big);
                    uint8 lrc;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Modbus ASCII写单个寄存器请求(解码后)
        // 设备01, 功能码06, 寄存器地址0001, 值03E8 (1000)
        // 01 06 00 01 03 E8
        // LRC = -(01+06+00+01+03+E8) & 0xFF = -F3 & 0xFF = 0x0D
        var data = new byte[] { 0x01, 0x06, 0x00, 0x01, 0x03, 0xE8, 0x0D };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal((byte)1, parseResult.Fields["device_address"]);
        Assert.Equal((byte)6, parseResult.Fields["function_code"]);
        Assert.Equal((ushort)1, parseResult.Fields["register_address"]);
        Assert.Equal((ushort)1000, parseResult.Fields["register_value"]);
        Assert.Equal((byte)0x0D, parseResult.Fields["lrc"]);
    }

    [Fact]
    public void ModbusAscii_ExceptionResponse_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusASCII {
                message ExceptionResponse {
                    uint8 device_address range(1, 247);
                    uint8 function_code range(128, 255);
                    uint8 exception_code range(1, 11);
                    uint8 lrc;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Modbus ASCII异常响应(解码后)
        // 设备01, 异常功能码83 (0x03 + 0x80), 异常码02
        // 01 83 02
        // LRC = -(01+83+02) & 0xFF = -86 & 0xFF = 0x7A
        var data = new byte[] { 0x01, 0x83, 0x02, 0x7A };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal((byte)1, parseResult.Fields["device_address"]);
        Assert.Equal((byte)0x83, parseResult.Fields["function_code"]);
        Assert.Equal((byte)2, parseResult.Fields["exception_code"]);
        Assert.Equal((byte)0x7A, parseResult.Fields["lrc"]);
    }

    [Fact]
    public void ModbusAscii_DeviceAddressRange_Validates()
    {
        // Arrange
        var source = @"
            protocol ModbusASCII {
                message ReadHoldingRegistersRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big);
                    uint8 lrc;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // 设备地址为0（无效）
        var invalidData = new byte[] { 0x00, 0x03, 0x00, 0x00, 0x00, 0x02, 0xFB };

        // Act
        var parseResult = parser.Parse(invalidData);

        // Assert
        Assert.False(parseResult.IsSuccess);
        Assert.Contains("out of range", parseResult.ErrorMessage ?? "");
    }

    [Fact]
    public void ModbusAscii_RegisterCountRange_Validates()
    {
        // Arrange
        var source = @"
            protocol ModbusASCII {
                message ReadHoldingRegistersRequest {
                    uint8 device_address;
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big) range(1, 125);
                    uint8 lrc;
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // 寄存器数量为126（超出范围）
        var invalidData = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x7E, 0x00 };

        // Act
        var parseResult = parser.Parse(invalidData);

        // Assert
        Assert.False(parseResult.IsSuccess);
        Assert.Contains("out of range", parseResult.ErrorMessage ?? "");
    }
}
