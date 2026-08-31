using System.Collections.Generic;
using Godot;

namespace FortressRush;

public enum Team { Blue, Red }

/// <summary>Root of the match: builds the world, holds registries, drives camera/HUD/game flow.</summary>
public partial class Game : Node3D
{
    // ---- tuning ----
    public const float BlockSize = 3f;
    public const float RunSpeed = 9f;
    public const float LaneSpeed = 11f;
    public const float BlastRadius = 7f;
    public const float BlastBlockDmg = 55f;
    public const float BlastRunnerDmg = 30f;
    public const float BlastCd = 2.2f;
    public const float DashTime = 0.35f;
    public const float DashMult = 3.2f;
    public const float DashRunnerDmg = 35f;
    public const float DashBlockDmg = 30f;
    public const float DashCd = 3.5f;
    public const float RespawnDelay = 3f;
    public const float RegenDelay = 5f;
    public const float RegenRate = 6f;
    public const int PlayerHp = 120;
    public const int AiHp = 90;
    public const int FortressCount = 10;
    public const float FrontlineSpawnZ = 20f; // 阶段1：出生在己方1号城池后方的前期交火位

    /// <summary>Six lanes flanking a central road. Blue side = 0..2 (x&lt;0), Red side = 3..5 (x&gt;0).</summary>
    public static readonly float[] LaneX = { -16f, -10.5f, -5f, 5f, 10.5f, 16f };

    public static readonly Color Blue = new(0.25f, 0.46f, 0.95f);
    public static readonly Color Red = new(0.93f, 0.30f, 0.27f);

    public static Game Instance { get; private set; }

    public readonly List<Fortress> Fortresses = new();
    public readonly List<Runner> Runners = new();
    public readonly System.Random Rng = new(42);
    public Runner Player { get; private set; }
    public HUD Hud { get; private set; }
    public float SpawnZ { get; private set; }
    public bool GameOver { get; private set; }

    private Camera3D _cam;
    private DirectionalLight3D _sun;
    private float _shake;
    private readonly List<BlastFx> _fx = new();
    private float _announceDelay;

    static Game() { RegisterActions(); }

    public static void RegisterActions()
    {
        void Add(string name, params Key[] keys)
        {
            if (!InputMap.HasAction(name)) InputMap.AddAction(name);
            foreach (var k in keys)
            {
                var ev = new InputEventKey { PhysicalKeycode = k };
                if (!InputMap.ActionHasEvent(name, ev)) InputMap.ActionAddEvent(name, ev);
            }
        }
        Add("move_left", Key.A, Key.Left);
        Add("move_right", Key.D, Key.Right);
        Add("turn_back", Key.S, Key.Down);
        Add("blast", Key.Space, Key.J);
        Add("dash", Key.Shift, Key.K);
        Add("restart", Key.R);
    }

    public override void _Ready()
    {
        Instance = this;
        BuildEnvironment();
        BuildGround();
        BuildFortresses();
        SpawnRunners();
        Hud = new HUD { Name = "HUD" };
        AddChild(Hud);
        PrePositionCamera();
        Hud.Announce("摧毁红方 10 号终极主城即可获胜！", 4f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (GameOver && Input.IsActionJustPressed("restart"))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        // chase camera: behind and above the runner, looking down the run axis (subway-surfers view)
        var focus = Player != null && IsInstanceValid(Player) ? Player.Position : Vector3.Zero;
        int heading = Player != null ? Player.Heading : -1;
        var camTarget = focus + new Vector3(0f, 12.5f, -heading * 19f);
        _cam.Position = _cam.Position.Lerp(camTarget, Mathf.Min(1f, dt * 5f));
        if (_shake > 0f)
        {
            _shake -= dt;
            float a = Mathf.Max(0f, _shake / 0.35f) * 0.6f;
            _cam.Position += new Vector3(Rng.NextSingle() - 0.5f, (Rng.NextSingle() - 0.5f) * 0.5f, Rng.NextSingle() - 0.5f) * a;
        }
        _cam.LookAt(focus + new Vector3(0f, 1.2f, heading * 14f), Vector3.Up);
        _sun.Position = focus + new Vector3(0f, 40f, 0f);

        // core crystals pulse; distant signs hidden to avoid label pile-up
        float pulse = 1f + 0.06f * Mathf.Sin(Time.GetTicksMsec() * 0.004f);
        foreach (var f in Fortresses)
        {
            f.PulseCore(pulse);
            f.UpdateVisibility(focus);
        }
    }

    // ---- world construction ----

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.42f, 0.62f, 0.92f),
            SkyHorizonColor = new Color(0.72f, 0.80f, 0.90f),
            GroundBottomColor = new Color(0.35f, 0.42f, 0.30f),
        };
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1.0f,
            FogEnabled = true,
            FogLightColor = new Color(0.75f, 0.80f, 0.90f),
            FogDensity = 0.005f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.2f,
            DirectionalShadowMaxDistance = 70f,
        };
        _sun.RotationDegrees = new Vector3(-52f, -32f, 0f);
        AddChild(_sun);

        _cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = 55f,
            Near = 0.5f,
            Far = 600f,
        };
        AddChild(_cam);
        _cam.MakeCurrent();
    }

    private void BuildGround()
    {
        var mesh = new BoxMesh { Size = new Vector3(BlockSize, 1f, BlockSize) };
        mesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };

        int nx = 18, nz = 144; // x in [-25.5, 25.5], z in [-214.5, 214.5]
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            UseColors = true,
            InstanceCount = nx * nz,
        };
        var grass1 = new Color(0.37f, 0.56f, 0.31f);
        var grass2 = new Color(0.32f, 0.51f, 0.28f);
        var dirt = new Color(0.48f, 0.38f, 0.26f);
        var road1 = new Color(0.33f, 0.34f, 0.37f);
        var road2 = new Color(0.30f, 0.31f, 0.34f);
        var stone = new Color(0.52f, 0.54f, 0.58f);
        int i = 0;
        for (int ix = 0; ix < nx; ix++)
        {
            float x = -25.5f + ix * BlockSize;
            for (int iz = 0; iz < nz; iz++)
            {
                float z = -214.5f + iz * BlockSize;
                Color c;
                if (Mathf.Abs(z) > 188f) c = stone;
                else if (Mathf.Abs(x) <= 1.8f) c = (iz + ix) % 2 == 0 ? road1 : road2;
                else if (Mathf.Abs(x) > 19.5f) c = dirt;
                else c = (iz + ix) % 2 == 0 ? grass1 : grass2;
                mm.SetInstanceTransform(i, Transform3D.Identity.Translated(new Vector3(x, -0.5f, z)));
                mm.SetInstanceColor(i, c);
                i++;
            }
        }
        AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // road markings: dashed center line + solid edge lines
        var whiteMat = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.88f), Roughness = 0.8f };
        var dashMesh = new BoxMesh { Size = new Vector3(0.35f, 0.06f, 1.6f), Material = whiteMat };
        int dashes = 124;
        var dashMM = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = dashMesh,
            InstanceCount = dashes,
        };
        for (int d = 0; d < dashes; d++)
            dashMM.SetInstanceTransform(d, Transform3D.Identity.Translated(new Vector3(0f, 0.03f, -186f + d * 3f)));
        AddChild(new MultiMeshInstance3D { Multimesh = dashMM, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        foreach (float ex in new[] { -2.9f, 2.9f })
        {
            var edge = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.25f, 0.06f, 376f), Material = whiteMat },
                Position = new Vector3(ex, 0.03f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(edge);
        }

        BuildScenery();
        BuildCamps();
    }

    /// <summary>Trees and rocks along the outer strips, flanking the battlefield (concept art scenery).</summary>
    private void BuildScenery()
    {
        var trunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.30f, 0.19f), Roughness = 1f };
        var leafMat1 = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.42f, 0.20f), Roughness = 1f };
        var leafMat2 = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.48f, 0.24f), Roughness = 1f };
        var rockMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.56f, 0.55f), Roughness = 1f };
        var trunkMesh = new BoxMesh { Size = new Vector3(0.9f, 2.4f, 0.9f), Material = trunkMat };
        var leafMesh = new BoxMesh { Size = new Vector3(2.6f, 2.8f, 2.6f) };

        for (int t = 0; t < 44; t++)
        {
            float side = t % 2 == 0 ? 1f : -1f;
            float x = side * (20.5f + (float)Rng.NextDouble() * 4.5f);
            float z = -180f + (float)Rng.NextDouble() * 360f;
            float s = 0.8f + (float)Rng.NextDouble() * 0.6f;
            var trunk = new MeshInstance3D { Mesh = trunkMesh, Position = new Vector3(x, 1.2f * s, z), Scale = Vector3.One * s };
            var leaf = new MeshInstance3D
            {
                Mesh = leafMesh,
                MaterialOverride = t % 3 == 0 ? leafMat2 : leafMat1,
                Position = new Vector3(x, (2.4f + 1.4f) * s, z),
                Scale = Vector3.One * s,
            };
            AddChild(trunk);
            AddChild(leaf);
        }

        var rockMesh = new BoxMesh { Size = new Vector3(1.3f, 0.8f, 1.1f), Material = rockMat };
        for (int r = 0; r < 18; r++)
        {
            float side = r % 2 == 0 ? 1f : -1f;
            float x = side * (19.8f + (float)Rng.NextDouble() * 5f);
            float z = -180f + (float)Rng.NextDouble() * 360f;
            var rock = new MeshInstance3D
            {
                Mesh = rockMesh,
                Position = new Vector3(x, 0.35f, z),
                RotationDegrees = new Vector3(0f, (float)Rng.NextDouble() * 90f, 0f),
                Scale = Vector3.One * (0.7f + (float)Rng.NextDouble() * 0.8f),
            };
            AddChild(rock);
        }
    }

    /// <summary>Team camps at each end of the map: stone platform, flag pole with team banner, supply crates.</summary>
    private void BuildCamps()
    {
        var stoneMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.57f), Roughness = 1f };
        var poleMat = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.25f, 0.2f), Roughness = 1f };
        var crateMat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.47f, 0.28f), Roughness = 1f };
        var platform = new BoxMesh { Size = new Vector3(10f, 0.4f, 10f), Material = stoneMat };
        var pole = new BoxMesh { Size = new Vector3(0.28f, 7f, 0.28f), Material = poleMat };
        var crate = new BoxMesh { Size = new Vector3(1.2f, 1.2f, 1.2f), Material = crateMat };

        foreach (var (team, cx, cz) in new[] { (Team.Blue, -10.5f, SpawnZ + 6f), (Team.Red, 10.5f, -(SpawnZ + 6f)) })
        {
            var c = ColorOf(team);
            var g = new Node3D { Position = new Vector3(cx, 0f, cz) };
            AddChild(g);

            g.AddChild(new MeshInstance3D { Mesh = platform, Position = new Vector3(0f, 0.2f, 0f) });
            g.AddChild(new MeshInstance3D { Mesh = pole, Position = new Vector3(3.2f, 3.5f, 3.2f) });
            g.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(2.4f, 1.5f, 0.12f),
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = c,
                        EmissionEnabled = true,
                        Emission = c,
                        EmissionEnergyMultiplier = 0.6f,
                        Roughness = 0.8f,
                    },
                },
                Position = new Vector3(3.2f - 1.2f, 6.2f, 3.2f),
            });
            g.AddChild(new MeshInstance3D { Mesh = crate, Position = new Vector3(-3f, 0.9f, 2.6f) });
            g.AddChild(new MeshInstance3D { Mesh = crate, Position = new Vector3(-1.8f, 0.9f, 3.4f), RotationDegrees = new Vector3(0f, 30f, 0f) });
        }
    }

    private void BuildFortresses()
    {
        float z = 4f;
        for (int idx = 1; idx <= FortressCount; idx++)
        {
            float depth = 9f + 1.2f * idx;
            var blueFort = new Fortress(Team.Blue, idx, z, z + depth, Rng);
            AddChild(blueFort);
            Fortresses.Add(blueFort);

            var redFort = new Fortress(Team.Red, idx, -z, -(z + depth), Rng);
            AddChild(redFort);
            Fortresses.Add(redFort);

            z += depth + 4f;
        }
        SpawnZ = z + 3f;
    }

    private void SpawnRunners()
    {
        Runner Spawn(Team t, bool player, int lane, float zOff)
        {
            var r = new Runner(t, isPlayer: player);
            r.TargetLane = lane;
            r.Position = new Vector3(Game.LaneX[lane], 0f,
                (t == Team.Blue ? Game.FrontlineSpawnZ : -Game.FrontlineSpawnZ) + zOff);
            AddChild(r);
            Runners.Add(r);
            return r;
        }
        Player = Spawn(Team.Blue, true, 1, 0f);
        Spawn(Team.Blue, false, 0, 3f);
        Spawn(Team.Blue, false, 2, -3f);
        Spawn(Team.Red, false, 4, 0f);
        Spawn(Team.Red, false, 3, -3f);
        Spawn(Team.Red, false, 5, 3f);
    }

    private void PrePositionCamera()
    {
        if (Player == null) return;
        _cam.Position = Player.Position + new Vector3(0f, 12.5f, -Player.Heading * 19f);
        _cam.LookAt(Player.Position + new Vector3(0f, 1.2f, Player.Heading * 14f), Vector3.Up);
    }

    // ---- queries used by runners ----

    /// <summary>Nearest block in the given lane just ahead of z for a runner moving with the given heading. Null if clear.</summary>
    public Block BlockAhead(int laneIdx, float z, int heading)
    {
        float probe = z + heading * 2.1f;
        Block best = null;
        foreach (var f in Fortresses)
        {
            if (f.Destroyed || !f.OwnsLane(laneIdx)) continue;
            if (probe < f.ZMin - 2f || probe > f.ZMax + 2f) continue;
            foreach (var b in f.Blocks)
            {
                if (b.LaneIdx != laneIdx) continue;
                if (Mathf.Abs(b.GlobalPosition.Z - probe) > 2.0f) continue; // only blocks actually at the probe
                if (best == null || Mathf.Abs(b.GlobalPosition.Z - probe) < Mathf.Abs(best.GlobalPosition.Z - probe))
                    best = b;
            }
        }
        return best;
    }

    public static int LaneBase(Team t) => t == Team.Blue ? 0 : 3;
    public static int Heading(Team t) => t == Team.Blue ? -1 : 1;
    public static Team EnemyOf(Team t) => t == Team.Blue ? Team.Red : Team.Blue;
    public static Color ColorOf(Team t) => t == Team.Blue ? Blue : Red;

    /// <summary>Own nearest intact fortress respawn point (falls back to own map end).</summary>
    public Vector3 FindRespawn(Runner r)
    {
        Fortress best = null;
        float bestD = float.MaxValue;
        foreach (var f in Fortresses)
        {
            if (f.Team != r.Team || f.Destroyed) continue;
            float d = Mathf.Abs(f.CenterZ - r.Position.Z);
            if (d < bestD) { bestD = d; best = f; }
        }
        if (best != null) return best.RespawnPoint();
        return new Vector3(LaneX[LaneBase(r.Team) + 1], 0f, r.Team == Team.Blue ? SpawnZ : -SpawnZ);
    }

    // ---- combat plumbing ----

    public void DamageBlock(Block b, float dmg)
    {
        b.Fortress.DamageBlock(b, dmg);
    }

    public void AddFx(BlastFx fx)
    {
        AddChild(fx);
        _fx.Add(fx);
    }

    public void Shake(float amount = 0.35f) => _shake = amount;

    public void RunnerDied(Runner r)
    {
        Hud?.Announce(r == Player ? "你被击败了…" : $"{NameOf(r)} 被击败", 1.5f);
    }

    public static string NameOf(Runner r) => r.IsPlayer ? "你" : (r.Team == Team.Blue ? "蓝方队友" : "红方敌人");

    public void OnFortressDestroyed(Fortress f)
    {
        Hud?.UpdateFortressSquares();
        Hud?.Announce($"{(f.Team == Team.Blue ? "蓝" : "红")}{f.Index} 号城池被摧毁！", 2.5f);
        if (GameOver) return;
        if (f.Team == Team.Red && f.Index == FortressCount) EndGame(winner: Team.Blue);
        else if (f.Team == Team.Blue && f.Index == FortressCount) EndGame(winner: Team.Red);
    }

    private void EndGame(Team winner)
    {
        GameOver = true;
        var destroyed = new List<Fortress>();
        Team loser = EnemyOf(winner);
        foreach (var f in Fortresses)
            if (f.Team == loser && f.Destroyed)
                destroyed.Add(f);
        destroyed.Sort((a, b) => a.Index.CompareTo(b.Index));
        var names = new List<string>();
        foreach (var f in destroyed)
            names.Add($"{(loser == Team.Blue ? "蓝" : "红")}{f.Index}");
        Hud.ShowEndScreen(winner == Team.Blue, names);
    }
}
