using Microsoft.Xna.Framework;

namespace RedBallGame;

/// <summary>
/// A round, moving thing that can fight. Holds the stat block shared by the
/// Player and Monsters so combat/stat logic lives in one place.
/// </summary>
public abstract class Entity
{
    public string Name { get; protected set; } = "";
    public int Level { get; protected set; }
    public int Health { get; set; }
    public int MaxHealth { get; protected set; }
    public int Attack { get; protected set; }
    public float Radius { get; protected set; }
    public float Speed { get; protected set; }
    public Color BodyColor { get; protected set; }

    public Vector2 Position;
}
