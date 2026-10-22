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

    /// <summary>Writes both item layers back into main.dat. Terrain and buildings are not edited here.</summary>
    public void WriteTo(MainSave save)
    {
        save.SetFieldItemLayer0(Layer(0).Tiles);
        save.SetFieldItemLayer1(Layer(1).Tiles);
    }

    public ulong[] Snapshot() => [.. Layer(0).Tiles.Select(t => t.RawValue), .. Layer(1).Tiles.Select(t => t.RawValue)];

    public void Restore(ulong[] snapshot)
    {
        var first = Layer(0).Tiles;
        var second = Layer(1).Tiles;
        for (int i = 0; i < first.Length; i++)
            first[i].RawValue = snapshot[i];
        for (int i = 0; i < second.Length; i++)
            second[i].RawValue = snapshot[first.Length + i];
    }

    /// <summary>
    /// RGBA pixels of the ground, one pixel per item tile. Same rules as NHSE's TerrainSprite:
    /// the editable acres from their terrain tiles, the rest (sea, beach) from the acre templates.
    /// </summary>
    public byte[] TerrainPixels()
    {
        var terrain = manager.LayerTerrain;
        var cfg = manager.ConfigTerrain;
        var pixels = new byte[Width * Height * 4];
        for (int ty = 0; ty < cfg.MapTotalHeight; ty++)
        {
            for (int tx = 0; tx < cfg.MapTotalWidth; tx++)
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
                        Put(pixels, (tx * 2) + sx, (ty * 2) + sy, argb);
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
                Put(pixels, x, y, FieldItemColor.GetItemColor(tile).ToArgb());
            }
        }
        return pixels;
    }

    private void Put(byte[] pixels, int x, int y, int argb)
    {
        if ((uint)x >= Width || (uint)y >= Height)
            return;
        var i = ((y * Width) + x) * 4;
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
