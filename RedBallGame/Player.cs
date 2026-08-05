using Microsoft.Xna.Framework;

namespace RedBallGame;

public enum PlayerClass
{
    Warrior,
    Wizard,
    Rogue,
}

public enum StatChoice
{
    Health,
    Stamina,
    Attack,
}

/// <summary>
/// The player: a red ball you move around the dungeon.
/// Carries the stats/combat rules from the original console game.
/// </summary>
public class Player : Entity
{
    public const int MaxLevel = 10;
    public const int AttackCost = 2;

    public PlayerClass Class { get; }

    public int XP;
    public int MaxStamina = 10;
    public float Stamina;

    public bool Guarding;
    public float AttackCooldownTimer;
    public float AttackCooldown = 0.35f;
    public float AttackRange = 80f;
    public float GuardStaminaRegen = 6f;

    public Player(PlayerClass playerClass)
    {
        Class = playerClass;
        Name = playerClass switch
        {
            PlayerClass.Warrior => "WARRIOR",
            PlayerClass.Wizard => "WIZARD",
            _ => "ROGUE",
        };
        (MaxHealth, Attack) = playerClass switch
        {
            PlayerClass.Warrior => (120, 15),
            PlayerClass.Wizard => (80, 30),
            _ => (100, 20),
        };
        Speed = playerClass switch
        {
            PlayerClass.Warrior => 150f, // slowest, toughest
            PlayerClass.Wizard => 200f,
            _ => 250f,                   // Rogue, fastest
        };
        Level = 1;
        Radius = 16f;
        BodyColor = Color.Red;
        Health = MaxHealth;
        Stamina = MaxStamina;
    }

    public int XpNeededForNextLevel => 100 + 25 * Level * (Level - 1);

    public bool HasPendingLevelUp => Level < MaxLevel && XP >= XpNeededForNextLevel;

    /// <summary>Returns true when this XP gain pushed the player into a pending level-up.</summary>
    public bool GainXP(int amount)
    {
        XP += amount;
        return HasPendingLevelUp;
    }

    public void LevelUp(StatChoice choice)
    {
        XP -= XpNeededForNextLevel;
        Level++;
        switch (choice)
        {
            case StatChoice.Health: MaxHealth += 10; break;
            case StatChoice.Stamina: MaxStamina += 2; break;
            case StatChoice.Attack: Attack += 5; break;
        }
        Health = MaxHealth;
        Stamina = MaxStamina;
    }
}
