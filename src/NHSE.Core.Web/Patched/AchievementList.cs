// Patched copy of external/NHSE/NHSE.Core/Structures/Misc/AchievementList.cs for the browser.
// The WebAssembly runtime ignores ArraySubType = UnmanagedType.I1 and marshals every bool of a
// bool[] as 4 bytes, so writing these structures back overflowed into the data behind them.
// The flags are stored as one byte each instead; the public bool[] properties stay the same.
using System;
using System.Runtime.InteropServices;

namespace NHSE.Core;

/// <summary>
/// Custom structure to wrap logic for editing all <see cref="LifeSupportAchievement"/> milestone data.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AchievementList
{
    /*
        u32        CountAchievement[512];
        bool       ReadAchievement[512][6];
        GSaveDate  DateAchievement[512][6];
        _44c6787c  NewAchievement[512];
     */

    public const int MaxCount = 512;
    public const int SIZE = MaxCount * (4 + (6 * 1) + (6 * 4) + 1);

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public uint[] Counts { get; set; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public AchievementReadSet[] Read { get; set; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public AchievementDateSet[] Date { get; set; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public byte[] Flags { get; set; }

    public readonly void GiveAll(DateTime t)
    {
        for (int i = 0; i < Counts.Length; i++)
            GiveAll(i, t);
    }

    public readonly void GiveAll(in int index, DateTime t)
    {
        if (!LifeSupportAchievement.List.TryGetValue(index, out var detail))
        {
            ClearAll(index);
            return;
        }

        var max = detail.MaxThreshold;
        if (max != 0)
            Counts[index] = Math.Max(Counts[index], max);
        Flags[index] = 1; // visible

        for (int i = 0; i < detail.AchievementCount; i++)
        {
            if (Read[index][i])
                continue;
            if (!Date[index][i].IsEmpty)
                continue;

            Date[index][i] = t;
            Read[index][i] = true;
            t = t.AddDays(1);
        }
    }

    public readonly void ClearAll()
    {
        for (int i = 0; i < Counts.Length; i++)
            ClearAll(i);
    }

    public readonly void ClearAll(int index)
    {
        Read[index].Clear();
        Date[index].Clear();
        Flags[index] = 0;
        Counts[index] = 0;
    }
}

public struct AchievementReadSet
{
    public const int MaxCount = 6;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    private byte[] _Read;
    public bool[] Read { readonly get => BoolBytes.Get(_Read); set => _Read = BoolBytes.Set(value); }

    public bool this[int index]
    {
        readonly get => _Read[index] != 0;
        set => _Read[index] = (byte)(value ? 1 : 0);
    }

    public void Clear() => Array.Clear(_Read);
}

public struct AchievementDateSet
{
    public const int MaxCount = 6;

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCount)]
    public GSaveDate[] Dates { get; set; }

    public GSaveDate this[int index]
    {
        get => Dates[index];
        set => Dates[index] = value;
    }

    public void Clear()
    {
        for (int i = 0; i < Dates.Length; i++)
            Dates[i] = new GSaveDate();
    }
}