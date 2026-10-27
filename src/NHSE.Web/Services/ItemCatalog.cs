using System.Text.Json;
using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// Item pictures and categories for the picker, loaded once from data/catalog.json
/// (built by tools/build_catalog.py). Names come from NHSE in the UI language.
/// </summary>
public sealed class ItemCatalog(HttpClient http)
{
    public sealed record Variant(ushort Count, string Picture);

    public sealed record Entry(ushort Id, int Category, string Name, string Picture, IReadOnlyList<Variant> Variants);

    public static readonly string[] CategoryKeys =
        ["tools", "materials", "furniture", "interior", "clothing", "critters", "museum", "other", "kits"];

    /// <summary>The picker's own category: kits sit among the materials in the game, but are easier to find on their own.</summary>
    public static readonly int KitsCategory = Array.IndexOf(CategoryKeys, "kits");

    // construction, moving, housing, ladder, marker and customization kits (ids from NHSE's English item names)
    private static readonly HashSet<ushort> Kits =
        [2513, 2756, 3349, 4310, 4311, 4411, 5166, 5358, 5359, 5360, 5361, 5827, 5828, 5829, 6841, 7211, 13526, 13530, 13534, 14470];

    public static bool InCategory(Entry entry, int category) =>
        category == KitsCategory ? Kits.Contains(entry.Id) : entry.Category == category;

    private readonly Dictionary<ushort, Entry> byId = [];
    private Task? loading;
    private string baseUrl = "https://nh-cdn.catalogue.ac/";

    public IReadOnlyList<Entry> Items { get; private set; } = [];
    public bool Loaded { get; private set; }

    public Task EnsureLoadedAsync() => loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("data/catalog.json"));
        baseUrl = doc.RootElement.GetProperty("base").GetString() ?? baseUrl;
        var strings = GameInfo.Strings;
        var items = new List<Entry>();
        foreach (var row in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = (ushort)row[0].GetInt32();
            var name = id < strings.itemlist.Length ? strings.itemlist[id] : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                continue; // no name in NHSE: not a regular item
            var variants = row.GetArrayLength() > 3
                ? row[3].EnumerateArray().Select(v => new Variant((ushort)v[0].GetInt32(), v[1].GetString()!)).ToList()
                : [];
            var entry = new Entry(id, row[1].GetInt32(), name, row[2].GetString()!, variants);
            items.Add(entry);
            byId[id] = entry;
        }
        // NHSE names more items than the picture data knows (everything from version 3.0, songs, bell bags, ...):
        // those come along with NHSE's generic inventory icon, internal placeholders stay out
        var english = GameInfo.GetStrings("en").itemlist;
        for (int i = 1; i < Math.Min(strings.itemlist.Length, Item.FieldItemMin); i++)
        {
            var id = (ushort)i;
            var name = strings.itemlist[id];
            if (byId.ContainsKey(id) || string.IsNullOrWhiteSpace(name) || !IsRealItem(id, id < english.Length ? english[id] : name))
                continue;
            var entry = new Entry(id, CategoryOf(id), name, MenuIcon(id)[..^4], []);
            items.Add(entry);
            byId[id] = entry;
        }
        Items = items.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Loaded = true;
    }

    public Entry? Find(ushort id) => byId.GetValueOrDefault(id);

    public string Url(string picture) => picture.StartsWith("menuicon/", StringComparison.Ordinal) ? picture + ".png" : baseUrl + picture + ".png";

    // names in Japanese, "Ftr..." model names and "(internal)" mark the game's own placeholders
    private static bool IsRealItem(ushort id, string english)
    {
        if (english.StartsWith("Ftr", StringComparison.Ordinal) || english.Contains("(internal)", StringComparison.Ordinal)
            || english.Any(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿'))
            return false;
        var kind = ItemInfo.GetItemKind(id).ToString();
        return !kind.StartsWith("Kind_Dummy", StringComparison.Ordinal)
            && kind is not ("Kind_SequenceOnly" or "Kind_NnpcRoomMarker" or "Kind_PlayerDemoOutfit" or "Kind_GardenEditList"
                or "Kind_PhotoStudioList" or "Kind_MusicMiss" or "Kind_OneRoomBox");
    }

    private static readonly string[] ToolKinds = ["Axe", "Shovel", "Net", "FishingRod", "Slingshot", "Watering", "Ladder", "Timer", "Megaphone",
        "SmartPhone", "StickLight", "Balloon", "BlowBubble", "Ocarina", "Panflute", "Tambourine", "Uchiwa", "Partyhorn", "PartyPopper", "PinataStick",
        "WoodenStickTool", "FierworkHand", "MaracasCarnival", "SubTool", "ChangeStick", "Transceiver", "HandheldPennant", "Sensu"];
    private static readonly string[] ClothingKinds = ["Accessory", "Bag", "Cap", "Helmet", "Shoes", "Socks", "Umbrella", "HandBag", "NpcOutfit",
        "Top", "Bottoms", "Onepiece", "OnePiece", "Marine", "Glass", "Mask"];
    private static readonly string[] FurnitureKinds = ["Ftr", "Counter", "Pillar", "Fence", "Lantern", "Windmill", "XmasDeco", "Gyroid", "HousePost"];

    /// <summary>A picker category for an item the picture data does not know, from NHSE's item kind.</summary>
    private static int CategoryOf(ushort id)
    {
        // most kinds are "Kind_...", furniture and clothing also come as "Ftr_2x2_Floor", "Top_Middle" and the like
        var name = ItemInfo.GetItemKind(id).ToString();
        var kind = name.StartsWith("Kind_", StringComparison.Ordinal) ? name["Kind_".Length..] : name;
        string key = kind switch
        {
            "Fish" or "Insect" or "DiveFish" or "ShellFish" => "critters",
            "Fossil" or "FossilUnknown" or "Picture" or "PictureFake" or "Sculpture" or "SculptureFake" => "museum",
            "RoomWall" or "RoomFloor" or "Rug" or "RugMyDesign" => "interior",
            _ when ToolKinds.Any(k => kind.StartsWith(k, StringComparison.Ordinal)) => "tools",
            _ when ClothingKinds.Any(k => kind.StartsWith(k, StringComparison.Ordinal)) => "clothing",
            _ when FurnitureKinds.Any(k => kind.StartsWith(k, StringComparison.Ordinal)) => "furniture",
            "Music" or "Poster" or "Bromide" or "DIYRecipe" or "MessageBottle" => "other",
            _ => "materials",
        };
        return Array.IndexOf(CategoryKeys, key);
    }

    /// <summary>Picture of an item as it sits in a slot, variant included.</summary>
    public string PictureOf(Item item)
    {
        var id = item.DisplayItemId;
        if (byId.TryGetValue(id, out var entry))
        {
            var variant = entry.Variants.FirstOrDefault(v => v.Count == item.Count);
            return Url(variant?.Picture ?? entry.Picture);
        }
        return MenuIcon(id);
    }

    /// <summary>NHSE's generic inventory icon for the item's kind.</summary>
    public static string MenuIcon(ushort id) => $"menuicon/{ItemInfo.GetMenuIcon(id)}.png";

    public const string Fallback = "menuicon/Leaf.png";

    public static string CategoryName(int index) => CategoryKeys[index] switch
    {
        "tools" => L.T("Werkzeuge", "Tools"),
        "materials" => L.T("Material und Essen", "Materials and food"),
        "furniture" => L.T("Möbel", "Furniture"),
        "interior" => L.T("Tapeten und Böden", "Walls and floors"),
        "clothing" => L.T("Kleidung", "Clothing"),
        "critters" => L.T("Fische und Insekten", "Fish and bugs"),
        "museum" => L.T("Fossilien und Kunst", "Fossils and art"),
        "kits" => L.T("Bausätze", "Kits"),
        _ => L.T("Sonstiges", "Other"),
    };

    /// <summary>A picture that stands for the category in the picker.</summary>
    public static string CategoryIcon(int index) => CategoryKeys[index] switch
    {
        "tools" => "menuicon/Axe.png",
        "materials" => "menuicon/Branch.png",
        "furniture" => "menuicon/Leaf.png",
        "interior" => "menuicon/Wall.png",
        "clothing" => "menuicon/Tops.png",
        "critters" => "menuicon/FishBlackbass.png",
        "museum" => "menuicon/Fossil.png",
        "kits" => "menuicon/TentWhite.png",
        _ => "menuicon/Present.png",
    };
}
