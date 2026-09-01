using System;

namespace agilicomsptoolkit
{
    // Compatibility facade for the few call sites that use RandomNumberGenerator statically.
    // It delegates every operation to the BCL implementation; no custom PRNG is introduced.
    internal static class RandomNumberGenerator
    {
        public static int GetInt32(int exclusiveUpperBound) => System.Security.Cryptography.RandomNumberGenerator.GetInt32(exclusiveUpperBound);
        public static int GetInt32(int fromInclusive, int exclusiveUpperBound) => System.Security.Cryptography.RandomNumberGenerator.GetInt32(fromInclusive, exclusiveUpperBound);
        public static void Fill(Span<byte> data) => System.Security.Cryptography.RandomNumberGenerator.Fill(data);
        public static void GetBytes(byte[] data) => System.Security.Cryptography.RandomNumberGenerator.Fill(data);

        public static void Shuffle<T>(Span<T> values)
        {
            for (int i = values.Length - 1; i > 0; i--)
            {
                int j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
    }
}
