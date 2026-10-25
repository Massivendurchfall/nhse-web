// Patched copy of external/NHSE/NHSE.Core/Structures/Misc/GSavePlayerManpu.cs for the browser.
// The WebAssembly runtime ignores ArraySubType = UnmanagedType.I1 and marshals every bool of a
// bool[] as 4 bytes, so writing these structures back overflowed into the data behind them.
// The flags are stored as one byte each instead; the public bool[] properties stay the same.
using System;
using System.Runtime.InteropServices;

namespace NHSE.Core;

/// <summary>
/// Original Reactions object from release
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GSavePlayerManpu : IReactionStore
{
    public const int SIZE = 0x88;
    private const int MaxCount = 64;
    private const int WheelCount = 8;

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public Reaction[] ManpuBit { get; set; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = WheelCount)]
    public Reaction[] UIList { get; set; }

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    private byte[] _NewFlag;
    public bool[] NewFlag { readonly get => BoolBytes.Get(_NewFlag); set => _NewFlag = BoolBytes.Set(value); }
}

/// <summary>
/// Reactions object revised in v1.5.0
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GSavePlayerManpu15 : IReactionStore
{
    public const int SIZE = 0x208;
    private const int MaxCount = 256; // up from 64
    private const int WheelCount = 8;

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public Reaction[] ManpuBit { get; set; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = WheelCount)]
    public Reaction[] UIList { get; set; }

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    private byte[] _NewFlag;
    public bool[] NewFlag { readonly get => BoolBytes.Get(_NewFlag); set => _NewFlag = BoolBytes.Set(value); }
}

public interface IReactionStore
{
    /// <summary>
    /// List of Reaction IDs the player currently knows.
    /// </summary>
    Reaction[] ManpuBit { get; set; }

    /// <summary>
    /// Emotions that are currently bound to the Reaction Wheel.
    /// </summary>
    Reaction[] UIList { get; set; }

    /// <summary>
    /// Flags indicating if a Reaction (at the same index?) is newly learned or not.
    /// </summary>
    bool[] NewFlag { get; set; }

    /// <summary>
    /// Adds all possible reaction values from <see cref="Reaction"/>'s defined list.
    /// </summary>
    void AddMissingReactions()
    {
        var all = Enum.GetValues<Reaction>();
        foreach (var react in all)
            TryAddReaction(react);
    }

    /// <summary>
    /// Attempts to add the <see cref="react"/> to the list of reactions.
    /// </summary>
    /// <param name="react">Reaction to add to list</param>
    bool TryAddReaction(Reaction react)
    {
        if (react.ToString().StartsWith("UNUSED"))
            return false; // shouldn't add

        if (ManpuBit.Contains(react))
            return true; // already have

        var empty = EmptyIndex;
        if (empty < 0)
            return true; // full? already have
        ManpuBit[empty] = react;
        return true;
    }

    /// <summary>
    /// First empty index within the array of reactions.
    /// </summary>
    int EmptyIndex => ManpuBit.IndexOf(Reaction.None);
}