using System;
using System.Security.Cryptography;

namespace MagicCampus.UnityAdapters.Storage
{
    /// <summary>
    /// Private on-disk framing of one generation file. The caller's payload is stored verbatim after the header.
    /// Layout (little endian): magic 'MCGF' (4), framing version (int32), generation (int64), payload length (int64),
    /// SHA-256 of payload (32), payload.
    /// </summary>
    internal static class GenerationFraming
    {
        internal const int HeaderSize = 56;
        private const int FramingVersion = 1;
        private static readonly byte[] Magic = { (byte)'M', (byte)'C', (byte)'G', (byte)'F' };

        internal static byte[] Encode(long generation, byte[] payload, out string checksum)
        {
            var hash = Sha256(payload);
            var frame = new byte[HeaderSize + payload.Length];
            Buffer.BlockCopy(Magic, 0, frame, 0, 4);
            WriteInt64(frame, 4, FramingVersion, 4);
            WriteInt64(frame, 8, generation, 8);
            WriteInt64(frame, 16, payload.Length, 8);
            Buffer.BlockCopy(hash, 0, frame, 24, 32);
            Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);
            checksum = Hex(hash);
            return frame;
        }

        /// <summary>Returns null when the frame is complete and consistent, otherwise the reason it is rejected.</summary>
        internal static string TryDecode(byte[] frame, long expectedGeneration, out byte[] payload, out string checksum)
        {
            payload = null;
            checksum = null;
            if (frame == null || frame.Length < HeaderSize) return "file shorter than header";
            for (var i = 0; i < 4; i++)
                if (frame[i] != Magic[i]) return "bad magic";
            if (ReadInt64(frame, 4, 4) != FramingVersion) return "unsupported framing version";
            if (ReadInt64(frame, 8, 8) != expectedGeneration) return "generation mismatch";
            var length = ReadInt64(frame, 16, 8);
            if (length < 0 || length != frame.Length - HeaderSize) return "length mismatch (torn or extended file)";
            var body = new byte[(int)length];
            Buffer.BlockCopy(frame, HeaderSize, body, 0, body.Length);
            var hash = Sha256(body);
            for (var i = 0; i < 32; i++)
                if (frame[24 + i] != hash[i]) return "checksum mismatch";
            payload = body;
            checksum = Hex(hash);
            return null;
        }

        internal static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        private static string Hex(byte[] hash)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[hash.Length * 2];
            for (var i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = digits[hash[i] >> 4];
                chars[i * 2 + 1] = digits[hash[i] & 15];
            }
            return new string(chars);
        }

        private static void WriteInt64(byte[] buffer, int offset, long value, int count)
        {
            for (var i = 0; i < count; i++) buffer[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
        }

        private static long ReadInt64(byte[] buffer, int offset, int count)
        {
            long value = 0;
            for (var i = 0; i < count; i++) value |= (long)buffer[offset + i] << (8 * i);
            return value;
        }
    }
}
