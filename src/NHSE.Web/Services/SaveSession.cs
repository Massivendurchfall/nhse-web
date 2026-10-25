using System.IO.Compression;
using Microsoft.JSInterop;
using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// The save being edited: the files the user picked, NHSE's view of them and the way back
/// (writing into the picked folder or downloading a ZIP).
/// </summary>
public sealed class SaveSession(IJSRuntime js)
{
    private IJSObjectReference? module;
    private Dictionary<string, byte[]> originals = [];
    private string rootPrefix = string.Empty;
    private bool backupDownloaded;

    public HorizonSave? Save { get; private set; }
    public MemorySaveFileProvider? Provider { get; private set; }
    public bool CanWriteBack { get; private set; }
    public bool SizesValid { get; private set; }
    public int InvalidHashes { get; private set; }
    public bool Dirty { get; private set; }
    public string TownName => Save?.Players[0].Personal.TownName ?? string.Empty;

    public event Action? Changed;

    public async Task<IJSObjectReference> Js() =>
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/savefs.js");

    public void MarkDirty()
    {
        Dirty = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        Save = null;
        Provider = null;
        originals = [];
        Dirty = false;
        Changed?.Invoke();
    }

    /// <summary>Loads from paths the browser picked (folder picker or folder upload).</summary>
    public async Task LoadPickedAsync(IReadOnlyList<string> paths)
    {
        var root = FindRoot(paths) ?? throw new SaveLoadException(L.T(
            "Im gewählten Ordner liegt keine main.dat mit mainHeader.dat. Wähle den Ordner 01006F8002326000.",
            "The chosen folder has no main.dat with mainHeader.dat. Pick the 01006F8002326000 folder."));

        var js = await Js();
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;
            files[path[root.Length..]] = await js.InvokeAsync<byte[]>("readFile", path);
        }
        Open(files, root, await js.InvokeAsync<bool>("canWriteBack"));
    }

#if DEBUG
    /// <summary>
    /// Development only: loads a copy of a save from wwwroot/testsave (listed in files.txt),
    /// so the UI can be tested without a file dialog. Never part of a release build.
    /// </summary>
    public async Task LoadTestSaveAsync(Uri baseAddress)
    {
        using var http = new HttpClient { BaseAddress = baseAddress };
        var list = await http.GetStringAsync("testsave/files.txt");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in list.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            files[path] = await http.GetByteArrayAsync("testsave/" + path + ".bin"); // .dat is no served file type
        Open(files, string.Empty, false);
    }

    /// <summary>
    /// Development only: encrypts the edited save in memory, decrypts the result again and
    /// reports what came back, without downloading anything.
    /// </summary>
    public string SelfTest()
    {
        if (Save is null || Provider is null)
            return "no save";
        // structures NHSE copies with the runtime marshaller: same size and an unchanged round trip?
        var personal = Save.Players[0].Personal;
        string Check<T>(ReadOnlySpan<byte> raw, byte[] written) =>
            $"{typeof(T).Name} {System.Runtime.InteropServices.Marshal.SizeOf<T>()}/{written.Length}/{(raw[..Math.Min(raw.Length, written.Length)].SequenceEqual(written) ? "same" : "DIFF")}";
        var marshal = string.Join(", ",
            Check<AchievementList>(personal.Data[personal.Offsets.CountAchievement..], personal.Achievements.ToBytes()),
            Check<GSavePlayerManpu15>(personal.Data[personal.Offsets.Manpu..], ((GSavePlayerManpu15)personal.Reactions).ToBytes()),
            Check<TurnipStonk>(Save.Main.Data[Save.Main.Offsets.ShopKabu..], Save.Main.Turnips.ToBytesClass()),
            Check<GSaveVisitorNpc>(Save.Main.Data[Save.Main.Offsets.Visitor..], Save.Main.Visitor.ToBytesClass()),
            Save.Main.GetVillager(0) is Villager2 v2
                ? Check<VillagerItem>(v2.Data[0x1094c..], VillagerItem.SetArray(v2.WearStockList)) + " " +
                  Check<GSavePlayerId>(v2.GetMemory(0).Data, v2.GetMemory(0).PlayerId.ToBytes())
                : "no villager");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Save.Save((uint)DateTime.Now.Ticks);
        var saveMs = timer.ElapsedMilliseconds;
        timer.Restart();
        var reloaded = new HorizonSave(new MemorySaveFileProvider(Provider.Files));
        var p = reloaded.Players[0].Personal;
        var book = p.GetRecipeBook();
        var main = reloaded.Main;
        var villagers = main.GetVillagers().Where(v => !VillagerCatalog.IsEmpty(v))
            .Select(v => v.InternalName + (v.MovingOut ? " (out)" : ""));
        var houses = main.GetVillagerHouses().Count(h => h.NPC1 >= 0);
        var weeds = main.GetFieldItemLayer0().Count(t => FieldItemList.Items.TryGetValue(t.DisplayItemId, out var d) && d.Kind.IsWeed);
        return $"self test: marshal [{marshal}], save {saveMs} ms, reload {timer.ElapsedMilliseconds} ms, invalid hashes {reloaded.GetInvalidHashes().Count()}, " +
               $"wallet {p.Wallet.Value}, bank {p.Bank.Value}, slot 16 {GameInfo.Strings.GetItemName(p.Pocket[15])}, " +
               $"recipes {RecipeList.Recipes.Keys.Count(k => book.GetIsKnown(k))}, reactions {p.Reactions.ManpuBit.Count(r => r != 0)}, " +
               $"villagers {string.Join(" ", villagers)}, houses {houses}, turnips buy {main.Turnips.BuyPrice} mon am {main.Turnips.SellMondayAM}, " +
               $"museum {main.Museum.GetItems().Count(i => !i.IsNone)}, weeds {weeds}, " +
               $"ground roots {main.GetFieldItemLayer0().Count(t => t.IsRoot)} (furniture {main.GetFieldItemLayer0().Count(t => t.IsRoot && t.ItemId < Item.FieldItemMin)}), " +
               $"path tiles {main.GetTerrainTiles().Count(t => t.UnitModelRoad.IsRoad)}, " +
               $"bridges [{string.Join(" ", main.Buildings.Where(b => b.BuildingType == BuildingType.Bridge).Select(b => $"{(BridgeType)b.Type}@{b.X},{b.Y}/a{b.Angle}"))}], " +
               $"catalog {Enumerable.Range(1, GameInfo.Strings.itemlist.Length - 1).Count(i => FlagUtil.GetFlag(p.Data, p.Offsets.ItemCollectBit, i))}, " +
               $"achievement counts {p.Achievements.Counts.Count(c => c > 0)}, birthday {p.Birthday}, fruit {p.ProfileFruit}/{main.SpecialtyFruit}, " +
               $"hemisphere {main.Hemisphere}, flower {main.SpecialtyFlower}, airport {main.AirportThemeColor}, visitors {string.Join("/", main.Visitor.VisitorNPC)}, " +
               $"camp {main.CampsiteStatus} {main.CampsiteVillagerSpeciesID}:{main.CampsiteVillagerVariantID}, recycle {main.RecycleBin.Count(i => !i.IsNone)}, " +
               $"pockets {p.PocketCount}+{p.BagCount}, " +
               $"house 0 room 0 floor [{string.Join(" ", new RoomManager(main.GetPlayerHouse(0).GetRoom(0)).Layers[0].Tiles.Where(t => t.IsRoot).Select(t => $"{GameInfo.Strings.GetItemName(t)}/r{t.Rotation}"))}], " +
               $"design 0 '{main.GetDesign(0).DesignName}' {main.GetDesign(0).UsageCompatibility:X4} by {main.GetDesign(0).PlayerName}, flag '{main.FlagMyDesign.DesignName}', " +
               (main.GetVillager(0) is var v0 && !VillagerCatalog.IsEmpty(v0)
                   ? $"villager 0 {v0.Personality} friendship {v0.GetMemory(0).Friendship} nick {v0.GetMemory(0).NickName} wear {v0.WearStockList.Count(i => !i.IsNone)}: {GameInfo.Strings.GetItemName(v0.WearStockList[0])}, "
                   : "") +
               $"storage {p.ItemChest.Count(i => !i.IsNone)} of {p.ItemChestCount}: " +
               string.Join(", ", p.ItemChest.Where(i => !i.IsNone).Take(3).Select(i => $"{GameInfo.Strings.GetItemName(i)} x{i.Count + 1}"));
    }
#endif

    /// <summary>Loads from an uploaded ZIP of the save folder.</summary>
    public void LoadZip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => e.Name.Length > 0).ToList();
        var root = FindRoot(entries.Select(e => e.FullName.Replace('\\', '/')).ToList())
                   ?? throw new SaveLoadException(L.T("In der ZIP liegt keine main.dat mit mainHeader.dat.",
                       "The ZIP has no main.dat with mainHeader.dat."));

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files[path[root.Length..]] = buffer.ToArray();
        }
        Open(files, root, false);
    }

    private void Open(Dictionary<string, byte[]> files, string root, bool canWriteBack)
    {
        var provider = new MemorySaveFileProvider(files);
        HorizonSave save;
        try
        {
            save = new HorizonSave(provider);
        }
        catch (Exception e)
        {
            throw new SaveLoadException(L.T("Der Spielstand ließ sich nicht lesen: ", "The save could not be read: ") + e.Message);
        }

        originals = files.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);
        rootPrefix = root;
        Provider = provider;
        Save = save;
        CanWriteBack = canWriteBack;
        SizesValid = save.ValidateSizes();
        InvalidHashes = save.GetInvalidHashes().Count();
        backupDownloaded = false;
        Dirty = false;
        Changed?.Invoke();
    }

    /// <summary>Folder that holds main.dat and mainHeader.dat, as a path prefix ("" or "x/y/").</summary>
    private static string? FindRoot(IReadOnlyCollection<string> paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return paths
            .Where(p => p.EndsWith("main.dat", StringComparison.OrdinalIgnoreCase) &&
                        (p.Length == 8 || p[^9] == '/'))
            .Select(p => p[..^8])
            .Where(prefix => set.Contains(prefix + "mainHeader.dat"))
            .OrderBy(prefix => prefix.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Encrypts the edited save. With folder access the files are written back (after a backup
    /// ZIP of the original files has been downloaded once); otherwise the result is downloaded.
    /// </summary>
    public async Task<string> SaveAsync()
    {
        if (Save is null || Provider is null)
            return string.Empty;

        Save.Save((uint)DateTime.Now.Ticks);
        var js = await Js();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var town = StringUtil.CleanFileName(TownName);

        if (!CanWriteBack)
        {
            await js.InvokeVoidAsync("download", $"ACNH-{town}-{stamp}.zip", Zip(Provider.Files));
            Dirty = false;
            Changed?.Invoke();
            return L.T("Der bearbeitete Spielstand wurde als ZIP heruntergeladen. Entpacke ihn in deinen Spielstandordner.",
                "The edited save was downloaded as a ZIP. Unpack it into your save folder.");
        }

        if (!backupDownloaded)
        {
            await js.InvokeVoidAsync("download", $"ACNH-Backup-{town}-{stamp}.zip", Zip(originals));
            backupDownloaded = true;
        }
        foreach (var path in Provider.Changed)
            await js.InvokeVoidAsync("writeFile", rootPrefix + path, Provider.Files[path]);
        Dirty = false;
        Changed?.Invoke();
        return L.T($"{Provider.Changed.Count} Dateien in den Ordner geschrieben. Das Backup der Originale liegt in deinen Downloads.",
            $"Wrote {Provider.Changed.Count} files to the folder. The backup of the originals is in your downloads.");
    }

    public async Task DownloadBackupAsync()
    {
        var js = await Js();
        await js.InvokeVoidAsync("download", $"ACNH-Backup-{StringUtil.CleanFileName(TownName)}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip",
            Zip(originals));
        backupDownloaded = true;
    }

    private static byte[] Zip(IReadOnlyDictionary<string, byte[]> files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (path, data) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(data);
            }
        }
        return buffer.ToArray();
    }
}

public sealed class SaveLoadException(string message) : Exception(message);
