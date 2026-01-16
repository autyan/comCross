using Xunit;
using ComCross.Core.Cpdl.Compiler;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Tests.Core.Cpdl;

/// <summary>
/// Modbus-RTU协议测试
/// </summary>
public class ModbusRtuProtocolTests
{
    private CpdlCompiler CreateCompiler()
    {
        return new CpdlCompiler(
            NullLogger<CpdlCompiler>.Instance,
            NullLogger<CpdlMessageParser>.Instance
        );
    }

    [Fact]
    public void ModbusRtu_ReadHoldingRegistersRequest_Compiles()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                message ReadHoldingRegistersRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big) range(1, 125);
                    uint16 crc endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();

        // Act
        var result = compiler.Compile(source);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage ?? "");
        Assert.NotNull(result.Parser);
        Assert.Equal("cpdl-modbusrtu", result.Parser!.Id);
    }

    [Fact]
    public void ModbusRtu_ReadHoldingRegistersRequest_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                message ReadHoldingRegistersRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big) range(1, 125);
                    uint16 crc endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Modbus RTU读保持寄存器请求: 设备01, 功能码03, 起始地址0000, 寄存器数量0002, CRC
        // 01 03 00 00 00 02 C4 0B
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x0B };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal(5, parseResult.Fields.Count);
        Assert.Equal((byte)1, parseResult.Fields["device_address"]);
        Assert.Equal((byte)3, parseResult.Fields["function_code"]);
        Assert.Equal((ushort)0, parseResult.Fields["start_address"]);
        Assert.Equal((ushort)2, parseResult.Fields["register_count"]);
        Assert.Equal((ushort)0x0BC4, parseResult.Fields["crc"]);  // Little endian: 0xC4 0x0B -> 0x0BC4
    }

    [Fact]
    public void ModbusRtu_CRC16_CalculatesCorrectly()
    {
        // Arrange - 测试CRC16校验算法
        // 数据: 01 03 00 00 00 02
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 };

        // Act
        var crc = ChecksumHelper.Crc16Modbus(data);

        // Assert
        // CRC16应该是0x0BC4 (3012) - 在数据中小端序存储为: 0xC4 0x0B
        Assert.Equal((ushort)0x0BC4, crc);
    }

    [Fact]
    public void ModbusRtu_ReadHoldingRegistersRequest_ValidatesDeviceAddress()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                message ReadHoldingRegistersRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big);
                    uint16 crc endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // 设备地址为0（无效）
        var invalidData = new byte[] { 0x00, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x0B };

        // Act
        var parseResult = parser.Parse(invalidData);

        // Assert
        Assert.False(parseResult.IsSuccess);
        Assert.Contains("out of range", parseResult.ErrorMessage ?? "");
    }

    [Fact]
    public void ModbusRtu_WriteSingleRegisterRequest_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                message WriteSingleRegisterRequest {
                    uint8 device_address range(1, 247);
                    uint8 function_code;
                    uint16 register_address endian(big);
                    uint16 register_value endian(big);
                    uint16 crc endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Modbus RTU写单个寄存器请求: 设备01, 功能码06, 寄存器地址0001, 值03E8 (1000), CRC
        // 01 06 00 01 03 E8 9A 9B
        var data = new byte[] { 0x01, 0x06, 0x00, 0x01, 0x03, 0xE8, 0x9A, 0x9B };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal((byte)1, parseResult.Fields["device_address"]);
        Assert.Equal((byte)6, parseResult.Fields["function_code"]);
        Assert.Equal((ushort)1, parseResult.Fields["register_address"]);
        Assert.Equal((ushort)1000, parseResult.Fields["register_value"]);
        Assert.Equal((ushort)0x9B9A, parseResult.Fields["crc"]);
    }

    [Fact]
    public void ModbusRtu_ExceptionResponse_ParsesCorrectly()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                message ExceptionResponse {
                    uint8 device_address range(1, 247);
                    uint8 function_code range(128, 255);
                    uint8 exception_code range(1, 11);
                    uint16 crc endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // Modbus RTU异常响应: 设备01, 异常功能码83 (0x03 + 0x80), 异常码02, CRC
        // 01 83 02 C0 F1
        var data = new byte[] { 0x01, 0x83, 0x02, 0xC0, 0xF1 };

        // Act
        var parseResult = parser.Parse(data);

        // Assert
        Assert.True(parseResult.IsSuccess, parseResult.ErrorMessage ?? "");
        Assert.Equal((byte)1, parseResult.Fields["device_address"]);
        Assert.Equal((byte)0x83, parseResult.Fields["function_code"]);
        Assert.Equal((byte)2, parseResult.Fields["exception_code"]);
        Assert.Equal((ushort)0xF1C0, parseResult.Fields["crc"]);
    }

    [Fact]
    public void ModbusRtu_RegisterCountRange_Validates()
    {
        // Arrange
        var source = @"
            protocol ModbusRTU {
                message ReadHoldingRegistersRequest {
                    uint8 device_address;
                    uint8 function_code;
                    uint16 start_address endian(big);
                    uint16 register_count endian(big) range(1, 125);
                    uint16 crc endian(little);
                }
            }
        ";

        var compiler = CreateCompiler();
        var compResult = compiler.Compile(source);
        Assert.True(compResult.IsSuccess);

        var parser = compResult.Parser!;

        // 寄存器数量为126（超出范围）
        // 01 03 00 00 00 7E (126个寄存器)
        var invalidData = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x7E, 0x00, 0x00 };

        // Act
        var parseResult = parser.Parse(invalidData);

        // Assert
        Assert.False(parseResult.IsSuccess);
        Assert.Contains("out of range", parseResult.ErrorMessage ?? "");
    }
}
