using System;

namespace XycloToothBridge.Protocol;

public static class Crc32
{
    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Table = new uint[256];

    static Crc32()
    {
        for (uint i = 0; i < 256; i++)
        {
            uint entry = i;
            for (int j = 0; j < 8; j++)
            {
                if ((entry & 1) == 1)
                    entry = (entry >> 1) ^ Polynomial;
                else
                    entry >>= 1;
            }
            Table[i] = entry;
        }
    }

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte index = (byte)(((crc) & 0xFF) ^ bytes[i]);
            crc = (crc >> 8) ^ Table[index];
        }
        return ~crc;
    }
}
