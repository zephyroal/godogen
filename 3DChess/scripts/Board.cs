using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>Wooden xiangqi board: desk, slab, carved lines, palace diagonals, river text, star markers, capture trays, highlight markers.</summary>
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
        var desk = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(15f, 0.8f, 15.5f), Material = Mat(new Color(0.26f, 0.16f, 0.09f), 0.7f) },
            Position = new Vector3(0f, -0.55f, 0f),
        };
        AddChild(desk);
    }

    private void BuildSlab()
    {
        var maple1 = new Color(0.82f, 0.63f, 0.40f);
        var maple2 = new Color(0.78f, 0.58f, 0.36f);
        var slab = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(9.6f, 0.32f, 10.6f), Material = Mat(new Color(0.72f, 0.52f, 0.30f), 0.55f) },
            Position = new Vector3(0f, -0.16f, 0f),
        };
        AddChild(slab);

        // plank stripes on the playing surface
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
            mm.SetInstanceColor(i, i % 2 == 0 ? maple1 : maple2);
        }
        var stripes = new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        stripes.MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.55f };
        AddChild(stripes);

        // raised frame around the slab
        var frameMat = Mat(new Color(0.45f, 0.29f, 0.15f), 0.5f);
        foreach (var (w, d, x, z) in new[] { (10.1f, 0.25f, 0f, -5.42f), (10.1f, 0.25f, 0f, 5.42f), (0.25f, 10.6f, -4.92f, 0f), (0.25f, 10.6f, 4.92f, 0f) })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, 0.1f, d), Material = frameMat },
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
        var trayMat = Mat(new Color(0.50f, 0.33f, 0.18f), 0.55f);
        foreach (float sx in new[] { -6.6f, 6.6f })
        {
            var tray = new Node3D { Position = new Vector3(sx, -0.02f, 0f), Name = "Tray" };
            AddChild(tray);
            tray.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(2.2f, 0.12f, 5.6f), Material = trayMat },
                Position = new Vector3(0f, -0.06f, 0f),
            });
            foreach (var (w, d, x, z) in new[] { (2.2f, 0.12f, 0f, -2.74f), (2.2f, 0.12f, 0f, 2.74f), (0.12f, 5.6f, -1.04f, 0f), (0.12f, 5.6f, 1.04f, 0f) })
                tray.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(w, 0.16f, d), Material = trayMat },
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
            EmissionEnergyMultiplier = 1.4f,
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
            EmissionEnergyMultiplier = 1.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _checkRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.48f, Material = redGlow },
            Position = new Vector3(0f, 0.03f, 0f),
            Visible = false,
        };
        AddChild(_checkRing);

        var lastMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.75f, 1f, 0.55f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.6f, 1f),
            EmissionEnergyMultiplier = 0.8f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        foreach (var slot in new[] { 0, 1 })
        {
            var m = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.02f, 0.16f), Material = lastMat },
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(m);
            if (slot == 0) _lastFrom = m; else _lastTo = m;
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
        _lastFrom.Position = new Vector3(WorldOf(m.From).X, 0.008f, WorldOf(m.From).Z);
        _lastTo.Position = new Vector3(WorldOf(m.To).X, 0.008f, WorldOf(m.To).Z);
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
            EmissionEnergyMultiplier = 1.2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var capMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.35f, 0.25f, 0.9f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.3f, 0.2f),
            EmissionEnergyMultiplier = 1.5f,
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
