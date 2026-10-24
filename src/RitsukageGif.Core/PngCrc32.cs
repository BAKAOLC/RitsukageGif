using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    internal static class PngCrc32
    {
        private static readonly uint[] CrcTable = CreateCrcTable();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            // Process eight bytes per iteration with the same PNG CRC-32 polynomial.
            while (data.Length >= 8)
            {
                crc ^= BinaryPrimitives.ReadUInt32LittleEndian(data);
                crc = CrcTable[1792 + (byte)crc] ^ CrcTable[1536 + (byte)(crc >> 8)] ^
                      CrcTable[1280 + (byte)(crc >> 16)] ^ CrcTable[1024 + (byte)(crc >> 24)] ^
                      CrcTable[768 + data[4]] ^ CrcTable[512 + data[5]] ^ CrcTable[256 + data[6]] ^ CrcTable[data[7]];
                data = data[8..];
            }

            foreach (var value in data) crc = (crc >> 8) ^ CrcTable[(byte)(crc ^ value)];
            return crc;
        }

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256 * 8];
            for (uint i = 0; i < 256; i++)
            {
                var crc = i;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
                table[i] = crc;
            }

            for (var slice = 1; slice < 8; slice++)
            for (var i = 0; i < 256; i++)
            {
                var previous = table[(slice - 1) * 256 + i];
                table[slice * 256 + i] = (previous >> 8) ^ table[(byte)previous];
            }

            return table;
        }
    }
}
