// Patched copy of external/NHSE/NHSE.Core/Structures/Misc/GSaveVisitorNpc.cs for the browser.
// The WebAssembly runtime ignores ArraySubType = UnmanagedType.I1 and marshals every bool of a
// bool[] as 4 bytes, so writing these structures back overflowed into the data behind them.
// The flags are stored as one byte each instead; the public bool[] properties stay the same.
using System.ComponentModel;
using System.Runtime.InteropServices;

#pragma warning disable CS8618, CA1815, CA1819, IDE1006
namespace NHSE.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public sealed class GSaveVisitorNpc
{
    public const int SIZE = 0x78;
    private const int Days = 7;

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = Days)]
    public VisitorNPC[] VisitorNPC { get; set; }

    public V3f CreatePos { get; set; }
    public V3f _61e631dc { get; set; }

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Days)]
    private byte[] _IsNormalLive;
    public bool[] IsNormalLive { get => BoolBytes.Get(_IsNormalLive); set => _IsNormalLive = BoolBytes.Set(value); }

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Days)]
    private byte[] _IsBirthdayLive;
    public bool[] IsBirthdayLive { get => BoolBytes.Get(_IsBirthdayLive); set => _IsBirthdayLive = BoolBytes.Set(value); }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] gap_42;

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = Days)]
    public VisitorNPC[] InitSchedule { get; set; }

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Days * 2)]
    private byte[] __437619d8;
    public bool[] _437619d8 { get => BoolBytes.Get(__437619d8); set => __437619d8 = BoolBytes.Set(value); }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] gap_6e;

    public int DayWisp {get; set; }
    public int DayCeleste { get; set; }
}

// same as Vector3; not importing package
[TypeConverter(typeof(ValueTypeTypeConverter))]
public struct V3f
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public readonly override string ToString() => $"({X},{Y},{Z})";
}

public enum VisitorNPC
{
    None = 0,
    Gulliver = 1,
    Label = 2,
    Saharah = 3,
    Wisp = 4,
    Mabel = 5,
    CJ = 6,
    Flick = 7,
    Kicks = 8,
    Leif = 9,
    Redd = 10,
    Gullivarrr = 11,
}