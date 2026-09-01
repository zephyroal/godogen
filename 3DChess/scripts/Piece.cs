using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>A turned wooden 3D piece. Loads a GLB model if available; falls back to procedural geometry.
/// Handles select/move/capture animations.</summary>
public partial class Piece : Node3D
{
    public Side Side;
    public PieceType Type;
    public int Index;
    public bool Alive = true;

    public enum AnimState { Idle, Selected, Moving, Captured }
    public AnimState State = AnimState.Idle;

    private float _t, _bob;
    private Vector3 _from, _to;
    private float _arcHeight;
    public bool AnimDone { get; private set; }

    // GLB scene cache: loaded once per (type, side), reused across all instances
    private static readonly Dictionary<(PieceType, Side), PackedScene> _glbCache = new();

    private static string GlbPath(PieceType t, Side s)
    {
        string type = t switch
        {
            PieceType.King => "king",
            PieceType.Advisor => "advisor",
            PieceType.Elephant => "elephant",
            PieceType.Horse => "horse",
            PieceType.Chariot => "chariot",
            PieceType.Cannon => "cannon",
            _ => "soldier",
        };
        string side = s == Side.Red ? "red" : "black";
        return $"res://assets/glb/{type}_{side}.glb";
    }

    private static PackedScene LoadGlb(PieceType t, Side s)
    {
        var key = (t, s);
        if (_glbCache.TryGetValue(key, out var scene)) return scene;
        string path = GlbPath(t, s);
        if (!ResourceLoader.Exists(path)) return null;
        scene = GD.Load<PackedScene>(path);
        if (scene != null) _glbCache[key] = scene;
        return scene;
    }

    public static string CharFor(PieceType t, Side s) => s == Side.Red
        ? t switch
        {
            PieceType.King => "帅",
            PieceType.Advisor => "仕",
            PieceType.Elephant => "相",
            PieceType.Horse => "马",
            PieceType.Chariot => "车",
            PieceType.Cannon => "炮",
            _ => "兵",
        }
        : t switch
        {
            PieceType.King => "将",
            PieceType.Advisor => "士",
            PieceType.Elephant => "象",
            PieceType.Horse => "马",
            PieceType.Chariot => "车",
            PieceType.Cannon => "砲",
            _ => "卒",
        };

    // target height per piece type (relative;帅=1.0)
    private static float TargetHeight(PieceType t) => t switch
    {
        PieceType.King => 0.95f,
        PieceType.Advisor => 0.75f,
        PieceType.Elephant => 0.80f,
        PieceType.Horse => 0.85f,
        PieceType.Chariot => 0.85f,
        PieceType.Cannon => 0.85f,
        _ => 0.65f,
    };

    public override void _Ready()
    {
        var glb = LoadGlb(Type, Side);
        if (glb != null) BuildFromGlb(glb);
        else BuildProcedural();
        Position = Board.WorldOf(Index);
    }

    private void BuildFromGlb(PackedScene glb)
    {
        var instance = glb.Instantiate<Node3D>();
        AddChild(instance);

        // measure AABB and scale to target size
        float targetH = TargetHeight(Type) * 1.1f; // world units
        float targetD = 0.84f; // base diameter fits grid spacing

        var aabb = GetAabb(instance);
        float h = aabb.Size.Y;
        float d = Mathf.Max(aabb.Size.X, aabb.Size.Z);
        float scale = Mathf.Min(targetH / h, targetD / d) * 0.95f;
        // normalize fallback discs to the same footprint as GLB pieces
        if (h < 0.01f) scale = targetD / 0.86f; // degenerate AABB — shouldn't happen, but guard
        instance.Scale = Vector3.One * scale;

        // re-measure after scale and align: bottom at y=0, center on XZ
        aabb = GetAabb(instance);
        instance.Position = new Vector3(-aabb.Position.X - aabb.Size.X * 0.5f, -aabb.Position.Y, -aabb.Position.Z - aabb.Size.Z * 0.5f);

        // orient: red faces north (toward black), black faces south (toward red)
        if (Side == Side.Red)
            instance.RotateY(Mathf.Pi);
    }

    private static Aabb GetAabb(Node3D root)
    {
        Aabb aabb = new Aabb();
        bool first = true;
        void Walk(Node n)
        {
            if (n is MeshInstance3D mi)
            {
                var a = mi.GetAabb();
                a.Position = mi.GlobalTransform * a.Position;
                if (first) { aabb = a; first = false; }
                else aabb = aabb.Merge(a);
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        return aabb;
    }

    private void BuildProcedural()
    {
        bool red = Side == Side.Red;
        var wood = new StandardMaterial3D
        {
            AlbedoColor = red ? new Color(0.88f, 0.70f, 0.47f) : new Color(0.74f, 0.58f, 0.38f),
            Roughness = 0.45f,
        };
        var rimMat = new StandardMaterial3D
        {
            AlbedoColor = red ? new Color(0.62f, 0.16f, 0.12f) : new Color(0.16f, 0.15f, 0.14f),
            Roughness = 0.6f,
        };

        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.31f, BottomRadius = 0.43f, Height = 0.10f, Material = wood },
            Position = new Vector3(0f, 0.05f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.31f, Height = 0.16f, Material = wood },
            Position = new Vector3(0f, 0.18f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.26f, OuterRadius = 0.37f, Material = rimMat },
            Position = new Vector3(0f, 0.29f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.27f, BottomRadius = 0.34f, Height = 0.07f, Material = wood },
            Position = new Vector3(0f, 0.335f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.30f, Height = 0.60f, Material = wood },
            Position = new Vector3(0f, 0.37f, 0f),
            Scale = new Vector3(1f, 0.42f, 1f),
        });

        var font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };
        var glyph = new Label3D
        {
            Text = CharFor(Type, Side),
            Font = font,
            FontSize = 44,
            OutlineSize = 6,
            Modulate = red ? new Color(0.68f, 0.12f, 0.08f) : new Color(0.16f, 0.14f, 0.12f),
            OutlineModulate = new Color(0.95f, 0.9f, 0.78f),
            Position = new Vector3(0f, 0.53f, 0f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            PixelSize = 0.004f,
        };
        AddChild(glyph);
    }

    public void SetSelected(bool on)
    {
        if (State == AnimState.Moving || State == AnimState.Captured) return;
        State = on ? AnimState.Selected : AnimState.Idle;
        if (!on)
        {
            Position = new Vector3(Position.X, 0f, Position.Z);
            Rotation = new Vector3(0f, 0f, 0f);
        }
    }

    public void AnimateMove(Vector3 target, bool jumpHigh)
    {
        _from = Position;
        _to = target;
        _arcHeight = jumpHigh ? 0.9f : 0.55f;
        _t = 0f;
        AnimDone = false;
        State = AnimState.Moving;
        Rotation = new Vector3(0f, 0f, 0f);
    }

    public void AnimateCapture(Vector3 trayPos)
    {
        _from = Position;
        _to = trayPos;
        _arcHeight = 2.2f;
        _t = 0f;
        AnimDone = false;
        State = AnimState.Captured;
    }

    public void Teleport(Vector3 pos)
    {
        Position = pos;
        State = AnimState.Idle;
        AnimDone = true;
        Scale = Vector3.One;
        Rotation = Vector3.Zero;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        switch (State)
        {
            case AnimState.Selected:
                _bob += dt;
                Position = new Vector3(Position.X, 0.32f + 0.04f * Mathf.Sin(_bob * 4f), Position.Z);
                RotateY(dt * 1.4f);
                break;

            case AnimState.Moving:
            case AnimState.Captured:
            {
                _t += dt / 0.45f;
                if (_t >= 1f)
                {
                    Position = _to;
                    if (State == AnimState.Captured) Scale = Vector3.One * 0.82f;
                    State = AnimState.Idle;
                    AnimDone = true;
                    return;
                }
                float k = _t;
                float xz = k;
                float y = Mathf.Sin(Mathf.Pi * k) * _arcHeight;
                Position = new Vector3(
                    Mathf.Lerp(_from.X, _to.X, xz),
                    Mathf.Lerp(_from.Y, _to.Y, k) + y,
                    Mathf.Lerp(_from.Z, _to.Z, xz));
                break;
            }
        }
    }
}
