using System.Text.RegularExpressions;
using NHSE.Core;
using NHSE.Villagers;

namespace NHSE.Web.Services;

/// <summary>
/// All regular villagers with name, species and picture, plus the download of the
/// NHSE.Villagers templates (wwwroot/villagers) that a swap needs.
/// </summary>
public sealed partial class VillagerCatalog(HttpClient http)
{
    public sealed record Entry(string Id, string Name, VillagerSpecies Species);

    private IReadOnlyList<Entry>? all;

    /// <summary>Every villager that can move to an island, sorted by name in the UI language.</summary>
    public IReadOnlyList<Entry> All => all ??= GameInfo.Strings.VillagerMap
        .Where(v => RegularVillager().IsMatch(v.Key) && Enum.TryParse<VillagerSpecies>(v.Key[..3], out _))
        .Select(v => new Entry(v.Key, v.Value, Enum.Parse<VillagerSpecies>(v.Key[..3])))
        .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public static IReadOnlyList<VillagerSpecies> Species { get; } =
        Enum.GetValues<VillagerSpecies>().Where(s => s != VillagerSpecies.non).ToList();

    // NHSE.Sprites pictures, served from wwwroot/villagericon (the CDN lacks the newest villagers)
    public static string Picture(string id) => $"villagericon/{id}.png";

    public static string Name(IVillager villager) => GameInfo.Strings.GetVillager(villager.InternalName);

    public static bool IsEmpty(IVillager villager) => villager.Species == (byte)VillagerSpecies.non;

    /// <summary>Fetches the template of a villager once and registers it for <see cref="VillagerSwap"/>.</summary>
    public async Task LoadTemplateAsync(string id)
    {
        if (VillagerResources.IsVillagerDataKnown(id))
            return;
        var villager = await http.GetByteArrayAsync($"villagers/{id}V.bin");
        var house = await http.GetByteArrayAsync($"villagers/{id}H.bin");
        VillagerResources.Add(id, villager, house);
    }

    public static string SpeciesName(VillagerSpecies species) => species switch
    {
        VillagerSpecies.ant => L.T("Ameisenbär", "Anteater"),
        VillagerSpecies.bea => L.T("Bär", "Bear"),
        VillagerSpecies.brd => L.T("Vogel", "Bird"),
        VillagerSpecies.bul => L.T("Stier", "Bull"),
        VillagerSpecies.cat => L.T("Katze", "Cat"),
        VillagerSpecies.cbr => L.T("Bärchen", "Cub"),
        VillagerSpecies.chn => L.T("Huhn", "Chicken"),
        VillagerSpecies.cow => L.T("Kuh", "Cow"),
        VillagerSpecies.crd => L.T("Alligator", "Alligator"),
        VillagerSpecies.der => L.T("Hirsch", "Deer"),
        VillagerSpecies.dog => L.T("Hund", "Dog"),
        VillagerSpecies.duk => L.T("Ente", "Duck"),
        VillagerSpecies.elp => L.T("Elefant", "Elephant"),
        VillagerSpecies.flg => L.T("Frosch", "Frog"),
        VillagerSpecies.goa => L.T("Ziege", "Goat"),
        VillagerSpecies.gor => L.T("Gorilla", "Gorilla"),
        VillagerSpecies.ham => L.T("Hamster", "Hamster"),
        VillagerSpecies.hip => L.T("Nilpferd", "Hippo"),
        VillagerSpecies.hrs => L.T("Pferd", "Horse"),
        VillagerSpecies.kal => L.T("Koala", "Koala"),
        VillagerSpecies.kgr => L.T("Känguru", "Kangaroo"),
        VillagerSpecies.lon => L.T("Löwe", "Lion"),
        VillagerSpecies.mnk => L.T("Affe", "Monkey"),
        VillagerSpecies.mus => L.T("Maus", "Mouse"),
        VillagerSpecies.ocp => L.T("Oktopus", "Octopus"),
        VillagerSpecies.ost => L.T("Strauß", "Ostrich"),
        VillagerSpecies.pbr => L.T("Adler", "Eagle"),
        VillagerSpecies.pgn => L.T("Pinguin", "Penguin"),
        VillagerSpecies.pig => L.T("Schwein", "Pig"),
        VillagerSpecies.rbt => L.T("Hase", "Rabbit"),
        VillagerSpecies.rhn => L.T("Nashorn", "Rhino"),
        VillagerSpecies.shp => L.T("Schaf", "Sheep"),
        VillagerSpecies.squ => L.T("Eichhörnchen", "Squirrel"),
        VillagerSpecies.tig => L.T("Tiger", "Tiger"),
        VillagerSpecies.wol => L.T("Wolf", "Wolf"),
        _ => species.ToString(),
    };

    // ant00 ... wol09; special characters like Isabelle have other keys and no template
    [GeneratedRegex("^[a-z]{3}[0-9]{2}$")]
    private static partial Regex RegularVillager();
}
