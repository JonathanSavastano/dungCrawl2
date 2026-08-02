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

    public Room(IEnumerable<Direction> exits)
    {
        _exits = new HashSet<Direction>(exits);
    }

    public IReadOnlyCollection<Direction> Exits => _exits;

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

        return new Room(exits);
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
}
