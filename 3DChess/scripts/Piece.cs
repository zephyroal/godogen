using Godot;

namespace Xiangqi3D;

/// <summary>A turned wooden 3D piece with an engraved character. Handles select/move/capture animations.</summary>
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

    public override void _Ready()
    {
        BuildBody();
        Position = Board.WorldOf(Index);
    }

    private static ImageTexture _redGrain, _blackGrain;

    private void BuildBody()
    {
        bool red = Side == Side.Red;
        int seed = (int)Type * 37 + (red ? 7 : 131) + Index * 5;
        var rng = new System.Random(seed);
        float tint = (rng.NextSingle() - 0.5f) * 0.08f;
        Color woodCol = red ? new Color(0.88f, 0.70f, 0.47f) : new Color(0.72f, 0.56f, 0.36f);
        woodCol = tint >= 0 ? woodCol.Lightened(tint) : woodCol.Darkened(-tint);
        var grain = red
            ? _redGrain ??= FX.WoodGrain(new Color(1f, 1f, 1f), new Color(0.74f, 0.72f, 0.69f), 21, 9f, 128)
            : _blackGrain ??= FX.WoodGrain(new Color(1f, 1f, 1f), new Color(0.72f, 0.70f, 0.67f), 33, 9f, 128);
        var wood = new StandardMaterial3D
        {
            AlbedoColor = woodCol,
            AlbedoTexture = grain,
            Roughness = 0.42f + (rng.NextSingle() - 0.5f) * 0.08f,
            Metallic = 0.05f,
            ClearcoatEnabled = true,
            Clearcoat = red ? 0.45f : 0.4f,
            ClearcoatRoughness = 0.25f,
        };
        var rimMat = new StandardMaterial3D
        {
            AlbedoColor = red ? new Color(0.62f, 0.16f, 0.12f) : new Color(0.16f, 0.15f, 0.14f),
            Roughness = 0.6f,
        };

        // turned profile: wide base → waist ring → shoulder → domed cap
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
        var dome = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.30f, Height = 0.60f, Material = wood },
            Position = new Vector3(0f, 0.37f, 0f),
            Scale = new Vector3(1f, 0.42f, 1f),
        };
        AddChild(dome);

        var font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };
        var glyph = new Label3D
        {
            Text = CharFor(Type, Side),
            Font = font,
            FontSize = 50,
            OutlineSize = red ? 6 : 8,
            Modulate = red ? new Color(0.68f, 0.12f, 0.08f) : new Color(0.13f, 0.11f, 0.09f),
            OutlineModulate = new Color(0.96f, 0.91f, 0.80f),
            Position = new Vector3(0f, 0.53f, 0f),
            RotationDegrees = new Vector3(-90f, 0f, 0f), // flat on the cap, readable from the camera side
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
                float xz = k; // linear slide
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
