using System.Collections.Generic;
using Godot;

namespace FortressRush;

public enum Team { Blue, Red }

public enum GameMode { Solo, SpectatorHost, SpectatorGuest }

/// <summary>Root of the match: builds the world, holds registries, drives camera/HUD/game flow.</summary>
public partial class Game : Node3D
{
    // ---- tuning ----
    public const float BlockSize = 3f;
    public const float RunSpeed = 9f;
    public const float LaneSpeed = 11f;
    public const float BlastRadius = 7f;
    public const float BlastBlockDmg = 70f;
    public const float BlastRunnerDmg = 30f;
    public const float BlastCd = 2.2f;
    public const float DashTime = 0.35f;
    public const float DashMult = 3.2f;
    public const float DashRunnerDmg = 35f;
    public const float DashBlockDmg = 45f;
    public const float DashCd = 3.5f;
    public const float RespawnDelay = 3f;
    public const float RegenDelay = 5f;
    public const float RegenRate = 6f;
    public const int PlayerHp = 120;
    public const int AiHp = 90;
    public const int FortressCount = 10;
    public const float CastleHeight = 10f; // GLB keep at the #10 main city
    public const float TowerHeight = 8f;   // GLB watchtowers — taller than compound walls
    public const float TreeHeight = 5f;    // GLB roadside pines — ~2x character height
    public const float FrontlineSpawnZ = 20f; // 阶段1：出生在己方1号城池后方的前期交火位

    /// <summary>Six lanes flanking a central road. Blue side = 0..2 (x&lt;0), Red side = 3..5 (x&gt;0).</summary>
    public static readonly float[] LaneX = { -12f, -8f, -4f, 4f, 8f, 12f };

    public static readonly Color Blue = new(0.10f, 0.38f, 1.0f);
    public static readonly Color Red = new(0.95f, 0.25f, 0.22f);

    public static Game Instance { get; private set; }

    public readonly List<Fortress> Fortresses = new();
    public readonly List<Runner> Runners = new();
    public readonly System.Random Rng = new(42);        // gameplay stream: fortress layouts, combat rolls, camera shake
    public readonly System.Random SceneryRng = new(42); // scenery-only stream, so baking the world never shifts gameplay RNG
    public Runner Player { get; private set; }
    public HUD Hud { get; private set; }
    public AudioPlayer Audio { get; private set; }
    public NetworkManager Net { get; private set; }
    public GameMode CurrentMode { get; private set; } = GameMode.Solo;
    public float SpawnZ { get; private set; }
    public bool GameOver { get; private set; }
    public float PlayerSpawnZ { get; private set; } // set after BuildFortresses

    private Camera3D _cam;
    private DirectionalLight3D _sun;
    private float _shake;
    private Vector3 _orbitFocus;
    private float _orbitAngle;

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
        if (HasNode("World"))
        {
            LinkBakedWorld();
            GD.Print("[Game] 使用烘焙场景 scenes/World.tscn");
        }
        else
        {
            GD.Print("[Game] 未找到 World 节点，运行时构建世界");
            BuildEnvironment();
            BuildGround();
        }
        BuildFortresses();
        SpawnRunners();
        Hud = new HUD { Name = "HUD" };
        AddChild(Hud);
        Audio = new AudioPlayer();
        AddChild(Audio);

        Net = new NetworkManager { Name = "Network" };
        AddChild(Net);
        if (OS.IsDebugBuild())
            AddChild(new Fps { Name = "Fps" }); // release builds: no node, zero overhead
        PrePositionCamera();

        bool bake = false;
        foreach (var a in OS.GetCmdlineUserArgs()) bake |= a == "--bake";
        foreach (var a in OS.GetCmdlineArgs()) bake |= a == "--bake";
        if (bake)
        {
            BakeStaticWorld();
            GetTree().Quit(0);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (GameOver && Input.IsActionJustPressed("restart"))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        var focus = Player != null && IsInstanceValid(Player) ? Player.Position : Vector3.Zero;
        if (GameOver)
        {
            // endgame: slow orbit around the decisive fortress while the end screen plays
            _orbitAngle += dt * 0.32f;
            var orbitTarget = _orbitFocus + new Vector3(Mathf.Cos(_orbitAngle) * 24f, 13f, Mathf.Sin(_orbitAngle) * 24f);
            _cam.Position = _cam.Position.Lerp(orbitTarget, Mathf.Min(1f, dt * 2.2f));
            _sun.Position = _orbitFocus + new Vector3(0f, 40f, 0f);
            if (_shake > 0f)
            {
                _shake -= dt;
                float a = Mathf.Max(0f, _shake / 0.35f) * 0.6f;
                _cam.Position += new Vector3(Rng.NextSingle() - 0.5f, (Rng.NextSingle() - 0.5f) * 0.5f, Rng.NextSingle() - 0.5f) * a;
            }
            _cam.LookAt(_orbitFocus + new Vector3(0f, 5f, 0f), Vector3.Up);
        }
        else
        {
            // elevated chase cam: ~20° down pitch, horizon visible at top, player in lower third (per concept art)
            int heading = Player != null ? Player.Heading : -1;
            var camTarget = focus + new Vector3(0f, 12f, -heading * 18f);
            _cam.Position = _cam.Position.Lerp(camTarget, Mathf.Min(1f, dt * 5f));
            if (_shake > 0f)
            {
                _shake -= dt;
                float a = Mathf.Max(0f, _shake / 0.35f) * 0.6f;
                _cam.Position += new Vector3(Rng.NextSingle() - 0.5f, (Rng.NextSingle() - 0.5f) * 0.5f, Rng.NextSingle() - 0.5f) * a;
            }
            _cam.LookAt(focus + new Vector3(0f, 1.5f, heading * 12f), Vector3.Up);
            _sun.Position = focus + new Vector3(0f, 40f, 0f);
        }

        // core crystals pulse; distant signs hidden to avoid label pile-up
        float pulse = 1f + 0.06f * Mathf.Sin(Time.GetTicksMsec() * 0.004f);
        foreach (var f in Fortresses)
        {
            f.TickVisuals(dt, pulse);
            f.UpdateVisibility(focus);
        }
    }

    // ---- world construction ----

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.12f, 0.38f, 0.90f),
            SkyHorizonColor = new Color(0.58f, 0.72f, 0.92f),
            GroundBottomColor = new Color(0.35f, 0.42f, 0.30f),
        };
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.25f,
            FogEnabled = true,
            FogLightColor = new Color(0.75f, 0.80f, 0.90f),
            FogDensity = 0.001f,
            FogSkyAffect = 0.1f,
            SsaoEnabled = true,
            SsaoIntensity = 3.0f,
            SdfgiEnabled = true,
            SdfgiUseOcclusion = true,
            SdfgiReadSkyLight = true,
            SdfgiBounceFeedback = 0.3f,
            GlowEnabled = true,
            GlowIntensity = 0.85f,
            GlowBloom = 0.08f,
            TonemapMode = Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });

        // warm key light with soft shadows
        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightEnergy = 1.25f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            DirectionalShadowMaxDistance = 70f,
        };
        _sun.RotationDegrees = new Vector3(-52f, -32f, 0f);
        AddChild(_sun);

        // cool back fill for shape modeling (shadowless)
        var fill = new DirectionalLight3D
        {
            Name = "Fill",
            ShadowEnabled = false,
            LightEnergy = 0.25f,
            LightColor = new Color(0.7f, 0.8f, 1.0f),
        };
        fill.RotationDegrees = new Vector3(-38f, 140f, 0f);
        AddChild(fill);

        _cam = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = 50f,
            Near = 0.5f,
            Far = 600f,
        };
        AddChild(_cam);
        _cam.MakeCurrent();
    }

    /// <summary>Static environment baked into scenes/World.tscn: relink the runtime handles instead of rebuilding.</summary>
    private void LinkBakedWorld()
    {
        _cam = GetNode<Camera3D>("World/Camera");
        _sun = GetNode<DirectionalLight3D>("World/Sun");
        _cam.MakeCurrent();
    }

    /// <summary>
    /// Move the runtime-built static environment under a "World" node and save it to scenes/World.tscn.
    /// One-shot bake: run with `++ --bake`, then instance World.tscn inside Main.tscn; _Ready skips the builders.
    /// Must run windowed (a real renderer) — the headless dummy renderer no-ops MultiMesh instance writes.
    /// Re-bake by deleting scenes/World.tscn and removing the World node from Main.tscn first.
    /// </summary>
    private void BakeStaticWorld()
    {
        if (HasNode("World"))
        {
            GD.Print("[Bake] Main.tscn 已包含 World 实例——请先删除 scenes/World.tscn 并移除 Main.tscn 中的 World 节点再重新烘焙");
            return;
        }
        var world = new Node3D { Name = "World" };
        AddChild(world);
        var moving = new List<Node>();
        foreach (Node child in GetChildren())
            if (child != world && child is not Fortress && child is not Runner && child is not CanvasLayer)
                moving.Add(child); // CanvasLayer covers HUD and the debug Fps overlay — UI stays runtime-only
        foreach (Node child in moving)
        {
            RemoveChild(child);
            world.AddChild(child);
        }
        // Pack() only serializes nodes whose Owner is inside the packed tree
        foreach (Node child in moving)
            ClaimForPack(child, world);
        var ps = new PackedScene();
        Error err = ps.Pack(world);
        if (err == Error.Ok) err = ResourceSaver.Save(ps, "res://scenes/World.tscn");
        GD.Print(err == Error.Ok ? "[Bake] 已生成 scenes/World.tscn" : $"[Bake] 失败：{err}");
    }

    /// <summary>Recursively claim nodes for baking; instanced scene roots (GLB models) are claimed
    /// but not descended into, so each model packs as a compact instance reference.</summary>
    private static void ClaimForPack(Node node, Node owner)
    {
        node.Owner = owner;
        if (node.SceneFilePath.Length != 0) return;
        foreach (Node child in node.GetChildren())
            ClaimForPack(child, owner);
    }

    private void BuildGround()
    {
        var mesh = new BoxMesh { Size = new Vector3(BlockSize, 1f, BlockSize) };
        mesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };

        int nx = 14, nz = 144; // x in [-19.5, 19.5], z in [-214.5, 214.5]
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
                else if (Mathf.Abs(x) > 15.5f) c = dirt;
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
        BuildCoins();
        BuildCamps();
        BuildLandmarkCastle();
    }

    /// <summary>Coins scattered on the road (decorative).</summary>
    private void BuildCoins()
    {
        const string coinPath = "res://assets/glb/coin.glb";
        for (int i = 0; i < 12; i++)
        {
            float z = PlayerSpawnZ - 10f - i * 16f;
            if (z < -SpawnZ + 10f) break;
            var coin = Glb.Create(coinPath, 1.2f);
            if (coin != null)
            {
                coin.Position = new Vector3(0f, 1f, z);
                coin.RotationDegrees = new Vector3(90f, i * 30f, 0f);
                AddChild(coin);
            }
        }
    }

    /// <summary>Distant castle_terminal at the far end of the road (per concept art landmark).</summary>
    private void BuildLandmarkCastle()
    {
        var landmark = Glb.Create("res://assets/glb/castle_terminal.glb", 20f);
        if (landmark != null)
        {
            landmark.Position = new Vector3(0f, 0f, -SpawnZ + 8f);
            AddChild(landmark);
        }
    }

    /// <summary>Trees and rocks along the outer strips, flanking the battlefield (concept art scenery).</summary>
    private void BuildScenery()
    {
        var rng = SceneryRng;
        var trunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.30f, 0.19f), Roughness = 1f };
        var leafMat1 = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.42f, 0.20f), Roughness = 1f };
        var leafMat2 = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.48f, 0.24f), Roughness = 1f };
        var rockMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.56f, 0.55f), Roughness = 1f };
        var trunkMesh = new BoxMesh { Size = new Vector3(0.9f, 2.4f, 0.9f), Material = trunkMat };
        var leafMesh = new BoxMesh { Size = new Vector3(2.6f, 2.8f, 2.6f) };
        const string treePath = "res://assets/glb/tree.glb";

        for (int t = 0; t < 44; t++)
        {
            float side = t % 2 == 0 ? 1f : -1f;
            float x = side * (15.5f + (float)rng.NextDouble() * 3f);
            float z = -180f + (float)rng.NextDouble() * 360f;
            float s = 0.8f + (float)rng.NextDouble() * 0.6f;
            float yaw = (float)rng.NextDouble() * 360f;

            var tree = Glb.Create(treePath, TreeHeight);
            if (tree != null)
            {
                tree.Position = new Vector3(x, 0f, z);
                tree.RotationDegrees = new Vector3(0f, yaw, 0f);
                tree.Scale = Vector3.One * s;
                AddChild(tree);
                continue;
            }

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
            float x = side * (14.8f + (float)rng.NextDouble() * 4f);
            float z = -180f + (float)rng.NextDouble() * 360f;
            var rock = new MeshInstance3D
            {
                Mesh = rockMesh,
                Position = new Vector3(x, 0.35f, z),
                RotationDegrees = new Vector3(0f, (float)rng.NextDouble() * 90f, 0f),
                Scale = Vector3.One * (0.7f + (float)rng.NextDouble() * 0.8f),
            };
            AddChild(rock);
        }

        // broadleaf trees (GLB) and bushes (GLB), with procedural fallback
        const string broadleafPath = "res://assets/glb/tree_broadleaf.glb";
        const string bushPath = "res://assets/glb/bush.glb";
        var bTrunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.38f, 0.27f, 0.15f), Roughness = 1f };
        var bLeafMat1 = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.58f, 0.28f), Roughness = 1f };
        var bLeafMat2 = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.63f, 0.32f), Roughness = 1f };
        var bushMat = new StandardMaterial3D { AlbedoColor = new Color(0.28f, 0.50f, 0.24f), Roughness = 1f };
        var bTrunkMesh = new BoxMesh { Size = new Vector3(0.8f, 2.0f, 0.8f), Material = bTrunkMat };
        var bLeafMesh = new SphereMesh { Radius = 1.6f, Height = 3.2f, RadialSegments = 8, Rings = 4 };
        var bushMeshFallback = new SphereMesh { Radius = 0.7f, Height = 1.4f, RadialSegments = 6, Rings = 3 };

        for (int t = 0; t < 12; t++)
        {
            float side = t % 2 == 0 ? 1f : -1f;
            float x = side * (16f + (float)rng.NextDouble() * 3f);
            float z = -170f + (float)rng.NextDouble() * 340f;
            float s = 0.9f + (float)rng.NextDouble() * 0.5f;
            float yaw = (float)rng.NextDouble() * 360f;

            var blTree = Glb.Create(broadleafPath, TreeHeight);
            if (blTree != null)
            {
                blTree.Position = new Vector3(x, 0f, z);
                blTree.RotationDegrees = new Vector3(0f, yaw, 0f);
                blTree.Scale = Vector3.One * s;
                AddChild(blTree);
            }
            else
            {
                AddChild(new MeshInstance3D { Mesh = bTrunkMesh, Position = new Vector3(x, 1.0f * s, z), Scale = Vector3.One * s });
                AddChild(new MeshInstance3D
                {
                    Mesh = bLeafMesh,
                    MaterialOverride = t % 2 == 0 ? bLeafMat1 : bLeafMat2,
                    Position = new Vector3(x, (2.0f + 1.2f) * s, z),
                    Scale = Vector3.One * s,
                });
            }
        }

        for (int b = 0; b < 20; b++)
        {
            float side = b % 2 == 0 ? 1f : -1f;
            float x = side * (13.5f + (float)rng.NextDouble() * 5f);
            float z = -170f + (float)rng.NextDouble() * 340f;
            float s = 0.6f + (float)rng.NextDouble() * 0.6f;

            var bush = Glb.Create(bushPath, 1.5f);
            if (bush != null)
            {
                bush.Position = new Vector3(x, 0f, z);
                bush.RotationDegrees = new Vector3(0f, (float)rng.NextDouble() * 360f, 0f);
                bush.Scale = Vector3.One * s;
                AddChild(bush);
            }
            else
            {
                AddChild(new MeshInstance3D
                {
                    Mesh = bushMeshFallback,
                    MaterialOverride = bushMat,
                    Position = new Vector3(x, 0.5f * s, z),
                    Scale = Vector3.One * s,
                });
            }
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

        foreach (var (team, cx, cz) in new[] { (Team.Blue, -10.5f, PlayerSpawnZ + 6f), (Team.Red, 10.5f, PlayerSpawnZ + 6f) })
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

            // spawn pen enclosure (GLB or procedural fence fallback)
            string penPath = $"res://assets/glb/spawn_pen_{(team == Team.Blue ? "blue" : "red")}.glb";
            var pen = Glb.Create(penPath, 4f);
            if (pen != null)
            {
                pen.Position = new Vector3(0f, 0f, 0f);
                g.AddChild(pen);
            }
            else
            {
                var fenceMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.40f, 0.22f), Roughness = 1f };
                var postMat = new StandardMaterial3D { AlbedoColor = c.Lightened(0.1f), Roughness = 0.9f };
                float fH = 1.5f, fT = 0.18f, penR = 7f;
                var fenceMesh = new BoxMesh { Size = new Vector3(penR * 2, fH, fT), Material = fenceMat };
                var postMesh = new BoxMesh { Size = new Vector3(0.35f, fH + 0.4f, 0.35f), Material = postMat };
                g.AddChild(new MeshInstance3D { Mesh = fenceMesh, Position = new Vector3(0f, fH * 0.5f, -penR) });
                g.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(fT, fH, penR * 2), Material = fenceMat },
                    Position = new Vector3(-penR, fH * 0.5f, 0f),
                });
                g.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(fT, fH, penR * 2), Material = fenceMat },
                    Position = new Vector3(penR, fH * 0.5f, 0f),
                });
                foreach (float gx in new[] { -2.2f, 2.2f })
                    g.AddChild(new MeshInstance3D { Mesh = postMesh, Position = new Vector3(gx, (fH + 0.4f) * 0.5f, penR) });
            }
        }
    }

    private void BuildFortresses()
    {
        float z = 4f;
        for (int idx = FortressCount; idx >= 1; idx--) // #10 far north, #1 near spawn
        {
            float depth = 9f + 1.2f * idx;
            // Blue and red fortresses face each other across the road at the same Z
            var blueFort = new Fortress(Team.Blue, idx, z, z + depth, Rng);
            AddChild(blueFort);
            Fortresses.Add(blueFort);

            var redFort = new Fortress(Team.Red, idx, z, z + depth, Rng);
            AddChild(redFort);
            Fortresses.Add(redFort);

            z += depth + 8f;
        }
        SpawnZ = z + 3f;
        PlayerSpawnZ = SpawnZ; // both teams spawn south of the last fortress, run north
    }

    private void SpawnRunners()
    {
        Runner Spawn(Team t, bool player, int lane, float zOff)
        {
            var r = new Runner(t, isPlayer: player);
            r.TargetLane = lane;
            r.Position = new Vector3(Game.LaneX[lane], 0f, Game.Instance.PlayerSpawnZ + zOff);
            AddChild(r);
            Runners.Add(r);
            return r;
        }
        // both teams spawn at the south end (+Z), running north side by side
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
        _cam.Position = Player.Position + new Vector3(0f, 12f, -Player.Heading * 18f);
        _cam.LookAt(Player.Position + new Vector3(0f, 1.5f, Player.Heading * 12f), Vector3.Up);
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
    public static int Heading(Team t) => -1; // both teams run north (same direction, per concept art)
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
        return new Vector3(LaneX[LaneBase(r.Team) + 1], 0f, PlayerSpawnZ);
    }

    // ---- combat plumbing ----

    public void DamageBlock(Block b, float dmg)
    {
        b.Fortress.DamageBlock(b, dmg);
    }

    public void AddFx(Node3D fx) => AddChild(fx);

    public void Shake(float amount = 0.35f) => _shake = amount;

    public void RunnerDied(Runner r)
    {
        Hud?.Announce(r == Player ? "你被击败了…" : $"{NameOf(r)} 被击败", 1.5f);
        Audio?.Play("runner_dead");
    }

    public static string NameOf(Runner r) => r.IsPlayer ? "你" : (r.Team == Team.Blue ? "蓝方队友" : "红方敌人");

    public void OnFortressDestroyed(Fortress f)
    {
        Hud?.UpdateFortressSquares();
        Hud?.Announce($"{(f.Team == Team.Blue ? "蓝" : "红")}{f.Index} 号城池被摧毁！", 2.5f);
        Audio?.Play("fortress_destroyed");
        Audio?.Play("announce");
        if (GameOver) return;
        if (f.Team == Team.Red && f.Index == FortressCount)
        {
            BeginEndgameOrbit(f);
            EndGame(winner: Team.Blue);
        }
        else if (f.Team == Team.Blue && f.Index == FortressCount)
        {
            BeginEndgameOrbit(f);
            EndGame(winner: Team.Red);
        }
    }

    /// <summary>Hand the camera from the chase rig to the victory orbit around the fallen keep.</summary>
    private void BeginEndgameOrbit(Fortress f)
    {
        _orbitFocus = f.KeepCenter;
        _orbitAngle = Mathf.Atan2(_cam.Position.Z - _orbitFocus.Z, _cam.Position.X - _orbitFocus.X);
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
        Audio?.Play(winner == Team.Blue ? "victory" : "defeat");

        if (Net?.IsHost == true && Multiplayer.HasMultiplayerPeer())
            Rpc(nameof(RpcGameEnd), winner == Team.Blue);
    }

    // ---- online: spectator mode ----

    public void ChooseMode(GameMode mode)
    {
        CurrentMode = mode;
        if (mode == GameMode.SpectatorGuest)
        {
            // guest: disable player input, free-cam only
            if (Player != null) Player.IsPlayer = false;
        }
    }

    private float _snapTimer;
    private const float SnapshotInterval = 0.05f;

    public override void _PhysicsProcess(double delta)
    {
        if (CurrentMode == GameMode.SpectatorHost && Net?.IsOnline == true)
        {
            _snapTimer += (float)delta;
            if (_snapTimer >= SnapshotInterval)
            {
                _snapTimer = 0f;
                SendSnapshot();
            }
        }
    }

    /// <summary>Host sends a compact game-state snapshot to the spectator.</summary>
    private void SendSnapshot()
    {
        // send player position + nearest fortress HPs (compact)
        float px = Player != null ? Player.Position.X : 0f;
        float pz = Player != null ? Player.Position.Z : 0f;
        Rpc(nameof(RpcSnapshot), px, pz, (int)(Player?.Hp ?? 0));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void RpcSnapshot(float px, float pz, int hp)
    {
        // guest: move the camera to follow the host's player position
        if (CurrentMode != GameMode.SpectatorGuest) return;
        if (Player != null)
            Player.Position = new Vector3(px, 0f, pz);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RpcGameEnd(bool blueWon)
    {
        if (CurrentMode != GameMode.SpectatorGuest) return;
        var destroyed = new List<string>();
        foreach (var f in Fortresses)
            if (f.Destroyed && f.Team == (blueWon ? Team.Red : Team.Blue))
                destroyed.Add($"{(blueWon ? "红" : "蓝")}{f.Index}");
        Hud.ShowEndScreen(blueWon, destroyed);
        Audio?.Play(blueWon ? "victory" : "defeat");
    }
}
