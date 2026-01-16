using System;

namespace ComCross.Core.Cpdl.Compiler;

/// <summary>
/// CRC和LRC校验算法工具类
/// </summary>
public static class ChecksumHelper
{
    /// <summary>
    /// 计算Modbus RTU使用的CRC16校验码 (Polynomial: 0xA001, Initial: 0xFFFF)
    /// </summary>
    public static ushort Crc16Modbus(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        ushort crc = 0xFFFF;
        
        foreach (byte b in data)
        {
            crc ^= b;
            
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        
        return crc;
    }

    /// <summary>
    /// 计算Modbus ASCII使用的LRC校验码 (纵向冗余校验)
    /// </summary>
    public static byte LrcModbus(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        int lrc = 0;
        
        foreach (byte b in data)
        {
            lrc += b;
        }
        
        return (byte)((-lrc) & 0xFF);
    }

    /// <summary>
    /// 计算标准CRC32校验码 (IEEE 802.3)
    /// </summary>
    public static uint Crc32(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        uint crc = 0xFFFFFFFF;
        
        foreach (byte b in data)
        {
            crc ^= b;
            
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ 0xEDB88320;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        
        return ~crc;
    }
}
