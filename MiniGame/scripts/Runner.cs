using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// A blocky MC-style runner. Subway-surfers movement: always auto-runs along the current
/// heading (±Z), switches lanes laterally, may U-turn to defend the home line.
/// The human player reads input actions; AI runners attack the enemy fortress line.
/// </summary>
public partial class Runner : Node3D
{
    public Team Team;
    public bool IsPlayer;
    public int Heading;          // -1 = north (-Z, blue attack dir), +1 = south (+Z, red attack dir)
    public int TargetLane;       // global lane index 0..5
    public float Hp, MaxHp;
    public bool Dead;
    public float RespawnTimer, StunTimer, RegenTimer, BlastCdTimer, DashCdTimer, DashTimer;
    public bool Dashing;
    public float Speed;         // actual forward speed this frame (0 = blocked)

    private readonly HashSet<Runner> _dashVictims = new();
    private float _aiTimer, _stuckTimer, _runPhase;
    private Node3D _legL, _legR, _armL, _armR, _body;
    private Label3D _tag;
    private bool _useGlbChar;
    public static int BlastCount;

    private StandardMaterial3D _bodyMat, _limbMat;

    public Runner(Team team, bool isPlayer)
    {
        Team = team;
        IsPlayer = isPlayer;
        Heading = Game.Heading(team); // -1 for both teams (same direction)
        MaxHp = isPlayer ? Game.PlayerHp : Game.AiHp;
        Hp = MaxHp;
        int lane = Game.LaneBase(team) + 1;
        TargetLane = lane;
        Position = new Vector3(Game.LaneX[lane], 0f, Game.Instance.PlayerSpawnZ); // both spawn at south end
    }

    public override void _Ready()
    {
        BuildBody();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        BlastCdTimer = Mathf.Max(0f, BlastCdTimer - dt);
        DashCdTimer = Mathf.Max(0f, DashCdTimer - dt);
        StunTimer = Mathf.Max(0f, StunTimer - dt);

        if (Game.Instance != null && Game.Instance.GameOver) return; // freeze on the end screen

        if (Dead)
        {
            RespawnTimer -= dt;
            AnimateDeath(dt);
            if (RespawnTimer <= 0f) Respawn();
            return;
        }

        if (IsPlayer) ReadPlayerInput();
        else Think(dt);

        Move(dt);
        Regenerate(dt);
        Animate(dt);
    }

    // ---- input / AI ----

    private void ReadPlayerInput()
    {
        if (Input.IsActionJustPressed("move_left")) TargetLane = Mathf.Max(0, TargetLane - 1);
        if (Input.IsActionJustPressed("move_right")) TargetLane = Mathf.Min(5, TargetLane + 1);
        if (Input.IsActionJustPressed("turn_back")) Heading *= -1;
        if (Input.IsActionJustPressed("blast")) TryBlast();
        if (Input.IsActionJustPressed("dash")) TryDash();
    }

    /// <summary>AI: run at the enemy's lowest-index intact fortress, sweep through it, blast enemy blocks in reach.</summary>
    private void Think(float dt)
    {
        _aiTimer -= dt;
        if (_aiTimer > 0f) return;
        _aiTimer = 0.25f;
        var game = Game.Instance;
        if (game.GameOver) return;

        if (TryDefend(game)) return; // home defense overrides the attack push

        // retarget: enemy's lowest-index intact fortress
        Fortress target = null;
        foreach (var f in game.Fortresses)
            if (f.Team != Team && !f.Destroyed && (target == null || f.Index < target.Index))
                target = f;

        int ownBase = Game.LaneBase(Team);
        int enemyBase = Game.LaneBase(Game.EnemyOf(Team));

        if (target != null)
        {
            // cross to enemy lanes only when within reach of the target fortress
            if (Mathf.Abs(Position.Z - target.CenterZ) < 14f)
            {
                if (TargetLane < enemyBase || TargetLane >= enemyBase + 3)
                    TargetLane = enemyBase + 1;
                SteerToBlocks(target);
            }
            else
            {
                // transit on own side to avoid own walls
                if (TargetLane < ownBase || TargetLane >= ownBase + 3)
                    TargetLane = ownBase + 1;
            }

            // turn back after fully sweeping past the fortress
            bool overshot = Position.Z < target.ZMin - 4f;
            if (overshot) Heading = 1; // head south to finish sweeping
            else Heading = -1; // keep pushing north
        }

        // engagement: ram a nearby enemy runner
        Runner foe = null;
        float foeD = float.MaxValue;
        foreach (var r in game.Runners)
        {
            if (r.Team == Team || r.Dead) continue;
            float d = r.Position.DistanceTo(Position);
            if (d < foeD) { foeD = d; foe = r; }
        }
        if (foe != null && foeD < 9f && game.Rng.NextDouble() < 0.5)
        {
            if (Mathf.Abs(foe.Position.Z - Position.Z) < 7f && Mathf.Abs(foe.Position.X - Position.X) < 2.5f)
            {
                if (Mathf.Sign(foe.Position.Z - Position.Z) == Heading) TryDash();
                return; // keep charging
            }
        }

        // attack: blast any enemy block within reach, even while running free
        if (BlastCdTimer <= 0f && EnemyBlockNear(9f))
        {
            TryBlast();
            return;
        }

        if (Speed < 0.1f) // blocked by a wall
        {
            _stuckTimer += 0.25f;
            if (_stuckTimer > 0.3f) { TryBlast(); return; }
            if (_stuckTimer > 0.6f)
            {
                _stuckTimer = 0f;
                TargetLane = PickFreerLane();
            }
        }
        else
        {
            _stuckTimer = 0f;
            if (target != null) SteerToBlocks(target);
        }
    }

    /// <summary>Home defense: while an enemy raider is hammering an own fortress, the nearest own
    /// AI turns back, matches the raider's lane and intercepts (design pillar: 同时防守自家).</summary>
    private bool TryDefend(Game game)
    {
        Fortress hot = null;
        Runner raider = null;
        float bestThreat = float.MaxValue;
        foreach (var f in game.Fortresses)
        {
            if (f.Team != Team || f.Destroyed) continue;
            if (f.CurrentHp < f.TotalHp * 0.3f) continue; // a collapsing city isn't worth dying for
            foreach (var r in game.Runners)
            {
                if (r.Team == Team || r.Dead) continue;
                if (r.Position.Z < f.ZMin - 8f || r.Position.Z > f.ZMax + 8f) continue;
                float threat = Mathf.Abs(r.Position.Z - f.CenterZ);
                if (threat < bestThreat) { bestThreat = threat; hot = f; raider = r; }
            }
        }
        if (raider == null || hot == null) return false;

        // only the nearest own AI answers the call; the rest keep pushing the attack
        float myD = Mathf.Abs(Position.Z - hot.CenterZ);
        foreach (var r in game.Runners)
        {
            if (r.Team != Team || r.IsPlayer || r.Dead || r == this) continue;
            if (Mathf.Abs(r.Position.Z - hot.CenterZ) < myD) return false;
        }

        // converge on the raider's column, preferring the own-side lane with the clearest run
        int ownBase = Game.LaneBase(Team);
        int bestLane = NearestLane(raider.Position.X);
        int fewest = int.MaxValue;
        for (int l = 0; l < 3; l++)
        {
            int lane = ownBase + l;
            if (Mathf.Abs(Game.LaneX[lane] - raider.Position.X) > 8f) continue;
            int blocks = 0;
            for (float d = 0f; d < 24f; d += 3f)
                if (game.BlockAhead(lane, Position.Z + Heading * d, Heading) != null) blocks++;
            if (blocks < fewest) { fewest = blocks; bestLane = lane; }
        }
        TargetLane = bestLane;

        float dz = raider.Position.Z - Position.Z;
        if (Mathf.Abs(dz) > 3f) Heading = (int)Mathf.Sign(dz);
        if (raider.Position.DistanceTo(Position) < 9f)
        {
            TryBlast();
            if (Mathf.Abs(raider.Position.X - Position.X) < 2.5f && Mathf.Abs(dz) < 8f && Mathf.Sign(dz) == Heading)
                TryDash();
        }
        if (Speed < 0.1f) TryBlast(); // blocked by an own wall — the blast still catches the raider if close
        return true;
    }

    /// <summary>Steer laterally toward the enemy lane whose surviving blocks are nearest (brings pillars/walls into blast reach).</summary>
    private void SteerToBlocks(Fortress target)
    {
        int baseLane = Game.LaneBase(Game.EnemyOf(Team));
        float cur = LaneBlockScore(target, TargetLane);
        int bestLane = TargetLane;
        float best = cur;
        for (int l = 0; l < 3; l++)
        {
            int lane = baseLane + l;
            if (lane == TargetLane) continue;
            float s = LaneBlockScore(target, lane);
            if (s < best - 2f) { best = s; bestLane = lane; } // hysteresis
        }
        if (bestLane != TargetLane)
            TargetLane = bestLane;
    }

    private float LaneBlockScore(Fortress f, int lane)
    {
        float lx = Game.LaneX[lane];
        float best = float.MaxValue;
        foreach (var b in f.Blocks)
        {
            float d = Mathf.Abs(b.GlobalPosition.X - lx) + 0.7f * Mathf.Abs(b.GlobalPosition.Z - Position.Z);
            if (d < best) best = d;
        }
        return best;
    }

    private bool EnemyBlockNear(float radius)
    {
        foreach (var f in Game.Instance.Fortresses)
        {
            if (f.Team == Team || f.Destroyed) continue;
            if (Mathf.Abs(f.CenterZ - Position.Z) > radius + (f.ZMax - f.ZMin) * 0.5f) continue;
            foreach (var b in f.Blocks)
                if (b.GlobalPosition.DistanceTo(Position) < radius)
                    return true;
        }
        return false;
    }

    private int PickFreerLane()
    {
        var game = Game.Instance;
        int baseLane = Game.LaneBase(Game.EnemyOf(Team));
        int best = TargetLane, bestScore = int.MaxValue;
        for (int l = 0; l < 3; l++)
        {
            int lane = baseLane + l;
            int blocks = 0;
            float z = Position.Z + Heading * 3f;
            for (float d = 0f; d < 15f; d += 3f)
                if (game.BlockAhead(lane, z + Heading * d, Heading) != null) blocks++;
            if (blocks < bestScore) { bestScore = blocks; best = lane; }
        }
        return best;
    }

    // ---- movement ----

    private void Move(float dt)
    {
        // lateral lane switching
        float dx = Game.LaneX[TargetLane] - Position.X;
        float latSpeed = Game.LaneSpeed * (Dashing ? 1.6f : 1f);
        if (Mathf.Abs(dx) > 0.01f)
            Position += new Vector3(Mathf.Sign(dx) * Mathf.Min(Mathf.Abs(dx), latSpeed * dt), 0f, 0f);

        // forward auto-run, blocked by walls
        float spd = Game.RunSpeed * (Dashing ? Game.DashMult : 1f);
        int laneNow = NearestLane(Position.X);
        var block = Game.Instance.BlockAhead(laneNow, Position.Z, Heading);
        if (block != null)
        {
            if (Dashing) Game.Instance.DamageBlock(block, Game.DashBlockDmg * dt * 3f);
            spd = 0f;
        }
        if (StunTimer > 0f) spd = 0f;
        Speed = spd;

        if (Dashing)
        {
            DashTimer -= dt;
            RamEnemies();
            if (DashTimer <= 0f) Dashing = false;
        }

        Position += new Vector3(0f, 0f, Heading * spd * dt);
        float lim = Game.Instance.SpawnZ + 10f;
        Position = new Vector3(Position.X, Position.Y, Mathf.Clamp(Position.Z, -lim, lim));
    }

    private int NearestLane(float x)
    {
        int best = 0;
        float bd = float.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            float d = Mathf.Abs(Game.LaneX[i] - x);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    // ---- skills ----

    public void TryBlast()
    {
        if (BlastCdTimer > 0f || Dead) return;
        BlastCdTimer = Game.BlastCd;
        BlastCount++;
        var center = Position + new Vector3(0f, 1.6f, Heading * 3.5f);
        var laserEnd = Position + new Vector3(0f, 1.4f, Heading * 12f);
        Game.Instance.AddFx(new LaserFx(Position + new Vector3(0f, 1.4f, 0f), laserEnd, Game.ColorOf(Team)));
        Game.Instance.AddFx(new BlastFx(center, Game.BlastRadius));
        Game.Instance.Shake(0.35f);

        foreach (var r in Game.Instance.Runners)
        {
            if (r.Team == Team || r.Dead) continue;
            if (r.Position.DistanceTo(center) < Game.BlastRadius + 1f)
                r.TakeDamage(Game.BlastRunnerDmg, Heading);
        }
        foreach (var f in Game.Instance.Fortresses)
        {
            if (f.Team == Team || f.Destroyed) continue;
            var hit = new List<Block>();
            foreach (var b in f.Blocks)
                if (b.GlobalPosition.DistanceTo(center) < Game.BlastRadius + 1f) hit.Add(b);
            foreach (var b in hit) Game.Instance.DamageBlock(b, Game.BlastBlockDmg);
        }
    }

    public void TryDash()
    {
        if (DashCdTimer > 0f || Dead || Dashing) return;
        DashCdTimer = Game.DashCd;
        Dashing = true;
        DashTimer = Game.DashTime;
        _dashVictims.Clear();
        Game.Instance.AddFx(new DashTrailFx(Position + new Vector3(0f, 0f, Heading * 1.5f), Game.ColorOf(Team)));
    }

    private void RamEnemies()
    {
        foreach (var r in Game.Instance.Runners)
        {
            if (r.Team == Team || r.Dead || _dashVictims.Contains(r)) continue;
            if (Mathf.Abs(r.Position.Z - Position.Z) < 1.9f && Mathf.Abs(r.Position.X - Position.X) < 1.7f)
            {
                _dashVictims.Add(r);
                r.TakeDamage(Game.DashRunnerDmg, Heading);
                Game.Instance.AddFx(new HitSparkFx(r.Position + new Vector3(0f, 1.2f, 0f), Game.ColorOf(r.Team)));
            }
        }
    }

    // ---- health ----

    public void TakeDamage(float dmg, int knockDir)
    {
        if (Dead) return;
        Hp -= dmg;
        StunTimer = Mathf.Max(StunTimer, 0.4f);
        RegenTimer = 0f;
        Position += new Vector3(0f, 0f, knockDir * 0.8f);
        _body.Scale = new Vector3(1.25f, 1.25f, 1.25f); // hit pop
        Game.Instance.AddFx(new HitSparkFx(Position + new Vector3(0f, 1.2f, 0f), Game.ColorOf(Team)));
        if (Hp <= 0f) Die();
    }

    private void Die()
    {
        Dead = true;
        Hp = 0f;
        RespawnTimer = Game.RespawnDelay;
        Dashing = false;
        Speed = 0f;
        Game.Instance.RunnerDied(this);
    }

    private void Respawn()
    {
        Dead = false;
        Hp = MaxHp;
        var p = Game.Instance.FindRespawn(this);
        Position = p;
        Heading = Game.Heading(Team);
        TargetLane = NearestLane(p.X);
        StunTimer = 1.2f; // brief invulnerability-ish grace
        Rotation = new Vector3(0f, 0f, 0f);
    }

    private void Regenerate(float dt)
    {
        RegenTimer += dt;
        if (RegenTimer > Game.RegenDelay && Hp < MaxHp)
            Hp = Mathf.Min(MaxHp, Hp + Game.RegenRate * dt);
    }

    // ---- visuals ----

    private void BuildBody()
    {
        var c = Game.ColorOf(Team);

        _body = new Node3D();
        AddChild(_body);

        // try GLB character model (static mesh, no skeleton — procedural bob/tilt animates it)
        var charGlb = Glb.Create("res://assets/glb/character.glb", 2.4f);
        if (charGlb != null)
        {
            _useGlbChar = true;
            _body.AddChild(charGlb);
            // team-color ground ring under the character (keeps GLB textures intact)
            var ring = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.0f, Height = 0.1f, RadialSegments = 16 },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(c.R, c.G, c.B, 0.5f),
                    EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.2f,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
                Position = new Vector3(0f, 0.05f, 0f),
            };
            _body.AddChild(ring);
        }
        else
        {
            _useGlbChar = false;
            _bodyMat = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.85f };
            _limbMat = new StandardMaterial3D { AlbedoColor = c.Darkened(0.35f), Roughness = 0.9f };
            var headMat = new StandardMaterial3D { AlbedoColor = c.Lightened(0.35f), Roughness = 0.85f };

            Node3D Part(StandardMaterial3D mat, Vector3 size, Vector3 pos)
            {
                return new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = size },
                    MaterialOverride = mat,
                    Position = pos,
                };
            }

            _body.AddChild(Part(_bodyMat, new Vector3(1.05f, 0.9f, 0.6f), new Vector3(0f, 1.42f, 0f)));
            _body.AddChild(Part(headMat, new Vector3(0.72f, 0.72f, 0.72f), new Vector3(0f, 2.25f, 0f)));

            _legL = new Node3D { Position = new Vector3(-0.28f, 0.95f, 0f) };
            _legL.AddChild(Part(_limbMat, new Vector3(0.42f, 0.95f, 0.42f), new Vector3(0f, -0.475f, 0f)));
            _body.AddChild(_legL);
            _legR = new Node3D { Position = new Vector3(0.28f, 0.95f, 0f) };
            _legR.AddChild(Part(_limbMat, new Vector3(0.42f, 0.95f, 0.42f), new Vector3(0f, -0.475f, 0f)));
            _body.AddChild(_legR);

            _armL = new Node3D { Position = new Vector3(-0.72f, 1.8f, 0f) };
            _armL.AddChild(Part(_bodyMat, new Vector3(0.32f, 0.85f, 0.32f), new Vector3(0f, -0.425f, 0f)));
            _body.AddChild(_armL);
            _armR = new Node3D { Position = new Vector3(0.72f, 1.8f, 0f) };
            _armR.AddChild(Part(_bodyMat, new Vector3(0.32f, 0.85f, 0.32f), new Vector3(0f, -0.425f, 0f)));
            _body.AddChild(_armR);
        }

        if (IsPlayer)
        {
            var crown = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.22f, 0.5f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.82f, 0.25f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.8f, 0.2f),
                    EmissionEnergyMultiplier = 0.5f,
                },
                Position = new Vector3(0f, 2.72f, 0f),
            };
            _body.AddChild(crown);
        }

        _tag = new Label3D
        {
            Text = IsPlayer ? "你" : (Team == Team.Blue ? "蓝友" : "红敌"),
            Position = new Vector3(0f, 3.3f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 40,
            OutlineSize = 8,
            Modulate = IsPlayer ? new Color(1f, 0.85f, 0.3f) : Game.ColorOf(Team).Lightened(0.2f),
            Font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } },
        };
        AddChild(_tag);
    }

    private void Animate(float dt)
    {
        // face the running direction (lerp yaw)
        float targetYaw = Heading < 0 ? Mathf.Pi : 0f;
        float yaw = Mathf.LerpAngle(Rotation.Y, targetYaw, Mathf.Min(1f, dt * 8f));
        Rotation = new Vector3(0f, yaw, 0f);

        bool moving = Speed > 0.1f;
        if (moving) _runPhase += dt * Speed * 1.1f;

        if (_useGlbChar)
        {
            // GLB character: bob + lean (no limb bones to swing)
            float bob = moving ? Mathf.Abs(Mathf.Sin(_runPhase)) * 0.12f : 0f;
            float lean = moving ? 0.08f : 0f;
            _body.Position = new Vector3(0f, bob, 0f);
            _body.Scale = _body.Scale.Lerp(Vector3.One, Mathf.Min(1f, dt * 10f));
            if (Dashing)
                _body.Rotation = new Vector3(-0.28f * Heading, 0f, 0f);
            else
                _body.Rotation = new Vector3(Mathf.Lerp(_body.Rotation.X, -lean * Heading, dt * 10f), 0f, 0f);
        }
        else
        {
            // procedural blocky body: swing limbs
            float swing = moving ? Mathf.Sin(_runPhase) * 0.85f : 0f;
            if (_legL != null) _legL.Rotation = new Vector3(swing, 0f, 0f);
            if (_legR != null) _legR.Rotation = new Vector3(-swing, 0f, 0f);
            if (_armL != null) _armL.Rotation = new Vector3(-swing * 0.7f, 0f, 0f);
            if (_armR != null) _armR.Rotation = new Vector3(swing * 0.7f, 0f, 0f);
            _body.Position = new Vector3(0f, moving ? Mathf.Abs(Mathf.Sin(_runPhase)) * 0.08f : 0f, 0f);
            _body.Scale = _body.Scale.Lerp(Vector3.One, Mathf.Min(1f, dt * 10f));
            if (Dashing) _body.Rotation = new Vector3(-0.28f * Heading, 0f, 0f);
            else _body.Rotation = new Vector3(Mathf.Lerp(_body.Rotation.X, 0f, dt * 10f), 0f, 0f);
        }

        if (_tag != null)
            _tag.Visible = true;
    }

    private void AnimateDeath(float dt)
    {
        float t = 1f - Mathf.Max(0f, RespawnTimer / Game.RespawnDelay);
        Rotation = new Vector3(Mathf.DegToRad(90f * t), Rotation.Y, 0f);
        Position = new Vector3(Position.X, 0.35f * t, Position.Z);
    }
}
