using System.Text.RegularExpressions;
using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// Names, pictures and the picker list for field items: trees, flowers, bushes, vegetables,
/// rocks, fences and weeds (item IDs from 60000). NHSE only knows them by internal names
/// such as "PltFlwRose3Red", so names come from the item they turn into when dug up or picked.
/// </summary>
public static partial class FieldItems
{
    public enum Group { Trees, Flowers, Bushes, Vegetables, Rocks, Fences, Weeds }

    public sealed record Entry(ushort Id, Group Group, string Name, string Picture);

    public static IReadOnlyList<Group> Groups { get; } = Enum.GetValues<Group>();

    private static IReadOnlyList<Entry>? all;

    public static bool IsMoneyTree(ushort id) => id is >= 0xEC9C and <= 0xECA0;

    public static string GroupName(Group group) => group switch
    {
        Group.Trees => L.T("Bäume", "Trees"),
        Group.Flowers => L.T("Blumen", "Flowers"),
        Group.Bushes => L.T("Sträucher", "Bushes"),
        Group.Vegetables => L.T("Gemüse", "Vegetables"),
        Group.Rocks => L.T("Felsen", "Rocks"),
        Group.Fences => L.T("Zäune", "Fences"),
        _ => L.T("Unkraut", "Weeds"),
    };

    public static string GroupIcon(Group group) => group switch
    {
        Group.Trees => "menuicon/PltApple.png",
        Group.Flowers => "menuicon/PltRose0.png",
        Group.Bushes => "menuicon/PltAzaleaPink.png",
        Group.Vegetables => "menuicon/PltSquashOrange.png",
        Group.Rocks => "menuicon/OreStone.png",
        Group.Fences => "menuicon/Fence.png",
        _ => "menuicon/Weed.png",
    };

    /// <summary>
    /// The grown, placeable form of every field item: adult trees, blooming flowers, full bushes,
    /// ripe vegetables, rocks, fences and weeds. Seedlings, stumps and ladders are left out.
    /// </summary>
    public static IReadOnlyList<Entry> All(ItemCatalog catalog) => all ??= Build(catalog);

    private static List<Entry> Build(ItemCatalog catalog)
    {
        var list = new List<Entry>();
        var bushes = new Dictionary<string, (int Stage, FieldItemDefinition Def)>();
        var fences = new Dictionary<ushort, FieldItemDefinition>();
        foreach (var def in FieldItemList.Items.Values)
        {
            var name = def.Name;
            var kind = def.Kind.ToString();
            Group? group = null;
            if (GrownTree().IsMatch(name))
                group = Group.Trees;
            else if (kind.StartsWith("PltFlw", StringComparison.Ordinal) && Blooming().IsMatch(name))
                group = Group.Flowers;
            else if (kind.StartsWith("PltVgt", StringComparison.Ordinal) && Blooming().IsMatch(name))
                group = Group.Vegetables;
            else if (kind.StartsWith("Stone", StringComparison.Ordinal))
                group = Group.Rocks;
            else if (kind.StartsWith("Fence", StringComparison.Ordinal) && FenceVariant().IsMatch(name))
                group = Group.Fences;
            else if (kind.StartsWith("Fence", StringComparison.Ordinal) && def.Pick != Item.NONE)
            {
                // the numbered pieces of a fence look alike in the picker: keep one per fence, the plain one if there is
                if (!fences.TryGetValue(def.Pick, out var known) || (char.IsDigit(known.Name[^1]) && !char.IsDigit(name[^1])))
                    fences[def.Pick] = def;
            }
            else if (kind.StartsWith("PltWeed", StringComparison.Ordinal) && name.EndsWith('3'))
                group = Group.Weeds;
            else if (kind.StartsWith("PltBush", StringComparison.Ordinal) && BushStage().Match(name) is { Success: true } m)
            {
                // keep the largest stage of every bush and colour
                var key = m.Groups[1].Value + m.Groups[3].Value;
                var stage = int.Parse(m.Groups[2].Value);
                if (!bushes.TryGetValue(key, out var known) || known.Stage < stage)
                    bushes[key] = (stage, def);
            }
            if (group is { } g)
                list.Add(new Entry(def.Index, g, EntryName(def, g), Picture(new Item(def.Index), catalog)));
        }
        foreach (var (_, def) in bushes.Values)
            list.Add(new Entry(def.Index, Group.Bushes, EntryName(def, Group.Bushes), Picture(new Item(def.Index), catalog)));
        foreach (var def in fences.Values)
            list.Add(new Entry(def.Index, Group.Fences, EntryName(def, Group.Fences), Picture(new Item(def.Index), catalog)));
        return list
            .OrderBy(e => e.Group)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string EntryName(FieldItemDefinition def, Group group)
    {
        switch (def.Name)
        {
            case "PltTreeCedarDecoA":
                return L.T("Nadelbaum, geschmückt 1", "Decorated cedar 1");
            case "PltTreeCedarDecoB":
                return L.T("Nadelbaum, geschmückt 2", "Decorated cedar 2");
            case "PltTreeEasterEgg":
                return L.T("Häschentag-Baum", "Bunny Day tree");
        }
        if (group == Group.Rocks)
            return L.T("Felsen ", "Rock ") + def.Name[^1];
        var name = Name(new Item(def.Index));
        if (group == Group.Weeds && WeedSeason().Match(def.Name) is { Success: true } weed)
        {
            var season = weed.Groups[1].Value switch
            {
                "Spr" => L.T("Frühling", "spring"),
                "Smr" => L.T("Sommer", "summer"),
                "Aut" => L.T("Herbst", "autumn"),
                _ => L.T("Winter", "winter"),
            };
            var kind = weed.Groups[2].Value;
            return $"{name} ({season}{(kind.Length > 0 ? " " + (int.Parse(kind) + 1) : "")})";
        }
        if (group != Group.Fences)
            return name;
        // the coloured wooden fences share one item name
        var suffix = FenceVariant().Match(def.Name).Groups[1].Value;
        return suffix.Length == 0 ? name : $"{name} ({ColorName(suffix)})";
    }

    /// <summary>Display name of a field item, or of any other item.</summary>
    public static string Name(Item item)
    {
        var id = item.DisplayItemId;
        if (id < Item.FieldItemMin || !FieldItemList.Items.TryGetValue(id, out var def))
            return GameInfo.Strings.GetItemName(item);
        if (IsMoneyTree(id))
            return L.T("Geldbaum", "Money tree");
        var held = def.HeldItemId;
        var names = GameInfo.Strings.itemlist; // without the "(#id)" NHSE adds to repeated names
        if (held < names.Length)
        {
            var name = names[held];
            if (!string.IsNullOrWhiteSpace(name))
                return name.Replace(" (Pflanze)", string.Empty).Replace(" (reif)", string.Empty);
        }
        return KindName(def.Kind);
    }

    /// <summary>Picture of an item as it lies on the map.</summary>
    public static string Picture(Item item, ItemCatalog catalog)
    {
        var id = item.DisplayItemId;
        if (id < Item.FieldItemMin)
            return catalog.PictureOf(item);
        // flowers, vegetables, fences and weeds look like the item they turn into when picked up
        if (!IsMoneyTree(id) && FieldItemList.Items.TryGetValue(id, out var def) && def.HeldItemId < Item.FieldItemMin
            && catalog.Find(def.HeldItemId) is { } held)
        {
            // some CDN menu icons are empty pictures; NHSE ships most of them (named like ItemMenuIconType)
            const string menu = "MenuIcon/";
            if (held.Picture.StartsWith(menu, StringComparison.Ordinal) && Enum.TryParse<ItemMenuIconType>(held.Picture[menu.Length..], out _))
                return $"menuicon/{held.Picture[menu.Length..]}.png";
            return catalog.Url(held.Picture);
        }
        return Icon(id) ?? ItemCatalog.Fallback;
    }

    // NHSE's menu icons stand in for trees, flowers, rocks and the like.
    private static string? Icon(ushort id)
    {
        if (IsMoneyTree(id))
            return "menuicon/PltMoney.png";
        if (!FieldItemList.Items.TryGetValue(id, out var def))
            return null;
        var name = def.Name;
        var kind = def.Kind.ToString();
        string? icon = kind switch
        {
            "PltTreeOak" when name.Contains("Apple") => "PltApple",
            "PltTreeOak" when name.Contains("Orange") => "PltOrange",
            "PltTreeOak" when name.Contains("Pear") => "PltPear",
            "PltTreeOak" when name.Contains("Cherry") => "PltCherry",
            "PltTreeOak" when name.Contains("Peach") => "PltPeach",
            "PltTreeOak" => "PltOak",
            "PltTreeBamboo" => "PltBamboo",
            "PltTreeCedar" or "PltTreeCedarDeco" => "PltConifer",
            "PltTreePalm" => "PltPalm",
            "PltBushAzalea" => name.Contains("White") ? "PltAzaleaWhite" : "PltAzaleaPink",
            "PltBushCamellia" => name.Contains("Pink") ? "PltBushCamelliaPink" : "PltBushCamelliaRed",
            "PltBushHibiscus" => name.Contains("Yellow") ? "PltHibiscusYellow" : "PltHibiscusRed",
            "PltBushHolly" => "PltHolly",
            "PltBushHydrangea" => name.Contains("Pink") ? "PltHydrangeaPink" : "PltHydrangeaBlue",
            "PltBushOsmanthus" => name.Contains("Orange") ? "PltBushOsmathusOrange" : "PltBushOsmathusYellow",
            "PltBushPlumeria" => name.Contains("Pink") ? "PltBushPlumeriaPink" : "PltBushPlumeriaWhite",
            "PltFlwAnemone" => "PltAnemone" + FlowerColor(name, 6),
            "PltFlwCosmos" => "PltCosmos" + FlowerColor(name, 6),
            "PltFlwHyacinth" => "PltHyacinth" + FlowerColor(name, 7),
            "PltFlwLily" => "PltSuzuran",
            "PltFlwYuri" => "PltYuri" + FlowerColor(name, 6),
            "PltFlwMum" => "PltMum" + FlowerColor(name, 6),
            "PltFlwPansy" => "PltPansi" + FlowerColor(name, 6),
            "PltFlwRose" => "PltRose" + FlowerColor(name, 8),
            "PltFlwRoseGold" => "PltGoldenRose",
            "PltFlwTulip" => "PltTurip" + FlowerColor(name, 7),
            "PltVgtCarrot" => "PltCarrot",
            "PltVgtPotato" => "PltPotato",
            "PltVgtPumpkin" => name.Contains("White") ? "PltSquashWhite" : name.Contains("Green") ? "PltSquashGreen"
                : name.Contains("Yellow") ? "PltSquashYellow" : "PltSquashOrange",
            "PltVgtSugarcane" => "PltSugarCane",
            "PltVgtTomato" => "PltTomato",
            "PltVgtWheat" => "PltWheat",
            "PltVine" => "PltVine",
            "PltWeedLight" => "PltWeedLight",
            "UnitIconHole" => "Scoop",
            _ when kind.StartsWith("PltWeed", StringComparison.Ordinal) => "Weed",
            _ when kind.StartsWith("Stone", StringComparison.Ordinal) => "OreStone",
            _ when kind.StartsWith("Fence", StringComparison.Ordinal) => "Fence",
            _ when kind.StartsWith("LadderKit", StringComparison.Ordinal) => "FixedLadder",
            _ => null,
        };
        return icon is null ? null : $"menuicon/{icon}.png";
    }

    // the flower menu icons are numbered by colour in this order
    private static readonly string[] FlowerColors = ["White", "Red", "Yellow", "Pink", "Orange", "Purple", "Black", "Blue"];

    private static int FlowerColor(string name, int count)
    {
        for (int i = 0; i < Math.Min(count, FlowerColors.Length); i++)
        {
            if (name.EndsWith(FlowerColors[i], StringComparison.Ordinal))
                return i;
        }
        return 0;
    }

    private static string ColorName(string suffix) => suffix switch
    {
        "White" => L.T("Weiß", "White"),
        "Blue" => L.T("Blau", "Blue"),
        "Pink" => L.T("Rosa", "Pink"),
        "Natural" => L.T("Natur", "Natural"),
        "Yellow" => L.T("Gelb", "Yellow"),
        "Orange" => L.T("Orange", "Orange"),
        "Purple" => L.T("Lila", "Purple"),
        "Green" => L.T("Grün", "Green"),
        _ => suffix.TrimStart('0'),
    };

    private static string KindName(FieldItemKind kind)
    {
        var name = kind.ToString();
        return name switch
        {
            _ when name.StartsWith("Fence", StringComparison.Ordinal) => L.T("Zaun", "Fence"),
            _ when name.StartsWith("LadderKit", StringComparison.Ordinal) => L.T("Leiter", "Ladder"),
            _ when name.StartsWith("PltWeed", StringComparison.Ordinal) => L.T("Unkraut", "Weed"),
            _ when name.StartsWith("Stone", StringComparison.Ordinal) => L.T("Felsen", "Rock"),
            _ when name.StartsWith("PltBush", StringComparison.Ordinal) => L.T("Strauch", "Bush"),
            _ when name.StartsWith("PltFlw", StringComparison.Ordinal) => L.T("Blume", "Flower"),
            _ when name.StartsWith("PltTree", StringComparison.Ordinal) => L.T("Baum", "Tree"),
            _ when name.StartsWith("PltVgt", StringComparison.Ordinal) => L.T("Gemüse", "Vegetable"),
            _ when kind == FieldItemKind.UnitIconHole => L.T("Loch", "Hole"),
            _ => name,
        };
    }

    // adult trees: the stage 4 trees, fruit trees without a stage, the money tree and decorated cedars
    [GeneratedRegex("^PltTree(Oak4|Cedar4|Bamboo4|Palm4|Money4|Apple|Orange|Pear|Cherry|Peach|CedarDeco[AB]|EasterEgg)$")]
    private static partial Regex GrownTree();

    // stage 3: blooming flowers and ripe vegetables
    [GeneratedRegex("^Plt(Flw|Vgt)[A-Za-z]+3[A-Za-z]*$")]
    private static partial Regex Blooming();

    [GeneratedRegex("^(PltBush[A-Za-z]+?)([0-9])([A-Za-z]*)$")]
    private static partial Regex BushStage();

    [GeneratedRegex("^FenceWood(White|Blue|Pink|Natural|Yellow|Orange|Purple|Green)$")]
    private static partial Regex FenceVariant();

    // PltWeedSpr3, PltWeedAut13, PltWeedWin23: season and the kind of weed for that season
    [GeneratedRegex("^PltWeed(Spr|Smr|Aut|Win)([12]?)3$")]
    private static partial Regex WeedSeason();
}
