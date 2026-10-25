using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// Pictures of designs, and turning an uploaded picture into a normal design
/// (15 colours plus transparency on 32 x 32 pixels).
/// </summary>
public static class DesignImage
{
    // UsageCompatibility values NHSE knows: normal designs 0xEE00 (plain) and 0xEE02 (with
    // transparent pixels), 0xEE04 for the untouched default; PRO designs 0xEE01, default 0xEE05.
    public const uint Plain = 0xEE00;
    public const uint WithTransparency = 0xEE02;
    public const uint DefaultNormal = 0xEE04;
    public const uint Pro = 0xEE01;
    public const uint DefaultPro = 0xEE05;

    public static byte[] Rgba(DesignPattern design) => ToRgba(design.GetBitmap());

    public static byte[] Rgba(DesignPatternPRO design, int sheet) => ToRgba(design.GetBitmap(sheet));

    // NHSE builds BGRA pixels for System.Drawing; PNG and the canvas want RGBA
    private static byte[] ToRgba(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
            (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        return bgra;
    }

    public static string Preview(DesignPattern design, int scale = 2) =>
        Png.DataUrl(Rgba(design), DesignPattern.Width, DesignPattern.Height, scale);

    public static string Preview(DesignPatternPRO design, int sheet, int scale = 2) =>
        Png.DataUrl(Rgba(design, sheet), DesignPatternPRO.Width, DesignPatternPRO.Height, scale);

    /// <summary>
    /// Replaces the pixels and colours of a normal design with a 32 x 32 RGBA picture: pixels with less
    /// than half opacity become transparent, the rest is reduced to 15 colours (median cut, then refined).
    /// The design gets the name, the owner's player and island, and a fresh hash.
    /// </summary>
    public static void Apply(DesignPattern design, ReadOnlySpan<byte> rgba, string name, Personal owner)
    {
        const int count = DesignPattern.Width * DesignPattern.Height;
        var opaque = new List<(int R, int G, int B)>(count);
        for (int i = 0; i < count; i++)
        {
            if (rgba[(i * 4) + 3] >= 128)
                opaque.Add((rgba[i * 4], rgba[(i * 4) + 1], rgba[(i * 4) + 2]));
        }

        var palette = Palette(opaque, DesignPattern.PaletteColorCount);
        for (int c = 0; c < DesignPattern.PaletteColorCount; c++)
        {
            var (r, g, b) = c < palette.Count ? palette[c] : (255, 255, 255);
            var offset = DesignPattern.GetColorOffset(c);
            design.Data[offset] = (byte)r;
            design.Data[offset + 1] = (byte)g;
            design.Data[offset + 2] = (byte)b;
        }

        var transparent = false;
        for (int i = 0; i < count; i++)
        {
            if (rgba[(i * 4) + 3] < 128)
            {
                design[i] = DesignPattern.PaletteColorCount; // index 15 is see-through
                transparent = true;
                continue;
            }
            design[i] = Nearest(palette, (rgba[i * 4], rgba[(i * 4) + 1], rgba[(i * 4) + 2]));
        }

        design.DesignName = name;
        design.UsageCompatibility = transparent ? WithTransparency : Plain;
        design.Hash = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        Own(design, owner);
    }

    /// <summary>Marks a design as made by the player on their island, so they can edit it in the game.</summary>
    public static void Own(IVillagerOrigin design, Personal owner)
    {
        owner.GetTownIdentity().CopyTo(design.GetTownIdentity());
        owner.GetPlayerIdentity().CopyTo(design.GetPlayerIdentity());
    }

    private static List<(int R, int G, int B)> Palette(List<(int R, int G, int B)> pixels, int size)
    {
        var distinct = pixels.Distinct().ToList();
        if (distinct.Count <= size)
            return distinct;

        // median cut: split the box with the widest channel range at its median until there are enough boxes
        var boxes = new List<List<(int R, int G, int B)>> { pixels };
        while (boxes.Count < size)
        {
            var widest = boxes.Where(b => b.Distinct().Count() > 1).OrderByDescending(Range).FirstOrDefault();
            if (widest is null)
                break;
            boxes.Remove(widest);
            var channel = Channel(widest);
            var sorted = widest.OrderBy(p => channel == 0 ? p.R : channel == 1 ? p.G : p.B).ToList();
            var half = sorted.Count / 2;
            boxes.Add(sorted[..half]);
            boxes.Add(sorted[half..]);
        }
        var palette = boxes.Select(Average).ToList();

        // a few k-means rounds pull the colours to the middle of the pixels they stand for
        for (int round = 0; round < 4; round++)
        {
            var groups = pixels.GroupBy(p => Nearest(palette, p)).ToDictionary(g => g.Key, g => g.ToList());
            for (int i = 0; i < palette.Count; i++)
            {
                if (groups.TryGetValue(i, out var members))
                    palette[i] = Average(members);
            }
        }
        return palette;
    }

    private static int Range(List<(int R, int G, int B)> box) =>
        Math.Max(box.Max(p => p.R) - box.Min(p => p.R), Math.Max(box.Max(p => p.G) - box.Min(p => p.G), box.Max(p => p.B) - box.Min(p => p.B)));

    private static int Channel(List<(int R, int G, int B)> box)
    {
        var r = box.Max(p => p.R) - box.Min(p => p.R);
        var g = box.Max(p => p.G) - box.Min(p => p.G);
        var b = box.Max(p => p.B) - box.Min(p => p.B);
        return r >= g && r >= b ? 0 : g >= b ? 1 : 2;
    }

    private static (int R, int G, int B) Average(List<(int R, int G, int B)> box) =>
        ((int)box.Average(p => p.R), (int)box.Average(p => p.G), (int)box.Average(p => p.B));

    private static int Nearest(List<(int R, int G, int B)> palette, (int R, int G, int B) p)
    {
        int best = 0, bestDistance = int.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            var (r, g, b) = palette[i];
            // weighted, close to how the eye sees differences
            var d = (2 * (r - p.R) * (r - p.R)) + (4 * (g - p.G) * (g - p.G)) + (3 * (b - p.B) * (b - p.B));
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }
}
