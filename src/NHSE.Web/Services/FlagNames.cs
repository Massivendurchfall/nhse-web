using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// Names for the event flag lists, taken from NHSE's descriptions ("12 - Name = 3"). Flags without a
/// name in NHSE ("???") return null.
/// </summary>
public static class FlagNames
{
    private static string? Strip(string text)
    {
        var start = text.IndexOf(" - ", StringComparison.Ordinal);
        var end = text.LastIndexOf(" = ", StringComparison.Ordinal);
        if (start < 0 || end <= start)
            return null;
        var name = text[(start + 3)..end];
        return name == "???" ? null : name;
    }

    private static IReadOnlyDictionary<string, string> Translation => GameInfo.Strings.InternalNameTranslation;

    public static string? Player(int index) => Strip(EventFlagPlayer.GetName((ushort)index, 0, Translation));
    public static string? Land(int index) => Strip(EventFlagLand.GetName((ushort)index, 0, Translation));
    public static string? House(int index) => Strip(EventFlagHouse.GetName((ushort)index, 0, Translation));
    public static string? Villager(int index) => Strip(EventFlagVillager.GetName((ushort)index, 0, Translation));
    public static string? Memory(int index) => Strip(EventFlagVillagerMemoryPlayer.GetName((ushort)index, 0, Translation));
}
