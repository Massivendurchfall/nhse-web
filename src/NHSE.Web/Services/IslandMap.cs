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
