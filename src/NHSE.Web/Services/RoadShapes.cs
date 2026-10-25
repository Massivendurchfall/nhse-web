using System.Text.RegularExpressions;
using NHSE.Core;

namespace NHSE.Web.Services;

/// <summary>
/// Picks the right path piece for a terrain tile from its neighbours, like the game's path tool.
/// The pieces are named by how many sides and corners connect (Road…0A to Road…8A) and turned in
/// 90 degree steps. The shapes come from NHSE's river colouring (TerrainTileColor), whose rivers use
/// the same naming: e.g. 1A connects to the bottom only, 2C is a corner to the right and bottom,
/// and every angle step turns the piece a quarter anticlockwise.
/// </summary>
public static partial class RoadShapes
{
    public static IReadOnlyList<string> Types { get; } = ["Stone", "Brick", "Soil", "DarkSoil", "Sand", "Tile", "FanPattern", "Wood"];

    public static string TypeName(string type) => type switch
    {
        "Stone" => L.T("Steinweg", "Stone path"),
        "Brick" => L.T("Ziegelweg", "Brick path"),
        "Soil" => L.T("Erdweg", "Dirt path"),
        "DarkSoil" => L.T("Dunkler Erdweg", "Dark dirt path"),
        "Sand" => L.T("Sandweg", "Sand path"),
        "Tile" => L.T("Terrakottaweg", "Terra cotta path"),
        "FanPattern" => L.T("Bogenpflaster", "Arched tile path"),
        "Wood" => L.T("Holzweg", "Wooden path"),
        _ => type,
    };

    /// <summary>Colour of the path type on the map, the same NHSE uses.</summary>
    public static string TypeColor(string type) => type switch
    {
        "Stone" => "#a9a9a9",
        "Brick" => "#b22222",
        "Soil" => "#cd853f",
        "DarkSoil" => "#8b4513",
        "Sand" => "#f4a460",
        "Tile" => "#4682b4",
        "FanPattern" => "#fffff0",
        _ => "#deb887",
    };

    private const int Up = 1, Right = 2, Down = 4, Left = 8, UpLeft = 16, UpRight = 32, DownLeft = 64, DownRight = 128;

    // every piece in its unturned form, as the set of neighbours that are path
    private static readonly (string Suffix, int Mask)[] Pieces =
    [
        ("0A", 0),
        ("1A", Down),
        ("2A", Up | Down),
        ("2C", Right | Down),
        ("3A", Up | Right | Down),
        ("3C", Right | Down | DownRight),
        ("4A", Up | Right | Down | DownRight),
        ("4B", Up | Right | Down | UpRight),
        ("4C", Up | Right | Down | Left),
        ("5A", Up | Right | Down | Left | DownLeft),
        ("5B", Up | Right | Down | UpRight | DownRight),
        ("6A", Up | Right | Down | Left | UpRight | DownLeft),
        ("6B", Up | Right | Down | Left | UpLeft | UpRight),
        ("7A", Up | Right | Down | Left | UpLeft | UpRight | DownRight),
        ("8A", Up | Right | Down | Left | UpLeft | UpRight | DownLeft | DownRight),
    ];

    private static readonly Dictionary<int, (string Suffix, ushort Angle)> Lookup = Build();

    private static Dictionary<int, (string, ushort)> Build()
    {
        var lookup = new Dictionary<int, (string, ushort)>();
        foreach (var (suffix, mask) in Pieces)
        {
            var m = mask;
            for (ushort angle = 0; angle < 4; angle++)
            {
                lookup.TryAdd(m, (suffix, angle));
                m = TurnAnticlockwise(m);
            }
        }
        return lookup;
    }

    private static int TurnAnticlockwise(int m)
    {
        int r = 0;
        if ((m & Up) != 0) r |= Left;
        if ((m & Left) != 0) r |= Down;
        if ((m & Down) != 0) r |= Right;
        if ((m & Right) != 0) r |= Up;
        if ((m & UpLeft) != 0) r |= DownLeft;
        if ((m & DownLeft) != 0) r |= DownRight;
        if ((m & DownRight) != 0) r |= UpRight;
        if ((m & UpRight) != 0) r |= UpLeft;
        return r;
    }

    /// <summary>The path type of a terrain tile ("Stone", "Brick", ...), or null without a path.</summary>
    public static string? TypeOf(TerrainTile tile)
    {
        if (!tile.UnitModelRoad.IsRoad)
            return null;
        var m = PieceName().Match(tile.UnitModelRoad.ToString());
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// The piece for a path tile whose neighbours are path where <paramref name="isPath"/> says so
    /// (dx, dy from -1 to 1). Corners only count when both sides next to them are path.
    /// </summary>
    public static (TerrainUnitModel Model, ushort Angle) Piece(string type, Func<int, int, bool> isPath)
    {
        int mask = 0;
        if (isPath(0, -1)) mask |= Up;
        if (isPath(1, 0)) mask |= Right;
        if (isPath(0, 1)) mask |= Down;
        if (isPath(-1, 0)) mask |= Left;
        if ((mask & (Up | Left)) == (Up | Left) && isPath(-1, -1)) mask |= UpLeft;
        if ((mask & (Up | Right)) == (Up | Right) && isPath(1, -1)) mask |= UpRight;
        if ((mask & (Down | Left)) == (Down | Left) && isPath(-1, 1)) mask |= DownLeft;
        if ((mask & (Down | Right)) == (Down | Right) && isPath(1, 1)) mask |= DownRight;
        var (suffix, angle) = Lookup[mask];
        return (Enum.Parse<TerrainUnitModel>($"Road{type}{suffix}"), angle);
    }

    [GeneratedRegex("^Road([A-Za-z]+)[0-8][A-C]$")]
    private static partial Regex PieceName();
}
