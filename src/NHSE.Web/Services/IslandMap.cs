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

    /// <summary>Writes both item layers and the terrain (paths) back into main.dat. Buildings are not edited here.</summary>
    public void WriteTo(MainSave save)
    {
        save.SetFieldItemLayer0(Layer(0).Tiles);
        save.SetFieldItemLayer1(Layer(1).Tiles);
        save.SetTerrainTiles(Terrain.Tiles);
    }

    public sealed record State(ulong[] Items, byte[] Terrain);

    public State Snapshot() => new([.. Layer(0).Tiles.Select(t => t.RawValue), .. Layer(1).Tiles.Select(t => t.RawValue)], Terrain.DumpAll());

    public void Restore(State snapshot)
    {
        var first = Layer(0).Tiles;
        var second = Layer(1).Tiles;
        for (int i = 0; i < first.Length; i++)
            first[i].RawValue = snapshot.Items[i];
        for (int i = 0; i < second.Length; i++)
            second[i].RawValue = snapshot.Items[first.Length + i];
        Terrain.ImportAll(snapshot.Terrain);
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
