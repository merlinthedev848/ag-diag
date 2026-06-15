using System;

namespace AgilicoConnectChecker
{
    public static class Crc32
    {
        private static readonly uint[] Table;

        static Crc32()
        {
            const uint polynomial = 0xEDB88320;
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ polynomial;
                    else
                        entry >>= 1;
                }
                Table[i] = entry;
            }
        }

        public static uint Compute(byte[] buffer)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < buffer.Length; i++)
            {
                byte index = (byte)((crc & 0xff) ^ buffer[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }

        public static string ComputeHex(byte[] buffer)
        {
            return Compute(buffer).ToString("X8");
        }
    }
}
