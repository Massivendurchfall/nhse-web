using System.Drawing;
using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// The island's two item layers for the map editor, plus the pixel data the map view draws.
/// Coordinates are absolute item tiles (32 per acre) on the full 9 x 8 acre map, as in NHSE's map editor.
/// </summary>
public sealed class IslandMap(MainSave main)
{
    private readonly MapTileManager manager = MapTileManagerUtil.FromSaveFile(main);

    private LayerPositionConfig Config => manager.ConfigItems;

    public int Width => Config.MapTotalWidth;
    public int Height => Config.MapTotalHeight;

    public LayerFieldItem Layer(int index) => index == 0 ? manager.FieldItems.Layer0 : manager.FieldItems.Layer1;

    public IReadOnlyList<Building> Buildings => manager.LayerBuildings.Buildings;

    /// <summary>Converts an absolute tile to the layer's own coordinates; false outside the editable land.</summary>
    public bool TryGetRelative(int absX, int absY, out int relX, out int relY)
    {
        (relX, relY) = Config.GetCoordinatesRelative(absX, absY);
        return Config.IsCoordinateValidRelative(relX, relY);
    }

    public (int X, int Y) GetAbsolute(int relX, int relY) => Config.GetCoordinatesAbsolute(relX, relY);

    /// <summary>Writes both item layers, the terrain (paths) and the buildings (bridges) back into main.dat.</summary>
    public void WriteTo(MainSave save)
    {
        save.SetFieldItemLayer0(Layer(0).Tiles);
        save.SetFieldItemLayer1(Layer(1).Tiles);
        save.SetTerrainTiles(Terrain.Tiles);
        save.Buildings = Buildings;
    }

    public sealed record State(ulong[] Items, byte[] Terrain, byte[] Buildings);

    public State Snapshot() => new(
        [.. Layer(0).Tiles.Select(t => t.RawValue), .. Layer(1).Tiles.Select(t => t.RawValue)],
        Terrain.DumpAll(),
        Building.SetArray(Buildings));

    public void Restore(State snapshot)
    {
        var first = Layer(0).Tiles;
        var second = Layer(1).Tiles;
        for (int i = 0; i < first.Length; i++)
            first[i].RawValue = snapshot.Items[i];
        for (int i = 0; i < second.Length; i++)
            second[i].RawValue = snapshot.Items[first.Length + i];
        Terrain.ImportAll(snapshot.Terrain);
        var buildings = Building.GetArray(snapshot.Buildings);
        for (int i = 0; i < buildings.Length; i++)
            Buildings[i].CopyFrom(buildings[i]);
    }

    // ---- bridges ----

    public const int MaxBridges = 8;

    public static IReadOnlyList<string> BridgeStyles { get; } = ["Log", "Suspension", "Stone", "Bricks", "Wood", "Iron", "Japanese", "Red"];

    public static string BridgeStyleName(string style) => style switch
    {
        "Log" => L.T("Baumstammbrücke", "Log bridge"),
        "Suspension" => L.T("Hängebrücke", "Suspension bridge"),
        "Stone" => L.T("Steinbrücke", "Stone bridge"),
        "Bricks" => L.T("Ziegelbrücke", "Brick bridge"),
        "Wood" => L.T("Holzbrücke", "Wooden bridge"),
        "Iron" => L.T("Eisenbrücke", "Iron bridge"),
        "Japanese" => L.T("Zen-Brücke", "Zen bridge"),
        "Red" => L.T("Rote Zen-Brücke", "Red zen bridge"),
        _ => style,
    };

    /// <summary>Style and length of a bridge from its type name, e.g. BridgeLog04 is a log bridge over 4 tiles.</summary>
    public static (string Style, int Length, bool Diagonal) BridgeInfo(Building bridge)
    {
        var name = ((BridgeType)bridge.Type).ToString();
        var diagonal = name.Contains("Diagonal", StringComparison.Ordinal);
        var style = name["Bridge".Length..].Replace("Diagonal", string.Empty).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var length = int.TryParse(name[^2..], out var l) ? l : 4;
        return (style, diagonal ? 4 : length, diagonal);
    }

    /// <summary>
    /// The area a building covers on the map, in absolute item tiles. Straight bridges are 2 terrain tiles
    /// wide and as long as their type says, running north to south when the angle is odd; everything else
    /// is drawn as the 4 x 4 square around its position, like NHSE does.
    /// </summary>
    public static (int X, int Y, int W, int H) Footprint(Building building)
    {
        if (building.BuildingType == BuildingType.Incline)
        {
            return (building.Angle & 1) == 0
                ? (building.X - 2, building.Y - 4, 4, 8)
                : (building.X - 4, building.Y - 2, 8, 4);
        }
        if (building.BuildingType == BuildingType.Bridge && BridgeInfo(building) is { Diagonal: false } info)
        {
            var span = info.Length * 2;
            return (building.Angle & 1) == 1
                ? (building.X - 2, building.Y - info.Length, 4, span)
                : (building.X - info.Length, building.Y - 2, span, 4);
        }
        return (building.X - 2, building.Y - 2, 4, 4);
    }

    public Building? BuildingAt(int absX, int absY) => Buildings.FirstOrDefault(b =>
        b.BuildingType != 0 && Footprint(b) is var (x, y, w, h) && absX >= x && absX < x + w && absY >= y && absY < y + h);

    private bool IsRiver(int relX, int relY) => IsTerrain(relX, relY) && Terrain.GetTile(relX, relY).UnitModel.IsRiver;

    /// <summary>
    /// Plans a straight bridge over the river at the tapped tile, the way the island's own bridges lie:
    /// across the narrow side of the river, 2 terrain tiles wide, from bank to bank over 3 to 5 tiles,
    /// with flat land of the same height at both ends. Returns why it does not fit, or null.
    /// </summary>
    public string? PlanBridge(int absX, int absY, string style, out Building bridge)
    {
        bridge = new Building();
        if (!TryGetTerrain(absX, absY, out var tx, out var ty) || !IsRiver(tx, ty))
            return L.T("Tippe auf das Wasser eines Flusses.", "Tap the water of a river.");
        if (Buildings.Count(b => b.BuildingType == BuildingType.Bridge) >= MaxBridges)
            return L.T($"Die Insel hat schon {MaxBridges} Brücken, mehr erlaubt das Spiel nicht.", $"The island already has {MaxBridges} bridges, the game allows no more.");

        int Run(int dx, int dy)
        {
            int n = 0;
            while (IsRiver(tx + (dx * (n + 1)), ty + (dy * (n + 1))))
                n++;
            return n;
        }
        var (up, down, left, right) = (Run(0, -1), Run(0, 1), Run(-1, 0), Run(1, 0));
        var vertical = up + down <= left + right; // cross the river the short way
        var length = vertical ? up + down + 1 : left + right + 1;
        if (length is < 3 or > 5)
            return L.T($"Der Fluss ist hier {length} Felder breit. Brücken gibt es für 3 bis 5 Felder.", $"The river is {length} tiles wide here. Bridges exist for 3 to 5 tiles.");

        string? Lanes(int side)
        {
            int? height = null;
            foreach (var lane in new[] { 0, side })
            {
                for (int k = -1; k <= length; k++)
                {
                    var (x, y) = vertical ? (tx + lane, ty - up + k) : (tx - left + k, ty + lane);
                    var end = k == -1 || k == length;
                    if (!IsTerrain(x, y))
                        return L.T("Die Brücke würde über den Rand der Insel ragen.", "The bridge would reach past the edge of the island.");
                    var tile = Terrain.GetTile(x, y);
                    if (!end && !tile.UnitModel.IsRiver)
                        return L.T("Der Fluss ist an dieser Stelle nicht gleichmäßig breit. Versuch es ein Stück weiter.", "The river is not equally wide here. Try a bit further along.");
                    if (end && tile.UnitModel != TerrainUnitModel.Base)
                        return L.T("An einem Ende der Brücke ist kein flaches Ufer.", "One end of the bridge has no flat bank.");
                    if (end && height is { } h && h != tile.Elevation)
                        return L.T("Die beiden Ufer liegen unterschiedlich hoch.", "The two banks are at different heights.");
                    if (end)
                        height = tile.Elevation;
                }
            }
            // a bridge spans a river, not the edge of a pond: water has to flow on past both sides
            foreach (var lane in new[] { Math.Min(0, side) - 1, Math.Max(0, side) + 1 })
            {
                for (int k = 0; k < length; k++)
                {
                    var (x, y) = vertical ? (tx + lane, ty - up + k) : (tx - left + k, ty + lane);
                    if (!IsRiver(x, y))
                        return L.T("Neben der Brücke muss auf beiden Seiten noch Wasser sein. Versuch es ein Stück weiter im Fluss.", "There has to be water on both sides of the bridge. Try a bit further along the river.");
                }
            }
            return null;
        }

        // the half of the tile that was tapped picks the second lane, the other side is the fallback
        var side = vertical ? ((absX & 1) == 0 ? -1 : 1) : ((absY & 1) == 0 ? -1 : 1);
        var error = Lanes(side);
        if (error is not null && Lanes(-side) is null)
        {
            side = -side;
            error = null;
        }
        if (error is not null)
            return error;

        var (startX, startY) = vertical ? (Math.Min(tx, tx + side), ty - up) : (tx - left, Math.Min(ty, ty + side));
        var (ax, ay) = manager.ConfigTerrain.GetCoordinatesAbsolute(startX, startY);
        bridge.BuildingType = BuildingType.Bridge;
        bridge.Type = (ushort)Enum.Parse<BridgeType>($"Bridge{style}{length:00}");
        bridge.Angle = (byte)(vertical ? 1 : 0);
        bridge.X = (ushort)(vertical ? (ax * 2) + 2 : (ax * 2) + length);
        bridge.Y = (ushort)(vertical ? (ay * 2) + length : (ay * 2) + 2);

        var (fx, fy, fw, fh) = Footprint(bridge);
        foreach (var other in Buildings.Where(b => b.BuildingType != 0))
        {
            var (ox, oy, ow, oh) = Footprint(other);
            if (fx < ox + ow && ox < fx + fw && fy < oy + oh && oy < fy + fh)
                return L.T("Dort steht schon eine Brücke oder ein Gebäude.", "A bridge or building is already there.");
        }
        return null;
    }

    // ---- buildings and inclines ----

    public const int MaxInclines = 8;

    public static IReadOnlyList<SlopeType> SlopeStyles { get; } =
    [
        SlopeType.SlopeNatural, SlopeType.SlopeWoodBlue, SlopeType.SlopeWood, SlopeType.SlopeWoodStair,
        SlopeType.SlopeStoneStair, SlopeType.SlopeBrickStair, SlopeType.SlopeIronStairBlue, SlopeType.SlopeIronStair,
    ];

    public static string SlopeName(SlopeType type) => type switch
    {
        SlopeType.SlopeNatural => L.T("Natürliche Rampe", "Natural ramp"),
        SlopeType.SlopeWoodBlue => L.T("Blaue Holzrampe", "Blue plank ramp"),
        SlopeType.SlopeWood => L.T("Weiße Holzrampe", "White plank ramp"),
        SlopeType.SlopeWoodStair => L.T("Baumstammtreppe", "Log staircase"),
        SlopeType.SlopeStoneStair => L.T("Steintreppe", "Stone staircase"),
        SlopeType.SlopeBrickStair => L.T("Ziegeltreppe", "Brick staircase"),
        SlopeType.SlopeIronStairBlue => L.T("Blaue Stahltreppe", "Blue steel staircase"),
        SlopeType.SlopeIronStair => L.T("Rote Stahltreppe", "Red steel staircase"),
        _ => type.ToString(),
    };

    public static string BuildingName(Building b) => b.BuildingType switch
    {
        >= BuildingType.PlayerHouse1 and <= BuildingType.PlayerHouse8 =>
            L.T($"Haus von Spieler {b.BuildingType - BuildingType.PlayerHouse1 + 1}", $"House of player {b.BuildingType - BuildingType.PlayerHouse1 + 1}"),
        >= BuildingType.Villager1 and <= BuildingType.Villager10 =>
            L.T($"Bewohnerhaus {b.BuildingType - BuildingType.Villager1 + 1}", $"Villager house {b.BuildingType - BuildingType.Villager1 + 1}"),
        BuildingType.NooksCranny => L.T("Laden von Nepp und Schlepp", "Nook's Cranny"),
        BuildingType.ResidentCenterStructure => L.T("Rathaus", "Resident Services"),
        BuildingType.ResidentCenterTent => L.T("Rathauszelt", "Resident Services tent"),
        BuildingType.Museum => L.T("Museum", "Museum"),
        BuildingType.Airport => L.T("Flughafen", "Airport"),
        BuildingType.AblesSisters => L.T("Schneiderei", "Able Sisters"),
        BuildingType.Campsite => L.T("Campingplatz", "Campsite"),
        BuildingType.Bridge => BridgeStyleName(BridgeInfo(b).Style),
        BuildingType.Incline => SlopeName((SlopeType)b.Type),
        BuildingType.ReddsTreasureTrawler => L.T("Reiners Kutter", "Redd's trawler"),
        BuildingType.Studio => L.T("Studio", "Studio"),
        BuildingType.Hotel => L.T("Hotel", "Hotel"),
        _ => b.BuildingType.ToString(),
    };

    // Which way an incline climbs at each angle is not documented anywhere; this follows buildings facing
    // south at angle 0 and turning anticlockwise like the path pieces. Flipping an incline turns it by 180 degrees.
    private static (int X, int Y) UpDirection(int angle) => (angle & 3) switch
    {
        0 => (0, -1),
        1 => (-1, 0),
        2 => (0, 1),
        _ => (1, 0),
    };

    private bool Overlaps((int X, int Y, int W, int H) area, Building? ignore) => Buildings.Any(o =>
        o.BuildingType != 0 && !ReferenceEquals(o, ignore) && Footprint(o) is var (ox, oy, ow, oh)
        && area.X < ox + ow && ox < area.X + area.W && area.Y < oy + oh && oy < area.Y + area.H);

    /// <summary>
    /// Checks an incline at this position: flat land at the bottom, flat land one level higher at the top,
    /// only land or cliff in between, no water and no other structure. Returns why it does not fit, or null.
    /// </summary>
    public string? CheckIncline(int x, int y, int angle, Building? ignore = null)
    {
        var probe = new Building { BuildingType = BuildingType.Incline, X = (ushort)x, Y = (ushort)y, Angle = (byte)angle };
        var (fx, fy, fw, fh) = Footprint(probe);
        var (ux, uy) = UpDirection(angle);
        int? low = null;
        var cliff = false;
        for (int ix = fx; ix < fx + fw; ix += 2)
        {
            for (int iy = fy; iy < fy + fh; iy += 2)
            {
                if (!TryGetTerrain(ix, iy, out var tx, out var ty) || Terrain.GetAcreTemplate(tx, ty) != 0)
                    return L.T("Die Rampe würde über den Rand der Insel, an den Strand oder ins Meer ragen.", "The incline would reach past the edge of the island, onto the beach or into the sea.");
                var tile = Terrain.GetTile(tx, ty);
                if (tile.UnitModel.IsRiver || tile.UnitModel.IsFall)
                    return L.T("Unter der Rampe wäre Wasser.", "There would be water under the incline.");
                // 0 at the bottom end, 3 at the top end
                var step = ux != 0
                    ? (ux > 0 ? (ix - fx) / 2 : (fx + fw - 2 - ix) / 2)
                    : (uy > 0 ? (iy - fy) / 2 : (fy + fh - 2 - iy) / 2);
                if (step == 0)
                {
                    if (tile.UnitModel != TerrainUnitModel.Base)
                        return L.T("Unten an der Rampe ist kein flacher Boden.", "There is no flat ground at the bottom of the incline.");
                    if (low is { } l && l != tile.Elevation)
                        return L.T("Der Boden unten ist nicht gleich hoch.", "The ground at the bottom is not level.");
                    low = tile.Elevation;
                }
                cliff |= tile.UnitModel.IsCliff;
            }
        }
        // both lanes have to look the same at every step, so the cliff runs straight across
        var lanes = new (bool Cliff, int Elevation)?[4];
        for (int ix = fx; ix < fx + fw; ix += 2)
        {
            for (int iy = fy; iy < fy + fh; iy += 2)
            {
                TryGetTerrain(ix, iy, out var tx, out var ty);
                var tile = Terrain.GetTile(tx, ty);
                var step = ux != 0
                    ? (ux > 0 ? (ix - fx) / 2 : (fx + fw - 2 - ix) / 2)
                    : (uy > 0 ? (iy - fy) / 2 : (fy + fh - 2 - iy) / 2);
                var look = (tile.UnitModel.IsCliff, (int)tile.Elevation);
                if (lanes[step] is { } other && other != look)
                    return L.T("Die Klippe verläuft hier nicht gerade.", "The cliff does not run straight here.");
                lanes[step] = look;
                if (step == 3 && (tile.UnitModel != TerrainUnitModel.Base || tile.Elevation != low + 1))
                    return L.T("Oben an der Rampe ist kein flacher Boden eine Ebene höher.", "There is no flat ground one level higher at the top of the incline.");
                if (tile.Elevation != low && tile.Elevation != low + 1)
                    return L.T("Die Rampe kann nur eine Ebene überwinden.", "An incline can only climb one level.");
            }
        }
        if (!cliff)
            return L.T("Hier ist keine Klippe, über die die Rampe führen könnte.", "There is no cliff here for the incline to climb.");
        if (low >= 2)
            return L.T("Auf die oberste Ebene führen im Spiel keine Rampen.", "The game has no inclines up to the top level.");
        if (Overlaps(Footprint(probe), ignore))
            return InclineOnStructure;
        for (int iy = fy; iy < fy + fh; iy++)
        {
            for (int ix = fx; ix < fx + fw; ix++)
            {
                if (TryGetRelative(ix, iy, out var rx, out var ry) && !Layer(0).GetTile(rx, ry).IsNone)
                    return InclineOccupied;
            }
        }
        return null;
    }

    private static string InclineOnStructure => L.T("Dort steht schon ein Gebäude, eine Brücke oder eine Rampe.", "A building, bridge or incline is already there.");

    private static string InclineOccupied => L.T("Hier würde eine Rampe passen, aber auf ihrer Fläche liegt oder wächst etwas. Räum es erst weg.",
                                                 "An incline would fit here, but something lies or grows where it would go. Clear it first.");

    /// <summary>Finds an incline over the cliff next to the tapped tile, in any direction that fits.</summary>
    public string? PlanIncline(int absX, int absY, SlopeType style, out Building incline)
    {
        incline = new Building();
        if (Buildings.Count(b => b.BuildingType == BuildingType.Incline) >= MaxInclines)
            return L.T($"Die Insel hat schon {MaxInclines} Rampen, mehr erlaubt das Spiel nicht.", $"The island already has {MaxInclines} inclines, the game allows no more.");
        if (!TryGetTerrain(absX, absY, out _, out _))
            return L.T("Tippe auf den Boden vor einer Klippe.", "Tap the ground in front of a cliff.");
        var (tapX, tapY) = (absX >> 1, absY >> 1); // absolute terrain tile
        // the ground would fit but something is in the way: worth telling instead of "no incline fits"
        var blocked = new[] { InclineOccupied, InclineOnStructure };
        string? blocker = null;
        for (int angle = 0; angle < 4; angle++)
        {
            var (ux, uy) = UpDirection(angle);
            // the half of the tapped tile picks the second lane, the other side is the fallback
            var preferred = (uy != 0 ? absX : absY) % 2 == 0 ? -1 : 1;
            foreach (var side in new[] { preferred, -preferred })
            {
                for (int k = 0; k < 4; k++)
                {
                    // the tapped tile is k steps above the bottom end
                    var (bx, by) = (tapX - (ux * k), tapY - (uy * k));
                    var (lx, ly) = uy != 0 ? (side, 0) : (0, side);
                    var xs = new[] { bx, bx + (ux * 3), bx + lx, bx + lx + (ux * 3) };
                    var ys = new[] { by, by + (uy * 3), by + ly, by + ly + (uy * 3) };
                    var (minX, minY) = (xs.Min(), ys.Min());
                    var (w, h) = uy != 0 ? (2, 4) : (4, 2);
                    var x = (minX * 2) + w;
                    var y = (minY * 2) + h;
                    var error = CheckIncline(x, y, angle);
                    if (error is not null && blocked.Contains(error))
                        blocker ??= error;
                    if (error is null)
                    {
                        incline.BuildingType = BuildingType.Incline;
                        incline.X = (ushort)x;
                        incline.Y = (ushort)y;
                        incline.Angle = (byte)angle;
                        incline.Type = (ushort)style;
                        return null;
                    }
                }
            }
        }
        return blocker ?? L.T("Hier passt keine Rampe. Tippe auf flachen Boden direkt vor einer Klippe, die eine Ebene hoch ist.",
                              "No incline fits here. Tap flat ground right in front of a cliff that is one level high.");
    }

    /// <summary>Checks moving a building to a new centre; bridges are rebuilt in the bridge mode instead.</summary>
    public string? CheckMove(Building building, int x, int y)
    {
        if (building.BuildingType == BuildingType.Bridge)
            return L.T("Brücken reißt du ab und baust sie im Modus Brücken neu, damit sie über das Wasser passen.",
                       "Remove a bridge and build it again in the bridge mode, so that it spans the water.");
        if (building.BuildingType == BuildingType.Incline)
            return CheckIncline(x, y, building.Angle, building);
        if (!TryGetTerrain(x, y, out var tx, out var ty) || Terrain.GetAcreTemplate(tx, ty) != 0)
            return L.T("Dort ist kein Land, sondern Strand oder Meer.", "That is no land but beach or sea.");
        var tile = Terrain.GetTile(tx, ty);
        if (tile.UnitModel != TerrainUnitModel.Base)
            return L.T("Dort ist Wasser oder eine Klippe.", "There is water or a cliff there.");
        var probe = new Building();
        probe.CopyFrom(building);
        probe.X = (ushort)x;
        probe.Y = (ushort)y;
        if (Overlaps(Footprint(probe), building))
            return L.T("Dort steht schon ein anderes Gebäude.", "Another building is already there.");
        return null;
    }

    /// <summary>Puts a planned bridge into the first free building slot.</summary>
    public bool AddBuilding(Building building)
    {
        var free = Buildings.FirstOrDefault(b => b.BuildingType == 0);
        if (free is null)
            return false;
        free.CopyFrom(building);
        return true;
    }

    public LayerTerrain Terrain => manager.LayerTerrain;

    /// <summary>The terrain tile under an absolute item tile (a terrain tile covers 2 x 2 item tiles).</summary>
    public bool TryGetTerrain(int absX, int absY, out int relX, out int relY)
    {
        (relX, relY) = manager.ConfigTerrain.GetCoordinatesRelative(absX >> 1, absY >> 1);
        return manager.ConfigTerrain.IsCoordinateValidRelative(relX, relY);
    }

    private bool IsTerrain(int relX, int relY) => manager.ConfigTerrain.IsCoordinateValidRelative(relX, relY);

    // ---- NHSE's file formats: .nhl items, .nht terrain, .nhb buildings ----

    public static string SizeError(int have, int expect) =>
        L.T($"Die Datei hat {have} Bytes, erwartet sind {expect}.", $"The file has {have} bytes, {expect} are expected.");

    public byte[] DumpItems(int layer) => Layer(layer).DumpAll();

    public string? ImportItems(int layer, byte[] data)
    {
        var target = Layer(layer);
        var expect = target.TileInfo.TotalCount * Item.SIZE;
        if (data.Length != expect && !FieldItemUpgrade.IsUpdateNeeded(data.Length, expect))
            return SizeError(data.Length, expect);
        FieldItemUpgrade.DetectUpdate(ref data, expect);
        target.ImportAll(data);
        return null;
    }

    /// <summary>One acre of items (32 x 32 tiles) starting at the absolute tile, like NHSE's view.</summary>
    public byte[] DumpItemsAcre(int layer, int absX, int absY)
    {
        var (relX, relY) = Config.GetCoordinatesRelative(absX, absY);
        return Layer(layer).DumpAcre(relX, relY);
    }

    public string? ImportItemsAcre(int layer, int absX, int absY, byte[] data)
    {
        var target = Layer(layer);
        var expect = target.TileInfo.ViewCount * Item.SIZE;
        if (data.Length != expect)
            return SizeError(data.Length, expect);
        var (relX, relY) = Config.GetCoordinatesRelative(absX, absY);
        target.ImportAcre(data, relX, relY);
        return null;
    }

    public byte[] DumpTerrain() => Terrain.DumpAll();

    public string? ImportTerrain(byte[] data)
    {
        var expect = Terrain.TileInfo.TotalCount * TerrainTile.SIZE;
        if (data.Length != expect)
            return SizeError(data.Length, expect);
        Terrain.ImportAll(data);
        return null;
    }

    /// <summary>One acre of terrain (16 x 16 terrain tiles) starting under the absolute item tile.</summary>
    public byte[] DumpTerrainAcre(int absX, int absY)
    {
        var (relX, relY) = manager.ConfigTerrain.GetCoordinatesRelative(absX / 2, absY / 2);
        return Terrain.DumpAcre(relX, relY);
    }

    public string? ImportTerrainAcre(int absX, int absY, byte[] data)
    {
        var expect = Terrain.TileInfo.ViewCount * TerrainTile.SIZE;
        if (data.Length != expect)
            return SizeError(data.Length, expect);
        var (relX, relY) = manager.ConfigTerrain.GetCoordinatesRelative(absX / 2, absY / 2);
        Terrain.ImportAcre(data, relX, relY);
        return null;
    }

    public byte[] DumpBuildings() => Building.SetArray(Buildings);

    public string? ImportBuildings(byte[] data)
    {
        // 46 since 2.0, 40 before
        if (data.Length is not (Building.SIZE * MainSaveOffsets.BuildingCount or Building.SIZE * 40))
            return SizeError(data.Length, Building.SIZE * MainSaveOffsets.BuildingCount);
        var list = Building.GetArray(data);
        for (int i = 0; i < list.Length && i < Buildings.Count; i++)
            Buildings[i].CopyFrom(list[i]);
        return null;
    }

    // ---- terraforming ----

    public enum Landform { Raise, Lower, Water, Land }

    /// <summary>The highest cliff level the game builds (ground is level 0).</summary>
    public const int MaxElevation = 3;

    /// <summary>Rounded outer corners (3B) like the island's own cliffs and rivers, or angular ones (3C).</summary>
    public bool RoundCorners { get; set; } = true;

    private bool IsInlandAround(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!IsTerrain(x + dx, y + dy) || Terrain.GetAcreTemplate(x + dx, y + dy) != 0)
                    return false;
            }
        }
        return true;
    }

    private bool NearFall(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (IsTerrain(x + dx, y + dy) && Terrain.GetTile(x + dx, y + dy).UnitModel.IsFall)
                    return true;
            }
        }
        return false;
    }

    // a terrain tile covers 2 x 2 item tiles; buildings must not stand on changed ground, bridges and
    // inclines also keep a free ring around them because they rest on the ground next to them
    private bool NearStructure(int x, int y)
    {
        var (ax, ay) = manager.ConfigTerrain.GetCoordinatesAbsolute(x, y);
        var tile = (X: ax * 2, Y: ay * 2, W: 2, H: 2);
        foreach (var b in Buildings.Where(b => b.BuildingType != 0))
        {
            var (bx, by, bw, bh) = Footprint(b);
            var ring = b.BuildingType is BuildingType.Bridge or BuildingType.Incline ? 2 : 0;
            if (tile.X < bx + bw + ring && bx - ring < tile.X + tile.W && tile.Y < by + bh + ring && by - ring < tile.Y + tile.H)
                return true;
        }
        return false;
    }

    // the game keeps neighbouring tiles at most one level apart and never lets a river run next to lower ground
    private bool FitsAround(int x, int y)
    {
        for (int cy = y - 1; cy <= y + 1; cy++)
        {
            for (int cx = x - 1; cx <= x + 1; cx++)
            {
                if (!IsTerrain(cx, cy))
                    continue;
                var c = Terrain.GetTile(cx, cy);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!IsTerrain(cx + dx, cy + dy))
                            continue;
                        var n = Terrain.GetTile(cx + dx, cy + dy);
                        if (Math.Abs(n.Elevation - c.Elevation) > 1)
                            return false;
                        if (c.UnitModel.IsRiver && n.Elevation < c.Elevation)
                            return false;
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Raises or lowers one terrain tile to <paramref name="level"/>, digs a river into it or fills one in,
    /// like the island designer. The pieces are only placeholders until <see cref="ShapeTerrain"/> runs.
    /// Returns why nothing happened, or null when the tile changed.
    /// </summary>
    public string? ChangeLand(int x, int y, Landform action, int level)
    {
        if (!IsInlandAround(x, y))
            return L.T("Am Strand und an der Küste lässt sich das Gelände nicht ändern.", "The ground cannot be changed on the beach and at the coast.");
        if (NearFall(x, y))
            return L.T("Wasserfälle und ihre Umgebung bleiben, wie sie sind.", "Waterfalls and the ground around them stay as they are.");
        if (NearStructure(x, y))
            return L.T("Unter und neben Gebäuden, Brücken und Rampen bleibt das Gelände, wie es ist.", "The ground under and next to buildings, bridges and inclines stays as it is.");
        var tile = Terrain.GetTile(x, y);
        var (model, elevation) = (tile.UnitModel, tile.Elevation);
        var river = model.IsRiver;
        switch (action)
        {
            case Landform.Raise when river || tile.Elevation != level - 1 || level > MaxElevation:
                return river ? L.T("Im Fluss lässt sich keine Klippe bauen.", "No cliff can be built in the river.")
                    : level > MaxElevation ? L.T("Höher geht es im Spiel nicht.", "The game does not go any higher.") : string.Empty;
            case Landform.Lower when river || tile.Elevation != level + 1 || level < 0:
                return river ? L.T("Den Fluss schüttest du erst zu.", "Fill the river in first.")
                    : level < 0 ? L.T("Tiefer geht es nicht.", "It does not go any lower.") : string.Empty;
            case Landform.Water when river:
            case Landform.Land when !river:
                return string.Empty;
            case Landform.Raise:
                tile.Elevation = (ushort)level;
                break;
            case Landform.Lower:
                tile.Elevation = (ushort)level;
                break;
            case Landform.Water:
                tile.UnitModel = TerrainUnitModel.River8A;
                break;
            case Landform.Land:
                tile.UnitModel = TerrainUnitModel.Base;
                break;
        }
        if (!FitsAround(x, y))
        {
            tile.UnitModel = model;
            tile.Elevation = elevation;
            return action switch
            {
                Landform.Water => L.T("Ein Fluss braucht ringsum Boden auf seiner Höhe, sonst bräuchte er einen Wasserfall.", "A river needs ground at its own level all around, otherwise it would need a waterfall."),
                _ => L.T("Klippen gehen immer nur eine Ebene hoch, und neben einem Fluss darf es nicht tiefer werden.", "Cliffs only ever rise by one level, and the ground next to a river must not drop."),
            };
        }
        if (action == Landform.Water)
            ClearItemsOnTerrain(x, y);
        return null;
    }

    // nothing can stay in the water: removes whatever lies on the 2 x 2 item tiles of a terrain tile, on both layers
    private void ClearItemsOnTerrain(int x, int y)
    {
        var (ax, ay) = manager.ConfigTerrain.GetCoordinatesAbsolute(x, y);
        foreach (var layer in new[] { Layer(0), Layer(1) })
        {
            for (int iy = ay * 2; iy < (ay * 2) + 2; iy++)
            {
                for (int ix = ax * 2; ix < (ax * 2) + 2; ix++)
                {
                    if (!TryGetRelative(ix, iy, out var rx, out var ry))
                        continue;
                    var t = layer.GetTile(rx, ry);
                    if (t.IsNone)
                        continue;
                    if (t.IsExtension)
                    {
                        rx -= t.ExtensionX;
                        ry -= t.ExtensionY;
                        layer.TileInfo.ClampInside(ref rx, ref ry);
                        t = layer.GetTile(rx, ry);
                    }
                    if (t.IsRoot)
                        layer.DeleteExtensionTiles(t, rx, ry);
                    t.Delete();
                }
            }
        }
    }

    /// <summary>
    /// Picks the fitting cliff and river pieces for every tile in the area and the ring around it,
    /// from which neighbours are on the same level (cliffs) or water (rivers). Paths cannot lie on
    /// cliffs or water, so they are taken off there and their neighbours reshaped.
    /// </summary>
    public void ShapeTerrain(int x0, int y0, int x1, int y1)
    {
        for (int y = y0 - 1; y <= y1 + 1; y++)
        {
            for (int x = x0 - 1; x <= x1 + 1; x++)
            {
                if (!IsTerrain(x, y) || Terrain.GetAcreTemplate(x, y) != 0)
                    continue;
                var tile = Terrain.GetTile(x, y);
                var m = tile.UnitModel;
                if (m.IsFall || !(m.IsRiver || m.IsCliff || m == TerrainUnitModel.Base))
                    continue;
                var (cx, cy, e) = (x, y, tile.Elevation);
                string name;
                ushort angle;
                if (m.IsRiver)
                {
                    var (suffix, a) = RoadShapes.Shape((dx, dy) => !IsTerrain(cx + dx, cy + dy)
                        || Terrain.GetTile(cx + dx, cy + dy).UnitModel is var n && (n.IsRiver || n.IsFall));
                    (name, angle) = ($"River{Corner(suffix)}", a);
                }
                else
                {
                    var (suffix, a) = RoadShapes.Shape((dx, dy) => !IsTerrain(cx + dx, cy + dy) || Terrain.GetTile(cx + dx, cy + dy).Elevation >= e);
                    (name, angle) = suffix == "8A" ? ("Base", (ushort)0) : ($"Cliff{Corner(suffix)}", a);
                }
                // keep a piece that already fits, with its own corner style and look
                if (tile.LandMakingAngle == angle && SamePiece(m, name))
                    continue;
                var model = Enum.Parse<TerrainUnitModel>(name);
                tile.Variation = 0;
                tile.UnitModel = model;
                tile.LandMakingAngle = angle;
                if (model != TerrainUnitModel.Base && tile.UnitModelRoad != 0)
                {
                    tile.UnitModelRoad = 0;
                    tile.VariationRoad = 0;
                    tile.LandMakingAngleRoad = 0;
                }
            }
        }
        ShapePaths(x0 - 2, y0 - 2, x1 + 2, y1 + 2);
    }

    private string Corner(string suffix) => suffix == "3C" && RoundCorners ? "3B" : suffix;

    private static bool SamePiece(TerrainUnitModel model, string name)
    {
        var have = model.ToString().Replace("_2", string.Empty).Replace("_3", string.Empty);
        return have == name || (have[^2..] is "3B" or "3C" && name[^2..] is "3B" or "3C" && have[..^2] == name[..^2]);
    }

    /// <summary>Paths go on flat ground of the island itself: no river, cliff, waterfall or beach.</summary>
    public bool CanHavePath(int relX, int relY) =>
        Terrain.GetTile(relX, relY).UnitModel == TerrainUnitModel.Base && Terrain.GetAcreTemplate(relX, relY) == 0;

    /// <summary>
    /// Lays a path of the given type on a terrain tile, or removes it when the type is null.
    /// The piece is only a placeholder until <see cref="ShapePaths"/> picks the fitting one.
    /// </summary>
    public bool SetPath(int relX, int relY, string? type)
    {
        var tile = Terrain.GetTile(relX, relY);
        if (type is null)
        {
            if (tile.UnitModelRoad == TerrainUnitModel.Base)
                return false;
            tile.UnitModelRoad = TerrainUnitModel.Base;
            tile.VariationRoad = 0;
            tile.LandMakingAngleRoad = 0;
            return true;
        }
        if (!CanHavePath(relX, relY) || RoadShapes.TypeOf(tile) == type)
            return false;
        tile.UnitModelRoad = Enum.Parse<TerrainUnitModel>($"Road{type}8A");
        tile.VariationRoad = 0;
        tile.LandMakingAngleRoad = 0;
        return true;
    }

    /// <summary>Picks the fitting piece for every path tile in the area and the ring of tiles around it.</summary>
    public void ShapePaths(int relX0, int relY0, int relX1, int relY1)
    {
        for (int y = relY0 - 1; y <= relY1 + 1; y++)
        {
            for (int x = relX0 - 1; x <= relX1 + 1; x++)
            {
                if (!IsTerrain(x, y))
                    continue;
                var tile = Terrain.GetTile(x, y);
                if (RoadShapes.TypeOf(tile) is not { } type)
                    continue;
                var (cx, cy) = (x, y);
                var (model, angle) = RoadShapes.Piece(type, (dx, dy) =>
                    IsTerrain(cx + dx, cy + dy) && RoadShapes.TypeOf(Terrain.GetTile(cx + dx, cy + dy)) == type);
                tile.UnitModelRoad = model;
                tile.LandMakingAngleRoad = angle;
            }
        }
    }

    /// <summary>
    /// RGBA pixels of the ground for a block of terrain tiles (relative coordinates, edges included),
    /// with the absolute item tile it starts on, for redrawing after path edits.
    /// </summary>
    public (int X, int Y, int W, int H, byte[] Pixels) TerrainPatch(int relX0, int relY0, int relX1, int relY1)
    {
        var (ax0, ay0) = manager.ConfigTerrain.GetCoordinatesAbsolute(relX0 - 1, relY0 - 1);
        var (ax1, ay1) = manager.ConfigTerrain.GetCoordinatesAbsolute(relX1 + 1, relY1 + 1);
        ax0 = Math.Max(0, ax0);
        ay0 = Math.Max(0, ay0);
        ax1 = Math.Min(manager.ConfigTerrain.MapTotalWidth - 1, ax1);
        ay1 = Math.Min(manager.ConfigTerrain.MapTotalHeight - 1, ay1);
        var w = ax1 - ax0 + 1;
        var h = ay1 - ay0 + 1;
        return (ax0 * 2, ay0 * 2, w * 2, h * 2, TerrainPixels(ax0, ay0, w, h));
    }

    /// <summary>
    /// RGBA pixels of the ground, one pixel per item tile. Same rules as NHSE's TerrainSprite:
    /// the editable acres from their terrain tiles, the rest (sea, beach) from the acre templates.
    /// </summary>
    public byte[] TerrainPixels() => TerrainPixels(0, 0, manager.ConfigTerrain.MapTotalWidth, manager.ConfigTerrain.MapTotalHeight);

    /// <inheritdoc cref="TerrainPixels()"/>
    /// <remarks>For a block of absolute terrain tiles; the result is twice as wide and high.</remarks>
    private byte[] TerrainPixels(int tx0, int ty0, int tw, int th)
    {
        var terrain = manager.LayerTerrain;
        var cfg = manager.ConfigTerrain;
        var width = tw * 2;
        var pixels = new byte[width * th * 2 * 4];
        for (int ty = ty0; ty < ty0 + th; ty++)
        {
            for (int tx = tx0; tx < tx0 + tw; tx++)
            {
                var (relX, relY) = cfg.GetCoordinatesRelative(tx, ty);
                var inside = cfg.IsCoordinateValidRelative(relX, relY);
                ushort template = 0;
                if (!inside)
                {
                    var (acreX, acreY) = cfg.GetCoordinatesRelative(tx - (tx % 16), ty - (ty % 16));
                    template = terrain.GetAcreTemplate(acreX, acreY);
                }
                // a terrain tile covers 2 x 2 item tiles: sample each quarter of its 16 px appearance
                for (int sy = 0; sy < 2; sy++)
                {
                    for (int sx = 0; sx < 2; sx++)
                    {
                        var argb = inside
                            ? terrain.GetTileColor(relX, relY, (sx * 8) + 4, (sy * 8) + 4)
                            : AcreTileColor.GetAcreTileColor(template, tx % 16, ty % 16);
                        if (argb == -0x1000000) // transparent (dynamic) outside the land: invalid anyway
                            argb = Color.ForestGreen.ToArgb();
                        Put(pixels, width, ((tx - tx0) * 2) + sx, ((ty - ty0) * 2) + sy, argb);
                    }
                }
            }
        }
        return pixels;
    }

    /// <summary>RGBA pixels of a layer's items in NHSE's map colors, transparent where nothing lies.</summary>
    public byte[] ItemPixels(int layerIndex)
    {
        var layer = Layer(layerIndex);
        var pixels = new byte[Width * Height * 4];
        for (int relY = 0; relY < layer.TileInfo.TotalHeight; relY++)
        {
            for (int relX = 0; relX < layer.TileInfo.TotalWidth; relX++)
            {
                var tile = layer.GetTile(relX, relY);
                if (tile.IsNone)
                    continue;
                var (x, y) = GetAbsolute(relX, relY);
                if ((uint)x < Width && (uint)y < Height)
                    Put(pixels, Width, x, y, FieldItemColor.GetItemColor(tile).ToArgb());
            }
        }
        return pixels;
    }

    private static void Put(byte[] pixels, int width, int x, int y, int argb)
    {
        var i = ((y * width) + x) * 4;
        pixels[i] = (byte)(argb >> 16);
        pixels[i + 1] = (byte)(argb >> 8);
        pixels[i + 2] = (byte)argb;
        pixels[i + 3] = (byte)(argb >>> 24);
    }

    /// <summary>Width and height in tiles of an item as placed, rotation included.</summary>
    public static (int W, int H) Footprint(Item item)
    {
        var size = ItemInfo.GetItemSize(item);
        var (w, h) = (size.Width, size.Height);
        return (item.Rotation & 1) == 1 ? (h, w) : (w, h);
    }
}
