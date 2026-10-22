using System;
using System.Buffers.Binary;

namespace NHSE.Core;

/// <summary>
/// AES-128 counter mode with a managed block cipher, replacing upstream's
/// <c>Encryption/AesCtr.cs</c> because System.Security.Cryptography.Aes is not available
/// on WebAssembly. Same internal API as upstream.
/// </summary>
internal static class AesCtr
{
    private const int BlockSize = 16;
    private const int Rounds = 10;

    private static readonly byte[] SBox = new byte[256];
    private static readonly uint[] Te0 = new uint[256];
    private static readonly uint[] Te1 = new uint[256];
    private static readonly uint[] Te2 = new uint[256];
    private static readonly uint[] Te3 = new uint[256];

    static AesCtr()
    {
        // Build the S-box from the multiplicative inverse in GF(2^8) plus the affine map.
        byte p = 1, q = 1;
        do
        {
            p = (byte)(p ^ (p << 1) ^ ((p & 0x80) != 0 ? 0x1B : 0));
            q ^= (byte)(q << 1);
            q ^= (byte)(q << 2);
            q ^= (byte)(q << 4);
            if ((q & 0x80) != 0)
                q ^= 0x09;
            byte x = (byte)(q ^ Rotl8(q, 1) ^ Rotl8(q, 2) ^ Rotl8(q, 3) ^ Rotl8(q, 4));
            SBox[p] = (byte)(x ^ 0x63);
        } while (p != 1);
        SBox[0] = 0x63;

        for (int i = 0; i < 256; i++)
        {
            uint s = SBox[i];
            uint s2 = Mul2(s);
            uint s3 = s2 ^ s;
            uint t = (s2 << 24) | (s << 16) | (s << 8) | s3;
            Te0[i] = t;
            Te1[i] = (t >> 8) | (t << 24);
            Te2[i] = (t >> 16) | (t << 16);
            Te3[i] = (t >> 24) | (t << 8);
        }
    }

    private static byte Rotl8(byte v, int s) => (byte)((v << s) | (v >> (8 - s)));

    private static uint Mul2(uint v) => ((v << 1) ^ ((v & 0x80) != 0 ? 0x1Bu : 0u)) & 0xFF;

    private static uint SubWord(uint w) =>
        ((uint)SBox[w >> 24] << 24) | ((uint)SBox[(w >> 16) & 0xFF] << 16) |
        ((uint)SBox[(w >> 8) & 0xFF] << 8) | SBox[w & 0xFF];

    private static uint[] ExpandKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != 16)
            throw new ArgumentException("AES-128 needs a 16 byte key.", nameof(key));

        var rk = new uint[4 * (Rounds + 1)];
        for (int i = 0; i < 4; i++)
            rk[i] = BinaryPrimitives.ReadUInt32BigEndian(key[(4 * i)..]);

        uint rcon = 1;
        for (int i = 4; i < rk.Length; i++)
        {
            uint temp = rk[i - 1];
            if (i % 4 == 0)
            {
                temp = SubWord((temp << 8) | (temp >> 24)) ^ (rcon << 24);
                rcon = Mul2(rcon);
            }
            rk[i] = rk[i - 4] ^ temp;
        }
        return rk;
    }

    private static void EncryptBlock(uint[] rk, ReadOnlySpan<byte> input, Span<byte> output)
    {
        uint s0 = BinaryPrimitives.ReadUInt32BigEndian(input) ^ rk[0];
        uint s1 = BinaryPrimitives.ReadUInt32BigEndian(input[4..]) ^ rk[1];
        uint s2 = BinaryPrimitives.ReadUInt32BigEndian(input[8..]) ^ rk[2];
        uint s3 = BinaryPrimitives.ReadUInt32BigEndian(input[12..]) ^ rk[3];

        int k = 4;
        for (int round = 1; round < Rounds; round++, k += 4)
        {
            uint t0 = Te0[s0 >> 24] ^ Te1[(s1 >> 16) & 0xFF] ^ Te2[(s2 >> 8) & 0xFF] ^ Te3[s3 & 0xFF] ^ rk[k];
            uint t1 = Te0[s1 >> 24] ^ Te1[(s2 >> 16) & 0xFF] ^ Te2[(s3 >> 8) & 0xFF] ^ Te3[s0 & 0xFF] ^ rk[k + 1];
            uint t2 = Te0[s2 >> 24] ^ Te1[(s3 >> 16) & 0xFF] ^ Te2[(s0 >> 8) & 0xFF] ^ Te3[s1 & 0xFF] ^ rk[k + 2];
            uint t3 = Te0[s3 >> 24] ^ Te1[(s0 >> 16) & 0xFF] ^ Te2[(s1 >> 8) & 0xFF] ^ Te3[s2 & 0xFF] ^ rk[k + 3];
            s0 = t0; s1 = t1; s2 = t2; s3 = t3;
        }

        // final round: SubBytes, ShiftRows, AddRoundKey (no MixColumns)
        uint o0 = Last(s0, s1, s2, s3) ^ rk[k];
        uint o1 = Last(s1, s2, s3, s0) ^ rk[k + 1];
        uint o2 = Last(s2, s3, s0, s1) ^ rk[k + 2];
        uint o3 = Last(s3, s0, s1, s2) ^ rk[k + 3];
        BinaryPrimitives.WriteUInt32BigEndian(output, o0);
        BinaryPrimitives.WriteUInt32BigEndian(output[4..], o1);
        BinaryPrimitives.WriteUInt32BigEndian(output[8..], o2);
        BinaryPrimitives.WriteUInt32BigEndian(output[12..], o3);
    }

    private static uint Last(uint a, uint b, uint c, uint d) =>
        ((uint)SBox[a >> 24] << 24) | ((uint)SBox[(b >> 16) & 0xFF] << 16) |
        ((uint)SBox[(c >> 8) & 0xFF] << 8) | SBox[d & 0xFF];

    /// <summary>
    /// Encrypts or decrypts data in-place using AES-CTR mode.
    /// </summary>
    /// <param name="data">Data to transform in-place.</param>
    /// <param name="key">16-byte AES key.</param>
    /// <param name="counter">16-byte initial counter value (will be modified).</param>
    public static void Crypt(Span<byte> data, ReadOnlySpan<byte> key, Span<byte> counter)
    {
        var rk = ExpandKey(key);
        Span<byte> keystream = stackalloc byte[BlockSize];

        for (int offset = 0; offset < data.Length; offset += BlockSize)
        {
            EncryptBlock(rk, counter, keystream);

            int blockLength = Math.Min(BlockSize, data.Length - offset);
            var dataBlock = data.Slice(offset, blockLength);
            for (int i = 0; i < blockLength; i++)
                dataBlock[i] ^= keystream[i];

            IncrementCounter(counter);
        }
    }

    /// <summary>
    /// Encrypts data using AES-CTR mode, returning a new buffer.
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ReadOnlySpan<byte> counter)
    {
        var result = new byte[data.Length];
        data.CopyTo(result);

        Span<byte> counterCopy = stackalloc byte[BlockSize];
        counter.CopyTo(counterCopy);

        Crypt(result, key, counterCopy);
        return result;
    }

    private static void IncrementCounter(Span<byte> counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }
}
