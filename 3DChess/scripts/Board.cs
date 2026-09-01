using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>Wooden xiangqi board: desk, slab, carved lines, palace diagonals, river text, star markers,
/// capture trays, and highlight markers (selection / legal moves / check / last move / hover).</summary>
public partial class Board : Node3D
{
    public const float S = 1.0f; // grid spacing

    public static Vector3 WorldOf(int idx)
    {
        int f = idx % 9, r = idx / 9;
        return new Vector3((f - 4) * S, 0f, (4.5f - r) * S);
    }

    private readonly List<Node3D> _moveMarks = new();
    private Node3D _selRing, _checkRing, _lastFrom, _lastTo;
    private MeshInstance3D _illegalMark, _hoverMark;
    private StandardMaterial3D _illegalMat;
    private StandardMaterial3D _deskMat, _trayMat;
    private float _pulseT, _illegalT;

    public override void _Ready()
    {
        BuildDesk();
        BuildSlab();
        BuildLines();
        BuildRiverText();
        BuildStarMarks();
        BuildTrays();
        BuildMarkers();
    }

    private static StandardMaterial3D Mat(Color c, float rough = 0.9f, float metal = 0f)
    {
        return new StandardMaterial3D { AlbedoColor = c, Roughness = rough, Metallic = metal };
    }

    private void BuildDesk()
    {
        var tex = FX.WoodGrain(new Color(0.34f, 0.22f, 0.13f), new Color(0.19f, 0.11f, 0.06f), 7, 12f);
        _deskMat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Uv1Scale = new Vector3(3f, 2f, 1f),
            Roughness = 0.65f,
        };
        _trayMat = new StandardMaterial3D
        {
            AlbedoTexture = FX.WoodGrain(new Color(0.46f, 0.31f, 0.19f), new Color(0.27f, 0.17f, 0.10f), 5, 10f),
            Uv1Scale = new Vector3(2f, 1f, 1f),
            Roughness = 0.6f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(11.5f, 0.8f, 12.5f), Material = _deskMat },
            Position = new Vector3(0f, -0.55f, 0f),
        });
    }

    private void BuildSlab()
    {
        var grain = FX.WoodGrain(new Color(0.85f, 0.68f, 0.45f), new Color(0.68f, 0.49f, 0.28f), 11, 16f);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(9.6f, 0.32f, 10.6f),
                Material = new StandardMaterial3D
                {
                    AlbedoTexture = grain,
                    Uv1Scale = new Vector3(2f, 2f, 1f),
                    Roughness = 0.55f,
                },
            },
            Position = new Vector3(0f, -0.16f, 0f),
        });

        // plank stripes: per-plank tint (vertex color) multiplied over the shared grain
        var stripeMesh = new BoxMesh { Size = new Vector3(S * 0.96f, 0.015f, 10.4f) };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = stripeMesh,
            UseColors = true,
            InstanceCount = 9,
        };
        for (int i = 0; i < 9; i++)
        {
            float x = -4f + i * S;
            mm.SetInstanceTransform(i, Transform3D.Identity.Translated(new Vector3(x, -0.005f, 0f)));
            mm.SetInstanceColor(i, i % 2 == 0 ? new Color(1f, 1f, 1f) : new Color(0.92f, 0.87f, 0.80f));
        }
        var stripes = new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        stripes.MaterialOverride = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = grain,
            Uv1Scale = new Vector3(1f, 3f, 1f),
            Roughness = 0.5f,
        };
        AddChild(stripes);

        // raised frame around the slab, same dark wood as the desk
        foreach (var (w, d, x, z) in new[] { (10.1f, 0.25f, 0f, -5.42f), (10.1f, 0.25f, 0f, 5.42f), (0.25f, 10.6f, -4.92f, 0f), (0.25f, 10.6f, 4.92f, 0f) })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, 0.1f, d), Material = _deskMat },
                Position = new Vector3(x, -0.05f, z),
            });
        }
    }

    private void BuildLines()
    {
        var lineMat = Mat(new Color(0.30f, 0.19f, 0.10f), 0.8f);
        void Line(float len, float thick, Vector3 pos, float yawDeg)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(len, 0.012f, thick), Material = lineMat },
                Position = pos,
                RotationDegrees = new Vector3(0f, yawDeg, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        // 10 horizontal lines (full width)
        for (int r = 0; r < 10; r++)
            Line(8 * S, 0.045f, new Vector3(0f, 0.006f, (4.5f - r) * S), 0f);

        // 9 vertical lines, broken at the river except the two edge files
        for (int f = 0; f < 9; f++)
        {
            float x = (f - 4) * S;
            if (f == 0 || f == 8)
            {
                Line(9 * S, 0.045f, new Vector3(x, 0.006f, 0f), 90f);
            }
            else
            {
                Line(4 * S, 0.045f, new Vector3(x, 0.006f, 2.5f * S), 90f);  // ranks 0..4 (z 4.5 → 0.5)
                Line(4 * S, 0.045f, new Vector3(x, 0.006f, -2.5f * S), 90f); // ranks 5..9 (z -0.5 → -4.5)
            }
        }

        // palace diagonals: red center (0, 3.5), black center (0, -3.5)
        float diagLen = Mathf.Sqrt(8f) * S;
        foreach (var (zc, deg) in new[] { (3.5f, 45f), (3.5f, -45f), (-3.5f, 45f), (-3.5f, -45f) })
            Line(diagLen, 0.045f, new Vector3(0f, 0.006f, zc * S), deg);
    }

    private void BuildRiverText()
    {
        var font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };
        var c = new Color(0.32f, 0.20f, 0.10f);
        foreach (var (text, x) in new[] { ("楚 河", -2.5f), ("汉 界", 2.5f) })
        {
            var l = new Label3D
            {
                Text = text,
                Font = font,
                FontSize = 88,
                Modulate = c,
                Position = new Vector3(x, 0.012f, 0f),
                RotationDegrees = new Vector3(-90f, 0f, 0f),
                OutlineSize = 4,
            };
            AddChild(l);
        }
    }

    private void BuildStarMarks()
    {
        var mat = Mat(new Color(0.32f, 0.20f, 0.10f));
        var dot = new BoxMesh { Size = new Vector3(0.1f, 0.01f, 0.1f), Material = mat };
        // cannon points (2,7 ranks) and soldier points (3,6 ranks)
        foreach (int r in new[] { 2, 7 })
            foreach (int f in new[] { 1, 7 })
                AddDot(f, r);
        foreach (int r in new[] { 3, 6 })
            foreach (int f in new[] { 0, 2, 4, 6, 8 })
                AddDot(f, r);

        void AddDot(int f, int r)
        {
            foreach (var (dx, dz) in new[] { (-0.13f, -0.13f), (0.13f, -0.13f), (-0.13f, 0.13f), (0.13f, 0.13f) })
                AddChild(new MeshInstance3D
                {
                    Mesh = dot,
                    Position = new Vector3((f - 4) * S + dx, 0.007f, (4.5f - r) * S + dz),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
        }
    }

    private void BuildTrays()
    {
        foreach (float sx in new[] { -6.6f, 6.6f })
        {
            var tray = new Node3D { Position = new Vector3(sx, -0.02f, 0f), Name = "Tray" };
            AddChild(tray);
            tray.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(2.2f, 0.12f, 5.6f), Material = _trayMat },
                Position = new Vector3(0f, -0.06f, 0f),
            });
            foreach (var (w, d, x, z) in new[] { (2.2f, 0.12f, 0f, -2.74f), (2.2f, 0.12f, 0f, 2.74f), (0.12f, 5.6f, -1.04f, 0f), (0.12f, 5.6f, 1.04f, 0f) })
                tray.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(w, 0.16f, d), Material = _trayMat },
                    Position = new Vector3(x, 0.0f, z),
                });
        }
    }

    private void BuildMarkers()
    {
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.3f, 0.85f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.8f, 0.2f),
            EmissionEnergyMultiplier = 1.8f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _selRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.34f, OuterRadius = 0.44f, Material = glow },
            Position = new Vector3(0f, 0.03f, 0f),
            Visible = false,
        };
        AddChild(_selRing);

        var redGlow = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.2f, 0.15f, 0.9f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.15f, 0.1f),
            EmissionEnergyMultiplier = 2.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _checkRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.48f, Material = redGlow },
            Position = new Vector3(0f, 0.03f, 0f),
            Visible = false,
        };
        AddChild(_checkRing);

        // last move: corner brackets, far clearer at a glance than small squares
        var lastMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.75f, 1f, 0.8f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.6f, 1f),
            EmissionEnergyMultiplier = 1.2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _lastFrom = MakeBracket(lastMat);
        _lastTo = MakeBracket(lastMat);
        AddChild(_lastFrom);
        AddChild(_lastTo);

        // illegal-target flash mark
        _illegalMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.2f, 0.15f, 0.8f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.15f, 0.1f),
            EmissionEnergyMultiplier = 2.0f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _illegalMark = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.02f, 0.55f), Material = _illegalMat },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_illegalMark);

        // desktop hover hint
        _hoverMark = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.13f,
                BottomRadius = 0.13f,
                Height = 0.02f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 1f, 1f, 0.22f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_hoverMark);
    }

    /// <summary>A square of four glowing corner brackets that frame a grid point.</summary>
    private static Node3D MakeBracket(StandardMaterial3D mat)
    {
        var root = new Node3D { Visible = false };
        const float half = 0.42f, leg = 0.26f, th = 0.055f;
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(leg, 0.014f, th), Material = mat },
                Position = new Vector3(sx * (half - leg / 2), 0.008f, sz * half),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(th, 0.014f, leg), Material = mat },
                Position = new Vector3(sx * half, 0.008f, sz * (half - leg / 2)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        return root;
    }

    /// <summary>Brief red flash on an illegal target.</summary>
    public void ShowIllegal(int idx)
    {
        var w = WorldOf(idx);
        _illegalMark.Position = new Vector3(w.X, 0.05f, w.Z);
        _illegalMark.Visible = true;
        _illegalT = 0.45f;
    }

    /// <summary>Faint marker on the grid point under the mouse cursor (desktop only).</summary>
    public void ShowHover(int? idx)
    {
        if (idx == null)
        {
            _hoverMark.Visible = false;
            return;
        }
        var w = WorldOf(idx.Value);
        _hoverMark.Position = new Vector3(w.X, 0.02f, w.Z);
        _hoverMark.Visible = true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _pulseT += dt;

        // breathing pulse on markers
        float dotS = 1f + 0.12f * Mathf.Sin(_pulseT * 5f);
        foreach (var m in _moveMarks)
            if (IsInstanceValid(m)) m.Scale = Vector3.One * dotS;
        _selRing.Scale = Vector3.One * (1f + 0.08f * Mathf.Sin(_pulseT * 4f));
        _checkRing.Scale = Vector3.One * (1f + 0.16f * Mathf.Sin(_pulseT * 6f));

        // illegal flash fade
        if (_illegalT > 0f)
        {
            _illegalT -= dt;
            float a = Mathf.Max(0f, _illegalT / 0.45f);
            _illegalMat.AlbedoColor = new Color(1f, 0.2f, 0.15f, 0.8f * a);
            _illegalMark.Scale = Vector3.One * (1f + 0.25f * (1f - a));
            if (_illegalT <= 0f) _illegalMark.Visible = false;
        }
    }

    public void ShowSelection(int idx)
    {
        _selRing.Position = new Vector3(WorldOf(idx).X, 0.03f, WorldOf(idx).Z);
        _selRing.Visible = true;
    }

    public void HideSelection() => _selRing.Visible = false;

    public void ShowCheck(int? kingIdx)
    {
        if (kingIdx == null) { _checkRing.Visible = false; return; }
        _checkRing.Position = new Vector3(WorldOf(kingIdx.Value).X, 0.03f, WorldOf(kingIdx.Value).Z);
        _checkRing.Visible = true;
    }

    public void ShowLastMove(Move m)
    {
        _lastFrom.Position = new Vector3(WorldOf(m.From).X, 0f, WorldOf(m.From).Z);
        _lastTo.Position = new Vector3(WorldOf(m.To).X, 0f, WorldOf(m.To).Z);
        _lastFrom.Visible = _lastTo.Visible = true;
    }

    public void HideLastMove() => _lastFrom.Visible = _lastTo.Visible = false;

    /// <summary>Spawn glowing dots on legal targets (rings for captures).</summary>
    public void ShowMoves(List<Move> moves, int fromIdx, int[] cells)
    {
        ClearMoves();
        var dotMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.4f, 0.9f, 0.4f, 0.85f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.9f, 0.3f),
            EmissionEnergyMultiplier = 1.6f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var capMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.35f, 0.25f, 0.9f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.3f, 0.2f),
            EmissionEnergyMultiplier = 1.9f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var dotMesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.02f, Material = dotMat };
        var ringMesh = new TorusMesh { InnerRadius = 0.3f, OuterRadius = 0.4f, Material = capMat };

        foreach (var m in moves)
        {
            if (m.From != fromIdx) continue;
            bool capture = cells[m.To] != 0;
            var mark = new MeshInstance3D { Mesh = capture ? ringMesh : dotMesh };
            var w = WorldOf(m.To);
            mark.Position = new Vector3(w.X, 0.035f, w.Z);
            AddChild(mark);
            _moveMarks.Add(mark);
        }
    }

    public void ClearMoves()
    {
        foreach (var m in _moveMarks) m.QueueFree();
        _moveMarks.Clear();
    }
}
