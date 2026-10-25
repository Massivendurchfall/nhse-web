using System;

namespace NHSE.Core;

/// <summary>
/// Converts the one-byte flags of the patched structures to bool arrays and back.
/// </summary>
internal static class BoolBytes
{
    public static bool[] Get(byte[]? flags) => flags is null ? [] : Array.ConvertAll(flags, b => b != 0);

    public static byte[] Set(bool[] flags) => Array.ConvertAll(flags, b => (byte)(b ? 1 : 0));
}
