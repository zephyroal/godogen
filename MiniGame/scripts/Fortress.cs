using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// One fortress: a maze of destructible wall blocks on the three lanes of one side.
/// Complexity scales with Index (1 = simple outpost … 10 = ultimate main city).
/// Total HP = sum of block HP; at zero the fortress collapses into non-blocking ruins.
/// </summary>
public partial class Fortress : Node3D
{
	private static readonly Dictionary<int, StandardMaterial3D> MatCache = new();

	public Team Team;
	public int Index;
	public float ZMin, ZMax;
	public float TotalHp, CurrentHp;
	public bool Destroyed;
	public readonly List<Block> Blocks = new();

	public float CenterZ => (ZMin + ZMax) * 0.5f;
	/// <summary>World position of the main-city keep / fortress heart (endgame camera focus).</summary>
	public Vector3 KeepCenter => new(Game.LaneX[Game.LaneBase(Team) + 1], 0f, CenterZ);
	public Block Core { get; private set; }
	private Label3D _sign;
	private int _hpTicks = -1;

	private Node3D _keep;                  // GLB castle of the #10 main city
	private Vector3 _keepBase;
	private Vector3 _crumbleDir;
	private float _crumbleT = -1f;         // <0 idle; 0..1 = keep keeling over
	private StandardMaterial3D _coreMat;  // per-fortress clone: hit-flash spikes the emissive
	private float _coreFlash;

	public Fortress(Team team, int index, float zA, float zB, System.Random rng)
	{
		Team = team;
		Index = index;
		ZMin = Mathf.Min(zA, zB);
		ZMax = Mathf.Max(zA, zB);
		Name = $"Fort_{(team == Team.Blue ? "Blue" : "Red")}{index}";
		Build(rng);
	}

	public bool OwnsLane(int laneIdx) => laneIdx >= Game.LaneBase(Team) && laneIdx < Game.LaneBase(Team) + 3;

	private void Build(System.Random rng)
	{
		int rows = Mathf.Max(2, Mathf.CeilToInt((ZMax - ZMin) / Game.BlockSize) - 1);
		int baseLane = Game.LaneBase(Team);

		float zFront = ZMax; // approach side: both teams come from +Z (south)

		// the #1 outpost and #10 main city both grow a GLB castle keep — wall cells under its footprint make way
		bool hasKeep = Index == 1 || Index == Game.FortressCount;
		Vector3 keepHalf = hasKeep
			? Glb.ScaledHalfExtents(CastlePath(), Index == 1 ? 8f : Game.CastleHeight)
			: Vector3.Zero;

		for (int r = 0; r < rows; r++)
		{
			float z = ZMin + 1.5f + r * Game.BlockSize;
			bool gateRow = r == 0;
			int openLane = rng.Next(3);
			if (r == rows - 1 && openLane == 1) openLane = rng.Next(2) * 2; // keep a side lane open beside the core
			for (int l = 0; l < 3; l++)
			{
				int laneIdx = baseLane + l;
				bool isGate = gateRow && l == 1;
				if (keepHalf != Vector3.Zero
					&& Mathf.Abs(Game.LaneX[laneIdx] - Game.LaneX[baseLane + 1]) < keepHalf.X - 0.3f
					&& Mathf.Abs(z - CenterZ) < keepHalf.Z - 0.3f)
					continue; // castle keep footprint
				float prob = 0.16f + 0.022f * Index;
				if (r == rows - 1) prob += 0.10f; // protect the core row
				if (isGate || l == openLane) continue;
				if ((float)rng.NextDouble() >= prob) continue;
				int height = Index >= 10 ? 3 : (Index >= 8 || (Index >= 4 && rng.NextDouble() < 0.5)) ? 2 : 1;
				for (int h = 0; h < height; h++)
					AddWallBlock(laneIdx, z, h);
			}
		}

		// core crystal on the last row, middle lane. The #10 keep displaces wall cells and
		// gate pillars, so its crystal is armored to keep the main city the toughest fortress.
		float coreZ = ZMin + 1.5f + (rows - 1) * Game.BlockSize;
		float coreHp = 90f + 12f * Index + (Index == Game.FortressCount ? 50f : 0f);
		_coreMat = CoreMat();
		Core = MakeBlock(Game.LaneX[baseLane + 1], 2.2f, coreZ, _coreMat, hp: coreHp, laneIdx: baseLane + 1, isCore: true);
		Core.Scale = new Vector3(0.87f, 0.73f, 0.87f);
		Core.RotationDegrees = new Vector3(0f, 45f, 0f);

		// dynamic point light: the core crystal glows and illuminates its surroundings
		var coreColor = Game.ColorOf(Team);
		AddChild(new OmniLight3D
		{
			Position = new Vector3(Game.LaneX[baseLane + 1], 2.2f, coreZ),
			LightColor = coreColor,
			LightEnergy = 3f,
			OmniRange = 8f,
			OmniAttenuation = 1.2f,
			ShadowEnabled = false,
		});

		// glowing gate portal at the fortress entrance (middle lane, enemy-facing edge)
		BuildGate(zFront, baseLane);

		// curtain walls connect the four corner pillars into a compound (per concept art)
		BuildWalls(rng, baseLane, zFront);

		// corner pillars (outside the lanes — target practice + HP pool, capped with battlements).
		// The gate-side pair becomes GLB watchtowers; the rear pair stays blocky target practice.
		float[] pillarX =
		{
			Game.LaneX[baseLane] - 2.5f,
			(Game.LaneX[baseLane + 1] + Game.LaneX[baseLane + 2]) * 0.5f,
		};
		float zRear = (ZMin + ZMax) - zFront;
		foreach (var px in pillarX)
		{
			var watchtower = Glb.Create(TowerPath(), Game.TowerHeight);
			if (watchtower != null)
			{
				watchtower.Position = new Vector3(px, 0f, zFront);
				watchtower.RotationDegrees = new Vector3(0f, EnemyFacingYaw(), 0f);
				AddChild(watchtower);
			}
			else
			{
				for (int h = 0; h < 2; h++)
				{
					var p = MakeBlock(px, 1.5f + h * Game.BlockSize, zFront, WallMat(rng), hp: 40f, laneIdx: -1, isCore: false);
					p.Scale = new Vector3(0.5f, 1f, 0.5f);
				}
				AddBattlement(px, zFront);
			}

			for (int h = 0; h < 2; h++)
			{
				var p = MakeBlock(px, 1.5f + h * Game.BlockSize, zRear, WallMat(rng), hp: 40f, laneIdx: -1, isCore: false);
				p.Scale = new Vector3(0.5f, 1f, 0.5f);
			}
			AddBattlement(px, zRear);
		}

		// the #1 outpost and #10 main city both get a GLB castle keep (home landmark + objective)
		if (Index == 1 || Index == Game.FortressCount)
		{
			float keepHeight = Index == 1 ? 6f : Game.CastleHeight;
			var keep = Glb.Create(CastlePath(), keepHeight);
			if (keep != null)
			{
				keep.Position = new Vector3(Game.LaneX[baseLane + 1], 0f, CenterZ);
				keep.RotationDegrees = new Vector3(0f, EnemyFacingYaw(), 0f); // gate toward the enemy
				AddChild(keep);
				_keep = keep;
				_keepBase = keep.Position;
				_crumbleDir = new Vector3(Mathf.Sin(Index * 2.1f), 0f, Mathf.Cos(Index * 1.3f)).Normalized();
			}
			else
			{
				var crown = new MeshInstance3D
				{
					Mesh = new BoxMesh
					{
						Size = new Vector3(1.3f, 0.7f, 1.3f),
						Material = new StandardMaterial3D
						{
							AlbedoColor = new Color(1f, 0.82f, 0.25f),
							EmissionEnabled = true,
							Emission = new Color(1f, 0.8f, 0.2f),
							EmissionEnergyMultiplier = 1.2f,
						},
					},
					Position = new Vector3(Game.LaneX[baseLane + 1], 5.2f, coreZ),
				};
				AddChild(crown);
			}
		}

		TotalHp = CurrentHp = SumBlockHp();

		// floating sign: 编号 + HP bar
		_sign = new Label3D
		{
			Text = SignText(),
			Position = new Vector3(Game.LaneX[baseLane + 1], 10.5f, zFront + 1.5f),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FontSize = 64,
			OutlineSize = 12,
			Modulate = Game.ColorOf(Team),
			Font = SharedFont(),
		};
		AddChild(_sign);
	}

	private float SumBlockHp()
	{
		float s = 0f;
		foreach (var b in Blocks) s += b.Hp;
		return s;
	}

	private void AddWallBlock(int laneIdx, float z, int level)
	{
		var rng = Game.Instance?.Rng;
		float y = 1.5f + level * Game.BlockSize;
		MakeBlock(Game.LaneX[laneIdx], y, z, WallMat(rng), hp: 20f, laneIdx: laneIdx, isCore: false);
	}

	private Block MakeBlock(float x, float y, float z, StandardMaterial3D mat, float hp, int laneIdx, bool isCore)
	{
		var b = new Block
		{
			Mesh = SharedBox(),
			Hp = hp,
			Fortress = this,
			IsCore = isCore,
			LaneIdx = laneIdx,
			Position = new Vector3(x, y, z),
			MaterialOverride = mat,
		};
		Blocks.Add(b);
		AddChild(b);
		return b;
	}

	private static BoxMesh SharedBox()
	{
		if (_box == null) _box = new BoxMesh { Size = new Vector3(Game.BlockSize, Game.BlockSize, Game.BlockSize) };
		return _box;
	}
	private static BoxMesh _box;

	private static SystemFont SharedFont()
	{
		if (_font == null)
			_font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };
		return _font;
	}
	private static SystemFont _font;

	private string CastlePath() => $"res://assets/glb/castle_{(Team == Team.Blue ? "blue" : "red")}.glb";
	private string TowerPath() => $"res://assets/glb/tower_{(Team == Team.Blue ? "blue" : "red")}.glb";

	/// <summary>Yaw that turns a GLB's -Z gate toward the road (blue faces +X, red faces -X).</summary>
    private float EnemyFacingYaw() => Team == Team.Blue ? 270f : 90f;

    private StandardMaterial3D WallMat(System.Random rng)
    {
        int shade = rng?.Next(4) ?? 0;
        int key = ((int)Team + 1) * 100 + shade;
        if (!MatCache.TryGetValue(key, out var mat))
        {
            var c = Game.ColorOf(Team);
            float f = 1f - shade * 0.13f;
            mat = new StandardMaterial3D { AlbedoColor = new Color(c.R * f, c.G * f, c.B * f), Roughness = 0.3f, Metallic = 0.25f };
            MatCache[key] = mat;
        }
        return mat;
    }

    private StandardMaterial3D CoreMat()
    {
        // fresh per fortress: hit-flash spikes the emissive, must not leak across cores
        var c = Game.ColorOf(Team);
        return new StandardMaterial3D
        {
            AlbedoColor = c.Lightened(0.15f),
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = 1.6f,
            Roughness = 0.3f,
            Metallic = 0.2f,
        };
    }

    /// <summary>Glowing portal frame at the road-facing side of the fortress (decorative, never blocks lanes).</summary>
    private void BuildGate(float zFront, int baseLane)
    {
        var c = Game.ColorOf(Team);
        float gateZ = CenterZ;
        float height = Index == Game.FortressCount ? 7.2f : 5.5f;

        // gate sits at the road-facing edge; posts span Z, portal panel faces the road
        float roadX = Team == Team.Blue
            ? Game.LaneX[baseLane + 2] + 2f   // just outside the inner blue lane, toward road
            : Game.LaneX[baseLane] - 2f;       // just outside the inner red lane, toward road

        var frameMat = new StandardMaterial3D { AlbedoColor = c.Darkened(0.25f), Roughness = 0.4f, Metallic = 0.3f };
        var post = new BoxMesh { Size = new Vector3(0.8f, height, 0.8f), Material = frameMat };
        foreach (float off in new[] { -2.4f, 2.4f })
            AddChild(new MeshInstance3D { Mesh = post, Position = new Vector3(roadX, height * 0.5f, gateZ + off) });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 5.6f), Material = frameMat },
            Position = new Vector3(roadX, height, gateZ),
        });

        var glowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(c.R, c.G, c.B, 0.42f),
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = 3.2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, height - 0.6f, 4f), Material = glowMat },
            Position = new Vector3(roadX, (height - 0.6f) * 0.5f, gateZ),
        });

        // dynamic point light: the gate portal casts team-color light on nearby walls
        AddChild(new OmniLight3D
        {
            Position = new Vector3(roadX, height * 0.5f, gateZ),
            LightColor = c,
            LightEnergy = 4f,
            OmniRange = 10f,
            OmniAttenuation = 1.5f,
            ShadowEnabled = false,
        });
    }

    /// <summary>Compound GLB replaces procedural curtain walls; falls back to BoxMesh walls.</summary>
    private void BuildWalls(System.Random rng, int baseLane, float zFront)
    {
        float outerX = Game.LaneX[baseLane] - 3.5f;
        float innerX = (Game.LaneX[baseLane + 1] + Game.LaneX[baseLane + 2]) * 0.5f;
        float zRear = (ZMin + ZMax) - zFront;
        float compoundW = innerX - outerX;
        float compoundD = zRear - zFront;
        float compoundH = 5f; // ~2x character height

        string compoundPath = $"res://assets/glb/compound_{(Team == Team.Blue ? "blue" : "red")}.glb";
        var compound = Glb.Create(compoundPath, compoundH);
        if (compound != null)
        {
            compound.Position = new Vector3((outerX + innerX) * 0.5f, 0f, (zFront + zRear) * 0.5f);
            compound.RotationDegrees = new Vector3(0f, EnemyFacingYaw(), 0f);
            // stretch to fill the fortress footprint
            compound.Scale = new Vector3(compoundW / 9f, 1f, compoundD / 9f);
            AddChild(compound);
            return;
        }

        // fallback: procedural curtain walls
        float wallY = 2.5f;
        float wallT = 0.6f;
        var mat = WallMat(rng);
        void Wall(float x, float z, float sx, float sz)
        {
            var b = MakeBlock(x, wallY, z, mat, hp: 30f, laneIdx: -1, isCore: false);
            b.Scale = new Vector3(sx / Game.BlockSize, compoundH / Game.BlockSize, sz / Game.BlockSize);
        }
        float gapHalf = 2.8f;
        float midX = (outerX + innerX) * 0.5f;
        float frontLenL = Mathf.Max(0.1f, midX - gapHalf - outerX);
        float frontLenR = Mathf.Max(0.1f, innerX - (midX + gapHalf));
        if (frontLenL > 0.5f) Wall((outerX + midX - gapHalf) * 0.5f, zFront, frontLenL, wallT);
        if (frontLenR > 0.5f) Wall((midX + gapHalf + innerX) * 0.5f, zFront, frontLenR, wallT);
        Wall(midX, zRear, innerX - outerX, wallT);
        Wall(outerX, (zFront + zRear) * 0.5f, wallT, zRear - zFront);
        Wall(innerX, (zFront + zRear) * 0.5f, wallT, zRear - zFront);
    }

    /// <summary>Battlement cap on top of a pillar (decorative).</summary>
    private void AddBattlement(float px, float pz)
    {
        var c = Game.ColorOf(Team);
        var capMat = new StandardMaterial3D { AlbedoColor = c.Lightened(0.08f), Roughness = 0.3f, Metallic = 0.4f };
        var cap = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2f, 0.35f, 2f), Material = capMat },
            Position = new Vector3(px, 7.65f, pz),
        };
        AddChild(cap);
        var merlon = new BoxMesh { Size = new Vector3(0.6f, 0.55f, 0.6f), Material = capMat };
        foreach (float ox in new[] { -0.65f, 0.65f })
            AddChild(new MeshInstance3D { Mesh = merlon, Position = new Vector3(px + ox, 8.1f, pz) });
    }

    // ---- damage / collapse ----

    public void DamageBlock(Block b, float dmg)
    {
        if (Destroyed || !Blocks.Contains(b)) return;
        CurrentHp -= dmg;
        _coreFlash = 1f; // the crystal winces
        b.TakeDamage(dmg); // may remove itself
        RefreshSign();
        if (CurrentHp <= 0f && !Destroyed) Collapse();
    }

    public void OnBlockRemoved(Block b) => Blocks.Remove(b);

    /// <summary>Per-frame visuals: crystal idle pulse + hit flash, keep crumble after collapse.</summary>
    public void TickVisuals(float dt, float pulse)
    {
        if (!Destroyed && Core != null && IsInstanceValid(Core))
        {
            if (_coreFlash > 0f) _coreFlash = Mathf.Max(0f, _coreFlash - dt * 4f);
            Core.Scale = new Vector3(0.87f, 0.73f, 0.87f) * pulse * (1f + 0.14f * _coreFlash);
            if (_coreMat != null)
                _coreMat.EmissionEnergyMultiplier = 1.6f + 3.4f * _coreFlash;
        }

        if (_crumbleT >= 0f && _keep != null && IsInstanceValid(_keep))
        {
            _crumbleT = Mathf.Min(1f, _crumbleT + dt / 1.4f);
            float e = 1f - Mathf.Pow(1f - _crumbleT, 3f); // ease-out: the keep keels over
            _keep.Position = new Vector3(_keepBase.X, _keepBase.Y - 2.8f * e, _keepBase.Z);
            _keep.RotationDegrees = new Vector3(_crumbleDir.X * 16f * e, EnemyFacingYaw(), _crumbleDir.Z * 16f * e);
        }
    }

    /// <summary>Hide the floating sign beyond ~95 units from the viewer to avoid distant label pile-up.</summary>
    public void UpdateVisibility(Vector3 viewer)
    {
        if (_sign == null) return;
        _sign.Visible = Mathf.Abs(CenterZ - viewer.Z) < 95f;
    }

    private void Collapse()
    {
        Destroyed = true;
        Game.Instance?.Shake(0.5f);
        foreach (var b in Blocks)
        {
            if (IsInstanceValid(b))
            {
                var fx = FX.BlockBurst(b.GlobalPosition, Game.ColorOf(Team), 10);
                Game.Instance?.AddChild(fx);
                b.QueueFree();
            }
        }
        Blocks.Clear();

		// the main city's keep keels over with the fortress it guarded
		if (_keep != null && IsInstanceValid(_keep))
		{
			_crumbleT = 0f;
			Game.Instance?.AddChild(FX.BlockBurst(new Vector3(_keepBase.X, 6f, _keepBase.Z), Game.ColorOf(Team), 26));
		}

		// rubble: GLB model or procedural broken fragments (visual, never blocks lanes)
		int baseLane = Game.LaneBase(Team);
		float outerX = Game.LaneX[baseLane] - 3.5f;
		float innerX = (Game.LaneX[baseLane + 1] + Game.LaneX[baseLane + 2]) * 0.5f;
		var rng = Game.Instance?.Rng ?? new System.Random();

		var rubbleGlb = Glb.Create("res://assets/glb/rubble.glb", 2f);
		if (rubbleGlb != null)
		{
			rubbleGlb.Position = new Vector3((outerX + innerX) * 0.5f, 0f, CenterZ);
			rubbleGlb.Scale = new Vector3((innerX - outerX) / 4f, 1f, (ZMax - ZMin) / 4f);
			AddChild(rubbleGlb);
		}
		else
		{
			var rubbleMat = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.42f, 0.44f), Roughness = 1f };
			var teamRubbleMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(Game.ColorOf(Team).R * 0.4f, Game.ColorOf(Team).G * 0.4f, Game.ColorOf(Team).B * 0.4f),
				Roughness = 1f,
			};
			for (int i = 0; i < 14; i++)
			{
				bool isPillar = i % 3 == 0;
				float x = Mathf.Lerp(outerX, innerX, (float)rng.NextDouble());
				float z = Mathf.Lerp(ZMin, ZMax, (float)rng.NextDouble());
				float h = isPillar ? 1.2f + (float)rng.NextDouble() * 1.5f : 0.3f + (float)rng.NextDouble() * 0.5f;
				float w = isPillar ? 0.6f : 1.2f + (float)rng.NextDouble() * 0.8f;
				float d = isPillar ? 0.6f : 0.8f + (float)rng.NextDouble() * 0.6f;
				float tilt = isPillar ? (float)rng.NextDouble() * 30f - 15f : (float)rng.NextDouble() * 20f;
				AddChild(new MeshInstance3D
				{
					Mesh = new BoxMesh { Size = new Vector3(w, h, d) },
					MaterialOverride = i % 4 == 0 ? teamRubbleMat : rubbleMat,
					Position = new Vector3(x, h * 0.5f, z),
					RotationDegrees = new Vector3(tilt, (float)rng.NextDouble() * 180f, tilt * 0.5f),
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
			}
		}

		_sign.Modulate = new Color(0.55f, 0.55f, 0.55f);
		RefreshSign();
		Game.Instance?.OnFortressDestroyed(this);
	}

	public Vector3 RespawnPoint()
	{
		// respawn south of the fortress (approach side, +Z), where runners come from
		return new Vector3(Game.LaneX[Game.LaneBase(Team) + 1], 0f, ZMax + 2.5f);
	}

	private void RefreshSign()
	{
		if (_sign == null) return;
		int ticks = Mathf.Clamp(Mathf.CeilToInt(CurrentHp / TotalHp * 10f), 0, 10);
		if (ticks == _hpTicks && !Destroyed) return;
		_hpTicks = ticks;
		_sign.Text = SignText();
		float pct = CurrentHp / TotalHp;
		_sign.Modulate = Destroyed ? new Color(0.55f, 0.55f, 0.55f)
			: pct > 0.5f ? Game.ColorOf(Team)
			: pct > 0.25f ? new Color(1f, 0.75f, 0.25f)
			: new Color(1f, 0.35f, 0.3f);
	}

	private string SignText()
	{
		string label = $"{(Team == Team.Blue ? "蓝" : "红")}{Index}" + (Index == Game.FortressCount ? " 主城" : "");
		if (Destroyed) return $"{label} ✗ 已摧毁";
		int ticks = Mathf.Clamp(Mathf.CeilToInt(CurrentHp / TotalHp * 10f), 0, 10);
		string bar = "";
		for (int i = 0; i < 10; i++) bar += i < ticks ? "▮" : "▯";
		return $"{label} {bar} {Mathf.CeilToInt(CurrentHp)}";
	}
}
