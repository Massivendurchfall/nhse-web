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
        ["tools", "materials", "furniture", "interior", "clothing", "critters", "museum", "other"];

    private readonly Dictionary<ushort, Entry> byId = [];
    private Task? loading;
    private string baseUrl = "https://acnhcdn.com/latest/";

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
        Items = items.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Loaded = true;
    }

    public Entry? Find(ushort id) => byId.GetValueOrDefault(id);

    public string Url(string picture) => baseUrl + picture + ".png";

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
        _ => "menuicon/Present.png",
    };
}
