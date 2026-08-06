using Microsoft.Xna.Framework;

namespace RedBallGame;

public enum Direction
{
    Up,
    Down,
    Left,
    Right,
}

public static class GameConfig
{
    public const int ScreenWidth = 1080;
    public const int ScreenHeight = 900;
    public const int WallThickness = 40;
    public const int ExitWidth = 120;

    public static readonly Direction[] AllDirections =
    {
        Direction.Up, Direction.Down, Direction.Left, Direction.Right,
    };

    public static Direction Opposite(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => Direction.Left,
    };
}

/// <summary>
/// A single dungeon room. Only knows which of the four walls have an exit.
/// Rooms are generated fresh each time the player enters them ("fog of war").
/// </summary>
public class Room
{
    private readonly HashSet<Direction> _exits;
    private readonly List<Rectangle> _interiorWalls;

    public Room(IEnumerable<Direction> exits, IEnumerable<Rectangle> interiorWalls)
    {
        _exits = new HashSet<Direction>(exits);
        _interiorWalls = new List<Rectangle>(interiorWalls);
        SolidWalls = BuildSolidWalls(_exits, _interiorWalls);
    }

    public IReadOnlyCollection<Direction> Exits => _exits;

    /// <summary>
    /// Every solid rectangle in the room: the perimeter walls (with the exit
    /// gaps cut out) plus the interior walls. Single source for collision and
    /// drawing, so what you see is always what you bump into.
    /// </summary>
    public IReadOnlyList<Rectangle> SolidWalls { get; }

    public bool HasExit(Direction d) => _exits.Contains(d);

    /// <summary>
    /// Builds a room with 1-4 exits. If the player walked in through
    /// <paramref name="enteredFrom"/>, the opposite wall is always kept open
    /// so you can always step back the way you came.
    /// </summary>
    public static Room GenerateRandom(Random rng, Direction? enteredFrom)
    {
        var exits = new HashSet<Direction>();
        if (enteredFrom.HasValue)
        {
            exits.Add(GameConfig.Opposite(enteredFrom.Value));
        }

        foreach (var d in GameConfig.AllDirections)
        {
            if (!exits.Contains(d) && rng.Next(2) == 0)
            {
                exits.Add(d);
            }
        }

        if (exits.Count == 0)
        {
            exits.Add(GameConfig.AllDirections[rng.Next(GameConfig.AllDirections.Length)]);
        }

        var walls = GenerateInteriorWalls(rng, exits, protectSpawn: enteredFrom == null);
        return new Room(exits, walls);
    }

    /// <summary>
    /// The rectangular opening cut into a wall for the given exit.
    /// </summary>
    public static Rectangle GetExitRect(Direction d)
    {
        int half = GameConfig.ExitWidth / 2;
        int cx = GameConfig.ScreenWidth / 2;
        int cy = GameConfig.ScreenHeight / 2;
        int w = GameConfig.WallThickness;

        return d switch
        {
            Direction.Left => new Rectangle(0, cy - half, w, GameConfig.ExitWidth),
            Direction.Right => new Rectangle(GameConfig.ScreenWidth - w, cy - half, w, GameConfig.ExitWidth),
            Direction.Up => new Rectangle(cx - half, 0, GameConfig.ExitWidth, w),
            _ => new Rectangle(cx - half, GameConfig.ScreenHeight - w, GameConfig.ExitWidth, w),
        };
    }

    /// <summary>
    /// Where the ball should appear in a room right after entering through
    /// the exit <paramref name="d"/> (just inside the wall, centered in the gap).
    /// </summary>
    public static Vector2 GetEntryPoint(Direction d, float playerRadius)
    {
        int w = GameConfig.WallThickness;
        float r = playerRadius;

        return d switch
        {
            Direction.Left => new Vector2(w + r + 1, GameConfig.ScreenHeight / 2f),
            Direction.Right => new Vector2(GameConfig.ScreenWidth - w - r - 1, GameConfig.ScreenHeight / 2f),
            Direction.Up => new Vector2(GameConfig.ScreenWidth / 2f, w + r + 1),
            _ => new Vector2(GameConfig.ScreenWidth / 2f, GameConfig.ScreenHeight - w - r - 1),
        };
    }

    // ------------------------------------------------------------ interior walls

    // Tuning knobs for interior wall generation. Named constants keep the
    // layout readable and easy to tweak without hunting for magic numbers.
    private const int MinWalls = 5;
    private const int MaxWalls = 15;
    private const int MinPerimeterWallLength = 150;
    private const int MaxPerimeterWallLength = 540;
    private const int MinBranchLength = 120;
    private const int MaxBranchLength = 260;
    private const int WallInset = 80;             // clearance kept from the room borders
    private const int WallPlacementAttempts = 24; // tries per wall before giving up
    private const int MinWallSpacing = 60;        // gap kept between two unconnected walls

    /// <summary>Which end of a wall hangs free (the other end is attached to
    /// the perimeter or another wall), so clearance can shrink that end.</summary>
    private enum FreeEdge
    {
        Top,
        Bottom,
        Left,
        Right,
    }

    private readonly record struct WallCandidate(Rectangle Rect, FreeEdge Free);

    /// <summary>
    /// Generates a few interior walls to break up open space (so the player
    /// can't just circle-kite around a clear room). Every wall hangs off the
    /// perimeter or off another interior wall, so nothing floats free in the
    /// middle of the room. Walls stay clear of the exits and (for the first
    /// room) the center spawn point.
    /// </summary>
    private static List<Rectangle> GenerateInteriorWalls(Random rng, IReadOnlyCollection<Direction> exits, bool protectSpawn)
    {
        var walls = new List<Rectangle>();
        int target = rng.Next(MinWalls, MaxWalls + 1);

        for (int attempts = 0; attempts < target * WallPlacementAttempts && walls.Count < target; attempts++)
        {
            var candidate = TryGenerateWall(rng, exits, walls, protectSpawn);
            if (candidate.HasValue) walls.Add(candidate.Value);
        }

        return walls;
    }

    private static Rectangle? TryGenerateWall(Random rng, IReadOnlyCollection<Direction> exits, IReadOnlyList<Rectangle> walls, bool protectSpawn)
    {
        bool branch = walls.Count > 0 && rng.Next(2) == 0;
        Rectangle? baseWall = branch ? walls[rng.Next(walls.Count)] : null;
        var candidate = baseWall.HasValue ? GenerateBranch(rng, baseWall.Value) : GeneratePerimeterWall(rng);

        if (branch && !InsideInterior(candidate.Rect)) return null;
        if (OverlapsExit(candidate.Rect, exits)) return null;
        if (protectSpawn && OverlapsSpawnZone(candidate.Rect)) return null;

        int minLength = branch ? MinBranchLength : MinPerimeterWallLength;
        var cleared = ApplyWallClearance(candidate.Rect, candidate.Free, walls, baseWall, minLength);
        return cleared ?? null;
    }

    /// <summary>A wall hanging inward off one of the four perimeter walls.</summary>
    private static WallCandidate GeneratePerimeterWall(Random rng)
    {
        int w = GameConfig.WallThickness;
        int W = GameConfig.ScreenWidth;
        int H = GameConfig.ScreenHeight;
        int len = rng.Next(MinPerimeterWallLength, MaxPerimeterWallLength + 1);

        return (Direction)rng.Next(GameConfig.AllDirections.Length) switch
        {
            Direction.Up => new WallCandidate(new Rectangle(rng.Next(w + WallInset, W - w - WallInset - w), 0, w, len), FreeEdge.Bottom),
            Direction.Down => new WallCandidate(new Rectangle(rng.Next(w + WallInset, W - w - WallInset - w), H - len, w, len), FreeEdge.Top),
            Direction.Left => new WallCandidate(new Rectangle(0, rng.Next(w + WallInset, H - w - WallInset - w), len, w), FreeEdge.Right),
            _ => new WallCandidate(new Rectangle(W - len, rng.Next(w + WallInset, H - w - WallInset - w), len, w), FreeEdge.Left),
        };
    }

    /// <summary>
    /// A wall branching perpendicularly off an existing interior wall, so it
    /// is always connected at one end (never free-standing).
    /// </summary>
    private static WallCandidate GenerateBranch(Random rng, Rectangle baseWall)
    {
        int w = GameConfig.WallThickness;
        int len = rng.Next(MinBranchLength, MaxBranchLength + 1);
        bool baseVertical = baseWall.Width <= baseWall.Height;

        if (baseVertical)
        {
            int lo = baseWall.Y + w;
            int hi = baseWall.Y + baseWall.Height - w;
            int y = (hi > lo ? rng.Next(lo, hi) : lo) - w / 2;
            if (rng.Next(2) == 0)
            {
                return new WallCandidate(new Rectangle(baseWall.X - len, y, len, w), FreeEdge.Left);
            }
            return new WallCandidate(new Rectangle(baseWall.X + w, y, len, w), FreeEdge.Right);
        }
        else
        {
            int lo = baseWall.X + w;
            int hi = baseWall.X + baseWall.Width - w;
            int x = (hi > lo ? rng.Next(lo, hi) : lo) - w / 2;
            if (rng.Next(2) == 0)
            {
                return new WallCandidate(new Rectangle(x, baseWall.Y - len, w, len), FreeEdge.Top);
            }
            return new WallCandidate(new Rectangle(x, baseWall.Y + w, w, len), FreeEdge.Bottom);
        }
    }

    private static bool InsideInterior(Rectangle rect)
    {
        int w = GameConfig.WallThickness;
        return rect.X >= w && rect.Y >= w &&
               rect.Right <= GameConfig.ScreenWidth - w &&
               rect.Bottom <= GameConfig.ScreenHeight - w;
    }

    /// <summary>Walls must never cover or crowd an exit gap.</summary>
    private static bool OverlapsExit(Rectangle candidate, IReadOnlyCollection<Direction> exits)
    {
        var inflated = candidate;
        inflated.Inflate(16, 16);
        foreach (var d in exits)
        {
            if (inflated.Intersects(GetExitRect(d))) return true;
        }
        return false;
    }

    /// <summary>
    /// Keeps a wall a comfortable distance from every other wall, parallel or
    /// perpendicular. Shrinks the free end until it clears each nearby wall by
    /// <see cref="MinWallSpacing"/>, while still touching its own base. Returns
    /// null when shrinking can't fix it (e.g. another wall runs alongside it).
    /// </summary>
    private static Rectangle? ApplyWallClearance(Rectangle candidate, FreeEdge free, IReadOnlyList<Rectangle> walls, Rectangle? baseWall, int minLength)
    {
        int gap = MinWallSpacing;
        var rect = candidate;

        foreach (var wall in walls)
        {
            if (baseWall.HasValue && wall == baseWall.Value) continue;

            var blocked = wall;
            blocked.Inflate(gap, gap);
            if (!rect.Intersects(blocked)) continue;

            // Pull the free end back so it just clears this wall. The attached
            // edge is fixed; only the free end moves inward.
            switch (free)
            {
                case FreeEdge.Bottom:
                    rect.Height = Math.Min(rect.Height, blocked.Y - rect.Y);
                    break;
                case FreeEdge.Top:
                {
                    int attached = rect.Bottom;
                    rect.Y = Math.Max(rect.Y, blocked.Bottom);
                    rect.Height = attached - rect.Y;
                    break;
                }
                case FreeEdge.Right:
                    rect.Width = Math.Min(rect.Width, blocked.X - rect.X);
                    break;
                case FreeEdge.Left:
                {
                    int attached = rect.Right;
                    rect.X = Math.Max(rect.X, blocked.Right);
                    rect.Width = attached - rect.X;
                    break;
                }
            }
        }

        // Shrinking only moves the free end, so walls that run alongside it
        // can't be fixed - reject those outright.
        foreach (var wall in walls)
        {
            if (baseWall.HasValue && wall == baseWall.Value) continue;

            var blocked = wall;
            blocked.Inflate(gap, gap);
            if (rect.Intersects(blocked)) return null;
        }

        int length = free is FreeEdge.Top or FreeEdge.Bottom ? rect.Height : rect.Width;
        return length < minLength ? null : rect;
    }

    /// <summary>Keeps the middle of the starting room open so the player can
    /// spawn somewhere they won't be stuck inside a wall.</summary>
    private static bool OverlapsSpawnZone(Rectangle candidate)
    {
        var inflated = candidate;
        inflated.Inflate(36, 36);
        var spawnZone = new Rectangle(
            GameConfig.ScreenWidth / 2 - 60,
            GameConfig.ScreenHeight / 2 - 60,
            120, 120);
        return inflated.Intersects(spawnZone);
    }

    /// <summary>The perimeter wall segments (with the exit gaps cut out) plus
    /// the interior walls, in one list for collision and drawing.</summary>
    private static List<Rectangle> BuildSolidWalls(IReadOnlyCollection<Direction> exits, IReadOnlyList<Rectangle> interior)
    {
        int w = GameConfig.WallThickness;
        int W = GameConfig.ScreenWidth;
        int H = GameConfig.ScreenHeight;
        var rects = new List<Rectangle>();

        foreach (var d in GameConfig.AllDirections)
        {
            var gap = exits.Contains(d) ? GetExitRect(d) : Rectangle.Empty;

            switch (d)
            {
                case Direction.Up:
                    AddSegment(rects, 0, 0, gap.X, w);
                    AddSegment(rects, gap.Right, 0, W - gap.Right, w);
                    break;
                case Direction.Down:
                    AddSegment(rects, 0, H - w, gap.X, w);
                    AddSegment(rects, gap.Right, H - w, W - gap.Right, w);
                    break;
                case Direction.Left:
                    AddSegment(rects, 0, 0, w, gap.Y);
                    AddSegment(rects, 0, gap.Bottom, w, H - gap.Bottom);
                    break;
                default:
                    AddSegment(rects, W - w, 0, w, gap.Y);
                    AddSegment(rects, W - w, gap.Bottom, w, H - gap.Bottom);
                    break;
            }
        }

        rects.AddRange(interior);
        return rects;
    }

    private static void AddSegment(List<Rectangle> rects, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        rects.Add(new Rectangle(x, y, width, height));
    }
}
