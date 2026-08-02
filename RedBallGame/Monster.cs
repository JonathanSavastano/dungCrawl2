using Microsoft.Xna.Framework;

namespace RedBallGame;

public enum MonsterKind
{
    Goblin,
    Orc,
    Ogre,
}

/// <summary>
/// A monster living in the current room. Same stat lineup as the console
/// game (Goblin/Orc/Ogre), but with simple real-time AI: it chases the player
/// when you get close, attacks on a cooldown, and occasionally telegraphs a
/// wind-up heavy attack that you can interrupt by attacking it.
/// </summary>
public class Monster
{
    public const float AttackInterval = 1.1f;
    public const float WindUpDuration = 0.9f;
    public const float AggroRange = 400f;
    public const int MaxMonsterLevel = 10;

    public string Name { get; }
    public MonsterKind Kind { get; }
    public int Level { get; }
    public int Health { get; set; }
    public int MaxHealth { get; }
    public int Attack { get; }
    public int XpValue { get; }
    public float Radius { get; }
    public float Speed { get; }
    public Color Color { get; }
    public Color WindUpColor { get; }

    public Vector2 Position;
    public bool Chasing;
    public bool IsWindingUp;
    public float WindUpTimer;
    public float AttackCooldown;
    public float HitFlash;

    private Monster(string name, MonsterKind kind, int level, int health, int attack, int xp,
        float radius, float speed, Color color)
    {
        Name = name;
        Kind = kind;
        Level = level;
        Health = health;
        MaxHealth = health;
        Attack = attack;
        XpValue = xp;
        Radius = radius;
        Speed = speed;
        Color = color;
        WindUpColor = Color.Lerp(color, Color.White, 0.75f);
        AttackCooldown = 0.8f;
    }

    /// <summary>
    /// Picks a random monster type with a level near the player's (within +-1),
    /// scaled up so the dungeon stays challenging as you grow stronger.
    /// </summary>
    public static Monster CreateRandom(Random rng, int playerLevel)
    {
        int level = Math.Clamp(playerLevel + rng.Next(-1, 2), 1, MaxMonsterLevel);
        return rng.Next(3) switch
        {
            0 => new Monster("GRUK", MonsterKind.Goblin, level,
                ScaleStat(50, level, 0.25f), ScaleStat(10, level, 0.2f), ScaleStat(40, level, 0.25f),
                12f, 115f, new Color(80, 180, 70)),
            1 => new Monster("ULAG", MonsterKind.Orc, level,
                ScaleStat(75, level, 0.25f), ScaleStat(15, level, 0.2f), ScaleStat(70, level, 0.25f),
                16f, 100f, new Color(230, 140, 50)),
            _ => new Monster("THOKK", MonsterKind.Ogre, level,
                ScaleStat(100, level, 0.25f), ScaleStat(20, level, 0.2f), ScaleStat(100, level, 0.25f),
                24f, 70f, new Color(160, 80, 210)),
        };
    }

    /// <summary>Base stat grown by <paramref name="perLevelGrowth"/> per level above 1.</summary>
    private static int ScaleStat(int baseValue, int level, float perLevelGrowth) =>
        (int)(baseValue * (1f + perLevelGrowth * (level - 1)));
}
