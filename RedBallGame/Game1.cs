using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace RedBallGame;

public class Game1 : Game
{
    private enum GameState
    {
        ClassSelect,
        Playing,
        LevelUp,
        GameOver,
    }

    private static readonly Color FloorColor = new(35, 35, 45);
    private static readonly Color WallColor = new(70, 75, 105);
    private static readonly Color ExitColor = new(240, 200, 60);
    private static readonly Color ExitGlowColor = new(255, 235, 140);
    private static readonly Color HpColor = new(220, 60, 60);
    private static readonly Color StaminaColor = new(90, 200, 90);
    private static readonly Color AccentColor = new(255, 220, 100);
    private static readonly Color TextColor = Color.White;
    private static readonly Color DimColor = new(0, 0, 0, 175);
    private static readonly Color UiBgColor = new(10, 10, 18, 210);
    private static readonly Color ShieldColor = new(120, 220, 255, 90);
    private static readonly Color PotionColor = new(255, 90, 160);

    private readonly GraphicsDeviceManager _graphics;
    private readonly Random _random = new();

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private Texture2D _ballTexture = null!;
    private Texture2D _squareTexture = null!;
    private BitmapFont _font = null!;

    private Player _player = null!;
    private Room _room = null!;
    private readonly List<Monster> _monsters = new();
    private readonly List<Vector2> _drops = new();

    private RoomMemory _currentRoomMem = null!;
    private readonly List<RoomMemory> _rememberedRooms = new(2);
    private long _roomVisitCounter;

    private GameState _state = GameState.ClassSelect;
    private int _roomNumber;
    private int _monstersSlain;
    private bool _prevAttackDown;
    private float _fullHealthPotionMsgCooldown;

    private readonly List<string> _messages = new();
    private readonly List<FloatingText> _floats = new();

    private struct FloatingText
    {
        public string Text;
        public Vector2 Position;
        public float Age;
        public Color Color;
    }

    /// <summary>
    /// A room the player can step back into. <see cref="Doors"/> maps each
    /// exit of <see cref="Room"/> to the remembered room behind it, filled in
    /// only when the player actually walks through that exit, so going back
    /// through a used door always returns the exact room that was left.
    /// </summary>
    private sealed class RoomMemory
    {
        public Room Room = null!;
        public readonly List<Monster> Monsters = new();
        public readonly List<Vector2> Drops = new();
        public readonly Dictionary<Direction, RoomMemory> Doors = new();
        public long LastVisited;
    }

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = GameConfig.ScreenWidth,
            PreferredBackBufferHeight = GameConfig.ScreenHeight,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        Window.Title = "Red Ball Dungeon";
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = CreateSolidTexture(1, Color.White);
        _ballTexture = CreateCircleTexture(64);
        _squareTexture = CreateSolidTexture(16, Color.White);
        _font = new BitmapFont(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            keyboard.IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        switch (_state)
        {
            case GameState.ClassSelect: UpdateClassSelect(keyboard); break;
            case GameState.Playing: UpdatePlaying(gameTime, keyboard, dt); break;
            case GameState.LevelUp: UpdateLevelUp(keyboard); break;
            case GameState.GameOver: UpdateGameOver(keyboard); break;
        }

        UpdateFloats(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(FloorColor);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        if (_state == GameState.ClassSelect)
        {
            DrawClassSelect();
            _spriteBatch.End();
            base.Draw(gameTime);
            return;
        }

        DrawWalls();
        DrawExits();
        DrawDrops(gameTime);
        DrawMonsters(gameTime);
        DrawPlayer();
        DrawFloats();
        DrawHud();
        DrawMessages();

        if (_state == GameState.LevelUp) DrawLevelUpOverlay();
        else if (_state == GameState.GameOver) DrawGameOverOverlay();

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    // ---------------------------------------------------------------- states

    private void UpdateClassSelect(KeyboardState kb)
    {
        if (kb.IsKeyDown(Keys.W)) StartNewRun(PlayerClass.Warrior);
        else if (kb.IsKeyDown(Keys.Z)) StartNewRun(PlayerClass.Wizard);
        else if (kb.IsKeyDown(Keys.R)) StartNewRun(PlayerClass.Rogue);
    }

    private void UpdateLevelUp(KeyboardState kb)
    {
        StatChoice? choice = null;
        if (kb.IsKeyDown(Keys.D1)) choice = StatChoice.Health;
        else if (kb.IsKeyDown(Keys.D2)) choice = StatChoice.Stamina;
        else if (kb.IsKeyDown(Keys.D3)) choice = StatChoice.Attack;

        if (choice == null) return;

        _player.LevelUp(choice.Value);
        AddMessage($"Level up! You are now level {_player.Level}.");

        _state = _player.HasPendingLevelUp ? GameState.LevelUp : GameState.Playing;
    }

    private void UpdateGameOver(KeyboardState kb)
    {
        if (kb.IsKeyDown(Keys.R)) _state = GameState.ClassSelect;
    }

    private void StartNewRun(PlayerClass chosen)
    {
        _player = new Player(chosen);
        _roomNumber = 0;
        _monstersSlain = 0;
        _monsters.Clear();
        _drops.Clear();
        _messages.Clear();
        _floats.Clear();
        _prevAttackDown = false;
        _currentRoomMem = null!;
        _rememberedRooms.Clear();
        _roomVisitCounter = 0;
        EnterRoom(null);
        _state = GameState.Playing;
    }

    // ------------------------------------------------------------- gameplay

    private void UpdatePlaying(GameTime gameTime, KeyboardState kb, float dt)
    {
        bool attackDown = kb.IsKeyDown(Keys.Space);
        if (attackDown && !_prevAttackDown) TryPlayerAttack();
        _prevAttackDown = attackDown;

        _player.Guarding = kb.IsKeyDown(Keys.LeftShift) ||
                           kb.IsKeyDown(Keys.RightShift) ||
                           kb.IsKeyDown(Keys.K);
        _player.AttackCooldownTimer = Math.Max(0, _player.AttackCooldownTimer - dt);

        if (_player.Guarding)
        {
            _player.Stamina = Math.Min(_player.MaxStamina, _player.Stamina + _player.GuardStaminaRegen * dt);
        }

        MovePlayer(dt, kb);
        UpdateMonster(dt);
        CheckPotionPickup(dt);
        CheckRoomTransition();

        if (_player.Health <= 0)
        {
            _state = GameState.GameOver;
            AddMessage("You have been defeated!");
        }
    }

    private void MovePlayer(float dt, KeyboardState kb)
    {
        if (_player.Guarding) return;

        var direction = Vector2.Zero;
        if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up)) direction.Y -= 1;
        if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down)) direction.Y += 1;
        if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left)) direction.X -= 1;
        if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) direction.X += 1;
        if (direction != Vector2.Zero) direction.Normalize();

        MoveEntity(_player, direction, _player.Speed * dt);
    }

    /// <summary>
    /// Moves an entity in the given direction, stopping on any solid wall
    /// (perimeter or interior). Axes are moved separately so the ball slides
    /// along walls instead of snagging on corners, and exit gaps stay passable
    /// because the perimeter has openings cut out of it there.
    /// </summary>
    private void MoveEntity(Entity e, Vector2 direction, float distance)
    {
        var pos = e.Position;
        var step = direction * distance;

        float x = pos.X + step.X;
        if (!CircleCollides(new Vector2(x, pos.Y), e.Radius)) pos.X = x;

        float y = pos.Y + step.Y;
        if (!CircleCollides(new Vector2(pos.X, y), e.Radius)) pos.Y = y;

        e.Position = pos;
    }

    /// <summary>True if a circle centred at <paramref name="center"/> overlaps
    /// any solid wall in the current room.</summary>
    private bool CircleCollides(Vector2 center, float radius)
    {
        foreach (var wall in _room.SolidWalls)
        {
            float nearestX = Math.Clamp(center.X, wall.X, wall.Right);
            float nearestY = Math.Clamp(center.Y, wall.Y, wall.Bottom);
            float dx = center.X - nearestX;
            float dy = center.Y - nearestY;
            if (dx * dx + dy * dy < radius * radius) return true;
        }
        return false;
    }

    private void UpdateMonster(float dt)
    {
        for (int i = _monsters.Count - 1; i >= 0; i--)
        {
            var m = _monsters[i];
            if (m.Health <= 0)
            {
                _monsters.RemoveAt(i);
                continue;
            }

            m.HitFlash = Math.Max(0, m.HitFlash - dt);
            float dist = Vector2.Distance(m.Position, _player.Position);
            float attackRange = _player.Radius + m.Radius + 8f;

            if (dist <= Monster.AggroRange) m.Chasing = true;

            // Chain reaction: a chasing monster rallies any monster near it.
            if (m.Chasing)
            {
                foreach (var other in _monsters)
                {
                    if (other == m || other.Health <= 0) continue;
                    if (Vector2.Distance(m.Position, other.Position) <= Monster.AggroRange)
                        other.Chasing = true;
                }
            }

            if (!m.Chasing) continue;

            if (dist > attackRange)
            {
                var dir = Vector2.Normalize(_player.Position - m.Position);
                MoveEntity(m, dir, m.Speed * dt);
                dist = Vector2.Distance(m.Position, _player.Position);
            }

            if (m.IsWindingUp)
            {
                m.WindUpTimer -= dt;
                if (m.WindUpTimer <= 0)
                {
                    m.IsWindingUp = false;
                    int damage = (int)(m.Attack * 1.5);
                    if (_player.Guarding) damage /= 2;
                    ApplyDamage(_player, damage, HpColor);
                    AddMessage(_player.Guarding
                        ? $"{m.Name} smashes into your guard for {damage} damage!"
                        : $"{m.Name} lands a devastating blow for {damage} damage!");
                    m.AttackCooldown = Monster.AttackInterval;
                }
                continue;
            }

            if (dist <= attackRange)
            {
                m.AttackCooldown -= dt;
                if (m.AttackCooldown <= 0)
                {
                    if (_random.NextDouble() < 0.3)
                    {
                        m.IsWindingUp = true;
                        m.WindUpTimer = Monster.WindUpDuration;
                        AddMessage($"{m.Name} the {m.Kind} winds up a heavy attack!");
                    }
                    else
                    {
                        MonsterHitsPlayer(m);
                    }
                }
            }
        }
    }

    private void MonsterHitsPlayer(Monster m)
    {
        float missChance = _player.Guarding ? 0.5f : 0.2f;
        if (_random.NextDouble() < missChance)
        {
            ReportMiss(m);
            m.AttackCooldown = Monster.AttackInterval;
            return;
        }

        int damage = RollDamage(m.Attack);
        ApplyDamage(_player, damage, HpColor);
        AddMessage($"{m.Name} attacks for {damage} damage!");
        m.AttackCooldown = Monster.AttackInterval;
    }

    private void TryPlayerAttack()
    {
        if (_player.Guarding) return;
        if (_player.AttackCooldownTimer > 0) return;

        Monster? m = null;
        float best = float.MaxValue;
        foreach (var monster in _monsters)
        {
            if (monster.Health <= 0) continue;
            float d = Vector2.Distance(_player.Position, monster.Position);
            if (d < best)
            {
                best = d;
                m = monster;
            }
        }

        if (m == null || best > _player.AttackRange + m.Radius)
        {
            AddMessage("No enemy in range!");
            return;
        }

        if (_player.Stamina < Player.AttackCost)
        {
            AddMessage("Too exhausted to attack!");
            return;
        }

        _player.AttackCooldownTimer = _player.AttackCooldown;
        _player.Stamina -= Player.AttackCost;

        if (_random.NextDouble() < 0.2)
        {
            ReportMiss(_player);
            return;
        }

        int damage = RollDamage(_player.Attack);
        ApplyDamage(m, damage, TextColor);
        m.HitFlash = 0.12f;

        if (m.IsWindingUp)
        {
            AddMessage($"You interrupt {m.Name}'s heavy attack!");
            m.IsWindingUp = false;
        }
        else
        {
            AddMessage($"{_player.Name} strikes for {damage} damage!");
        }

        if (m.Health <= 0)
        {
            _monstersSlain++;
            AddMessage($"You have defeated {m.Name} the {m.Kind}!");
            AddMessage($"You gained {m.XpValue} XP!");
            _monsters.Remove(m);

            if (_player.GainXP(m.XpValue))
            {
                _state = GameState.LevelUp;
                AddMessage("Level up! Choose (1)+10 health, (2)+2 stamina, (3)+5 attack.");
            }

            if (_random.NextDouble() < 0.35)
            {
                _drops.Add(m.Position);
                AddMessage("The monster drops a potion!");
            }
        }
    }

    private void CheckPotionPickup(float dt)
    {
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var drop = _drops[i];
            if (Vector2.Distance(_player.Position, drop) > _player.Radius + 14) continue;

            if (_player.Health >= _player.MaxHealth)
            {
                _fullHealthPotionMsgCooldown -= dt;
                if (_fullHealthPotionMsgCooldown <= 0)
                {
                    AddMessage("Your health is already full - you leave the potion.");
                    _fullHealthPotionMsgCooldown = 1.2f;
                }
                continue;
            }

            int before = _player.Health;
            _player.Health = Math.Min(_player.MaxHealth, _player.Health + _random.Next(10, 30));
            AddMessage($"You drink a potion and recover {_player.Health - before} health!");
            SpawnFloat($"+{_player.Health - before}", drop, StaminaColor);
            _drops.RemoveAt(i);
        }
    }

    private void CheckRoomTransition()
    {
        float r = _player.Radius;
        var p = _player.Position;

        if (p.X + r < 0) EnterRoom(Direction.Left);
        else if (p.X - r > GameConfig.ScreenWidth) EnterRoom(Direction.Right);
        else if (p.Y + r < 0) EnterRoom(Direction.Up);
        else if (p.Y - r > GameConfig.ScreenHeight) EnterRoom(Direction.Down);
    }

    private void EnterRoom(Direction? enteredFrom)
    {
        _roomNumber++;

        bool reused = false;
        RoomMemory next;
        if (enteredFrom.HasValue && _currentRoomMem != null)
        {
            Direction via = enteredFrom.Value;
            SaveRoomContents(_currentRoomMem);

            if (_currentRoomMem.Doors.ContainsKey(via))
            {
                next = _currentRoomMem.Doors[via];
                reused = true;
                _rememberedRooms.Remove(next);
            }
            else
            {
                next = new RoomMemory { Room = Room.GenerateRandom(_random, via) };
            }

            _currentRoomMem.Doors[via] = next;
            next.Doors[GameConfig.Opposite(via)] = _currentRoomMem;
            AddRemembered(_currentRoomMem);
        }
        else
        {
            next = new RoomMemory { Room = Room.GenerateRandom(_random, enteredFrom) };
        }

        _room = next.Room;
        _currentRoomMem = next;
        next.LastVisited = ++_roomVisitCounter;
        _player.Stamina = _player.MaxStamina;
        _floats.Clear();

        if (enteredFrom.HasValue)
        {
            _player.Position = Room.GetEntryPoint(GameConfig.Opposite(enteredFrom.Value), _player.Radius);
        }
        else
        {
            _player.Position = new Vector2(GameConfig.ScreenWidth / 2f, GameConfig.ScreenHeight / 2f);
        }

        RestoreRoomContents(next);

        AddMessage($"--- Room {_roomNumber} ---");
        if (reused)
        {
            AddMessage("You step back into the room you came from.");
        }
        else
        {
            _monsters.Clear();
            _drops.Clear();

            if (_random.NextDouble() < 0.45)
            {
                int count = _random.Next(1, 4); // 1-3 enemies
                for (int i = 0; i < count; i++)
                {
                    var m = Monster.CreateRandom(_random, _player.Level);
                    m.Position = FindSpawnPoint(90f, _monsters.Select(x => x.Position));
                    _monsters.Add(m);
                    AddMessage($"A {m.Name} the {m.Kind} (LV {m.Level}) blocks your way!");
                }
            }
            else if (_random.NextDouble() < 0.35)
            {
                _drops.Add(FindSpawnPoint(50f, Enumerable.Empty<Vector2>()));
                AddMessage("You spot a glowing potion!");
            }
            else
            {
                AddMessage("The room is empty, save for the dust in the air.");
            }
        }

        UpdateTitle();
    }

    private void SaveRoomContents(RoomMemory mem)
    {
        mem.Monsters.Clear();
        mem.Monsters.AddRange(_monsters);
        mem.Drops.Clear();
        mem.Drops.AddRange(_drops);
    }

    private void RestoreRoomContents(RoomMemory mem)
    {
        _monsters.Clear();
        _monsters.AddRange(mem.Monsters);
        _drops.Clear();
        _drops.AddRange(mem.Drops);
    }

    private void AddRemembered(RoomMemory mem)
    {
        _rememberedRooms.Remove(mem);
        _rememberedRooms.Add(mem);
        while (_rememberedRooms.Count > 2)
        {
            EvictRoom(_rememberedRooms.OrderBy(r => r.LastVisited).First());
        }
    }

    private void EvictRoom(RoomMemory victim)
    {
        foreach (var mem in _rememberedRooms.Concat(new[] { _currentRoomMem }))
        {
            foreach (var door in mem.Doors.Keys.ToList())
            {
                if (ReferenceEquals(mem.Doors[door], victim)) mem.Doors.Remove(door);
            }
        }
        victim.Doors.Clear();
        _rememberedRooms.Remove(victim);
    }

    private Vector2 FindSpawnPoint(float minDistFromPlayer, IEnumerable<Vector2> occupied)
    {
        int w = GameConfig.WallThickness;
        for (int i = 0; i < 40; i++)
        {
            var pos = new Vector2(
                _random.Next(w + 40, GameConfig.ScreenWidth - w - 40),
                _random.Next(w + 40, GameConfig.ScreenHeight - w - 40));
            if (Vector2.Distance(pos, _player.Position) >= minDistFromPlayer &&
                occupied.All(o => Vector2.Distance(pos, o) >= 60f) &&
                !CircleCollides(pos, 36f))
            {
                return pos;
            }
        }

        // Last resort: scan a few fixed spots for one that isn't inside a wall.
        var fallbacks = new[]
        {
            new Vector2(w + 80, GameConfig.ScreenHeight - w - 80),
            new Vector2(GameConfig.ScreenWidth / 2f, GameConfig.ScreenHeight - w - 80),
            new Vector2(GameConfig.ScreenWidth - w - 80, GameConfig.ScreenHeight - w - 80),
            new Vector2(GameConfig.ScreenWidth / 2f, GameConfig.ScreenHeight / 2f),
        };
        foreach (var pos in fallbacks)
        {
            if (!CircleCollides(pos, 36f)) return pos;
        }
        return new Vector2(GameConfig.ScreenWidth / 2f, GameConfig.ScreenHeight - w - 60);
    }

    // ------------------------------------------------------------------- draw

    private void DrawWalls()
    {
        foreach (var wall in _room.SolidWalls)
        {
            DrawRect(wall, WallColor);
        }
    }

    private void DrawExits()
    {
        foreach (var d in _room.Exits)
        {
            var gap = Room.GetExitRect(d);
            DrawRect(gap, ExitColor);
            var glow = gap;
            glow.Inflate(-8, -8);
            DrawRect(glow, ExitGlowColor);
        }
    }

    private void DrawMonsters(GameTime gameTime)
    {
        foreach (var m in _monsters)
        {
            if (m.Health <= 0) continue;
            DrawMonster(gameTime, m);
        }
    }

    private void DrawMonster(GameTime gameTime, Monster m)
    {
        var bodyColor = m.BodyColor;
        if (m.HitFlash > 0)
        {
            bodyColor = Color.Lerp(m.BodyColor, Color.White, m.HitFlash / 0.12f);
        }
        else if (m.IsWindingUp)
        {
            bodyColor = Math.Sin(gameTime.TotalGameTime.TotalSeconds * 30) > 0
                ? m.WindUpColor
                : Color.White;
        }

        DrawEntityBody(m, bodyColor);

        float barWidth = m.Radius * 2 + 12;
        DrawBar(
            new Rectangle((int)(m.Position.X - barWidth / 2), (int)(m.Position.Y - m.Radius - 16), (int)barWidth, 5),
            m.Health, m.MaxHealth, HpColor, UiBgColor);

        string name = $"{m.Name} THE {m.Kind} LV {m.Level}";
        _font.Draw(_spriteBatch, name, m.Position.X - _font.Measure(name, 1f) / 2, m.Position.Y - m.Radius - 26, 1f, AccentColor);

        if (m.IsWindingUp)
        {
            _font.Draw(_spriteBatch, "!", m.Position.X - 2, m.Position.Y - m.Radius - 40, 1.6f, Color.White);
        }
    }

    private void DrawPlayer()
    {
        DrawEntityBody(_player);

        if (_player.Guarding)
        {
            DrawCircle(_player.Position, _player.Radius + 8, ShieldColor);
        }
    }

    private void DrawDrops(GameTime gameTime)
    {
        float pulse = 1f + 0.15f * (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6);
        foreach (var drop in _drops)
        {
            float size = 22f * pulse;
            var dest = new Rectangle(
                (int)(drop.X - size / 2),
                (int)(drop.Y - size / 2),
                (int)size,
                (int)size);
            _spriteBatch.Draw(_squareTexture, dest, null, PotionColor, MathHelper.PiOver4, new Vector2(8, 8), SpriteEffects.None, 0);
        }
    }

    private void DrawFloats()
    {
        foreach (var f in _floats)
        {
            float t = f.Age;
            int alpha = (int)(255 * Math.Clamp(1 - t / 0.9f, 0f, 1f));
            _font.Draw(_spriteBatch, f.Text, f.Position.X - _font.Measure(f.Text, 1.4f) / 2, f.Position.Y - t * 40, 1.4f, f.Color * (alpha / 255f));
        }
    }

    private void DrawHud()
    {
        float scale = 1.4f;
        _font.Draw(_spriteBatch, $"{_player.Name}  LV {_player.Level}", 10, 8, scale, AccentColor);

        DrawBar(new Rectangle(10, 24, 170, 12), _player.Health, _player.MaxHealth, HpColor, UiBgColor);
        _font.Draw(_spriteBatch, $"HP {Math.Max(0, _player.Health)}/{_player.MaxHealth}", 10, 38, scale, TextColor);

        DrawBar(new Rectangle(10, 56, 170, 8), (int)_player.Stamina, _player.MaxStamina, StaminaColor, UiBgColor);
        _font.Draw(_spriteBatch, $"ST {Math.Max(0, (int)_player.Stamina)}/{_player.MaxStamina}", 10, 66, scale, TextColor);

        _font.Draw(_spriteBatch, $"XP {_player.XP}/{_player.XpNeededForNextLevel}", 10, 82, scale, TextColor);
        _font.Draw(_spriteBatch, $"SLAIN {_monstersSlain}", 10, 96, scale, TextColor);

        string room = $"ROOM {_roomNumber}";
        _font.Draw(_spriteBatch, room, GameConfig.ScreenWidth - _font.Measure(room, scale) - 10, 8, scale, AccentColor);
    }

    private void DrawMessages()
    {
        float y = GameConfig.ScreenHeight - 22;
        for (int i = Math.Max(0, _messages.Count - 5); i < _messages.Count; i++)
        {
            _font.Draw(_spriteBatch, _messages[i], 10, y, 1.3f, TextColor);
            y -= 16;
        }
    }

    private void DrawClassSelect()
    {
        DrawRect(new Rectangle(0, 0, GameConfig.ScreenWidth, GameConfig.ScreenHeight), DimColor);
        DrawCentered("RED BALL DUNGEON", 90, 3f, AccentColor);
        DrawCentered("CHOOSE YOUR CLASS:", 170, 1.8f, TextColor);
        DrawCentered("(W) WARRIOR    HP 120  ATK 15", 220, 1.8f, TextColor);
        DrawCentered("(Z) WIZARD     HP 80   ATK 30", 250, 1.8f, TextColor);
        DrawCentered("(R) ROGUE      HP 100  ATK 20", 280, 1.8f, TextColor);
        DrawCentered("WASD/ARROWS: MOVE     SPACE: ATTACK", 360, 1.5f, TextColor);
        DrawCentered("HOLD SHIFT OR K: DEFEND (REGENS STAMINA)", 390, 1.5f, TextColor);
        DrawCentered("RUN AWAY FROM A MONSTER TO FLEE", 420, 1.5f, TextColor);
        DrawCentered("ESC TO QUIT", 470, 1.5f, TextColor);
    }

    private void DrawLevelUpOverlay()
    {
        DrawRect(new Rectangle(0, 0, GameConfig.ScreenWidth, GameConfig.ScreenHeight), DimColor);
        DrawCentered("LEVEL UP!", 220, 3f, AccentColor);
        DrawCentered($"YOU ARE NOW LEVEL {_player.Level + 1}", 270, 1.8f, TextColor);
        DrawCentered("(1) +10 HEALTH    (2) +2 STAMINA    (3) +5 ATTACK", 330, 1.8f, TextColor);
    }

    private void DrawGameOverOverlay()
    {
        DrawRect(new Rectangle(0, 0, GameConfig.ScreenWidth, GameConfig.ScreenHeight), DimColor);
        DrawCentered("GAME OVER", 170, 3f, HpColor);
        DrawCentered($"FINAL LEVEL: {_player.Level}", 250, 1.8f, TextColor);
        DrawCentered($"ROOMS EXPLORED: {_roomNumber}", 285, 1.8f, TextColor);
        DrawCentered($"MONSTERS SLAIN: {_monstersSlain}", 320, 1.8f, TextColor);
        DrawCentered("PRESS R TO RESTART", 400, 1.8f, AccentColor);
    }

    private void DrawCentered(string text, float y, float scale, Color color)
    {
        float x = (GameConfig.ScreenWidth - _font.Measure(text, scale)) / 2f;
        _font.Draw(_spriteBatch, text, x, y, scale, color);
    }

    private void DrawEyes(Vector2 pos, float radius)
    {
        float eyeRadius = Math.Max(2f, radius * 0.22f);
        float dx = radius * 0.35f;
        float dy = radius * 0.25f;
        DrawCircle(pos + new Vector2(-dx, -dy), eyeRadius, Color.White);
        DrawCircle(pos + new Vector2(dx, -dy), eyeRadius, Color.White);
    }

    private void DrawEntityBody(Entity e, Color? color = null)
    {
        DrawCircle(e.Position, e.Radius, color ?? e.BodyColor);
        DrawEyes(e.Position, e.Radius);
    }

    private void DrawBar(Rectangle bounds, int current, int max, Color fill, Color background)
    {
        DrawRect(bounds, background);
        int width = Math.Max(0, (int)(bounds.Width * Math.Clamp((float)current / max, 0f, 1f)));
        if (width > 0)
        {
            DrawRect(new Rectangle(bounds.X, bounds.Y, width, bounds.Height), fill);
        }
    }

    private void DrawCircle(Vector2 center, float radius, Color color)
    {
        var dest = new Rectangle(
            (int)(center.X - radius),
            (int)(center.Y - radius),
            (int)(radius * 2),
            (int)(radius * 2));
        _spriteBatch.Draw(_ballTexture, dest, color);
    }

    private void DrawRect(Rectangle rect, Color color) =>
        _spriteBatch.Draw(_pixel, rect, color);

    private void DrawRect(int x, int y, int width, int height, Color color) =>
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, width, height), color);

    // --------------------------------------------------------------- helpers

    private void AddMessage(string message)
    {
        _messages.Add(message);
        if (_messages.Count > 6) _messages.RemoveAt(0);
    }

    private void SpawnFloat(string text, Vector2 pos, Color color)
    {
        _floats.Add(new FloatingText { Text = text, Position = pos, Age = 0, Color = color });
    }

    private int RollDamage(int attack) => _random.Next(1, attack + 1);

    private void ReportMiss(Entity attacker) =>
        AddMessage($"{attacker.Name} attacks and misses!");

    private void ApplyDamage(Entity target, int damage, Color color)
    {
        target.Health -= damage;
        SpawnFloat($"-{damage}", target.Position + new Vector2(0, -target.Radius - 6), color);
    }

    private void UpdateFloats(float dt)
    {
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            var f = _floats[i];
            f.Age += dt;
            _floats[i] = f;
            if (f.Age > 0.9f) _floats.RemoveAt(i);
        }
    }

    private void UpdateTitle()
    {
        Window.Title = $"Red Ball Dungeon - Room {_roomNumber}";
    }

    private Texture2D CreateCircleTexture(int size)
    {
        var texture = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        float radius = size / 2f - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - size / 2f + 0.5f;
                float dy = y - size / 2f + 0.5f;
                data[y * size + x] = dx * dx + dy * dy <= radius * radius
                    ? Color.White
                    : Color.Transparent;
            }
        }

        texture.SetData(data);
        return texture;
    }

    private Texture2D CreateSolidTexture(int size, Color color)
    {
        var texture = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        Array.Fill(data, color);
        texture.SetData(data);
        return texture;
    }
}
