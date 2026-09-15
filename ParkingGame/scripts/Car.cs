using Godot;

namespace ParkingGame;

/// <summary>
/// The player car — a VehicleBody3D with a manual gearbox (R/N/D) and speed-sensitive
/// steering. Controls are written into the public fields by Game every physics tick
/// (keyboard or the --demo script); the physics integration is all Godot's raycast
/// vehicle: per-wheel suspension, friction slip, traction and brake forces.
/// </summary>
public partial class Car : VehicleBody3D
{
    public enum Gear { N, D, R }
    public Gear CurrentGear = Gear.N;

    /// <summary>Sedan (default) or the special SUV. The SUV is larger and gets
    /// the relaxed 70%-area-in-slot verdict rule + its own success fanfare.</summary>
    public enum VehicleKind { Sedan, Suv }
    public VehicleKind Vehicle { get; private set; } = VehicleKind.Sedan;

    // sedan geometry stays a shared constant — parked cars and patrol cars use it
    public const float SedanLen = 4.6f;
    public const float SedanWid = 1.8f;
    // the player's actual body follows the selected vehicle kind
    public float BodyLen = SedanLen;
    public float BodyWid = SedanWid;

    // ---- control inputs (0..1 / -1..1), written externally each tick ----
    public float Throttle;    // 0..1
    public float BrakeInput;  // 0..1
    public float SteerInput;  // -1..1, +1 = wheels left (Godot steering convention)
    public bool Handbrake;

    // ---- tuning ----
    public float MaxEngineForce = 3400f;   // N total at driven wheels (forward)
    public float MaxReverseForce = 2400f;
    public float EngineBrakeForce = 550f;  // coasting drag from the drivetrain
    public float MaxBrake = 7.0f;           // wheel brake strength (tuned empirically)
    public float MaxSteerDeg = 34f;
    public float SteerFadeSpeed = 16f;     // steering clamps down as speed climbs
    public float SteerLerpRate = 7f;
    public float TopSpeedD = 13f;          // m/s (~47 km/h — a parking lot)
    public float TopSpeedR = 6.5f;

    public float ForwardSpeed => -GlobalTransform.Basis.Z.Dot(LinearVelocity);

    private VehicleWheel3D _fl, _fr, _rl, _rr;
    private Node3D _flPivot, _frPivot;     // visual front wheels (steer with input)
    private float _steer;
    private float _collideCooldown;
    private readonly System.Collections.Generic.HashSet<Node> _recentHits = new();
    private StandardMaterial3D _paintMat = null!;  // body+roof paint — swapped by the garage
    private StandardMaterial3D _tailMat = null!;   // taillights — brighten while braking
    private BoxShape3D _chassisShape = null!;
    private Node3D _shell = null!;                 // procedural body, rebuilt per vehicle kind
    private Color _skin = new(0.82f, 0.27f, 0.24f);

    public int CollisionCount { get; private set; }
    public bool CollisionThisTick { get; private set; }

    public override void _Ready()
    {
        Mass = 1200f;
        CanSleep = false;

        // chassis collision (resized by SetVehicle)
        _chassisShape = new BoxShape3D { Size = new Vector3(BodyWid, 1.05f, BodyLen) };
        AddChild(new CollisionShape3D
        {
            Shape = _chassisShape,
            Position = new Vector3(0, 0.55f, 0),
        });

        // ---- wheels: front steer, rear drive (classic RWD parking feel) ----
        _fl = MakeWheel(new Vector3(-0.80f, 0.10f, -1.48f), steering: true);
        _fr = MakeWheel(new Vector3(0.80f, 0.10f, -1.48f), steering: true);
        _rl = MakeWheel(new Vector3(-0.80f, 0.10f, 1.48f), steering: false, traction: true);
        _rr = MakeWheel(new Vector3(0.80f, 0.10f, 1.48f), steering: false, traction: true);

        BuildVisuals();

        // Collision bookkeeping runs on a trigger Area3D, not RigidBody.BodyEntered:
        // the rigid contact monitor never reports kinematic (AnimatableBody3D)
        // hazards — the pedestrian physically plows the car, yet no signal fires.
        // Areas overlap-detect every body type: walls, cones, walkers, patrol cars.
        // (The area also sees the car's own body — the `body == this` guard in
        // OnBodyEntered filters that.)
        var hitbox = new Area3D { Name = "Hitbox" };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(BodyWid - 0.02f, 1.05f, BodyLen - 0.02f) },
            Position = new Vector3(0, 0.55f, 0),
        });
        hitbox.BodyEntered += OnBodyEntered;
        AddChild(hitbox);
    }

    /// <summary>Swap vehicle kind: resizes the collision box and rebuilds the
    /// procedural shell (SUV is longer, wider, taller, with roof rails).</summary>
    public void SetVehicle(VehicleKind kind)
    {
        Vehicle = kind;
        BodyLen = kind == VehicleKind.Suv ? 4.9f : SedanLen;
        BodyWid = kind == VehicleKind.Suv ? 1.95f : SedanWid;
        _chassisShape.Size = new Vector3(BodyWid, 1.05f, BodyLen);
        if (_shell != null && IsInstanceValid(_shell))
        {
            RemoveChild(_shell);
            _shell.QueueFree();
        }
        BuildVisuals();
    }

    private VehicleWheel3D MakeWheel(Vector3 pos, bool steering, bool traction = false)
    {
        var w = new VehicleWheel3D
        {
            Position = pos,
            WheelRadius = 0.34f,
            WheelRestLength = 0.35f,
            SuspensionTravel = 0.20f,
            SuspensionStiffness = 55f,
            DampingCompression = 6.0f,
            DampingRelaxation = 7.5f,
            SuspensionMaxForce = 60000f,
            WheelFrictionSlip = 11f,
            WheelRollInfluence = 0.08f,
            UseAsSteering = steering,
            UseAsTraction = traction,
        };
        AddChild(w);
        return w;
    }

    private void BuildVisuals()
    {
        bool suv = Vehicle == VehicleKind.Suv;
        _shell = new Node3D { Name = "Shell" };
        AddChild(_shell);

        var red = new StandardMaterial3D
        {
            AlbedoColor = _skin,
            Roughness = 0.32f,
            Metallic = 0.12f, // paint with a hint of clearcoat for the sun to read
        };
        _paintMat = red;
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.15f, 0.17f), Roughness = 0.5f };
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.13f, 0.16f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f,
            Metallic = 0.9f,
        };
        var tire = new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.09f, 0.10f), Roughness = 0.95f };
        var hub = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.63f, 0.66f), Roughness = 0.4f, Metallic = 0.8f };
        var headlight = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.97f, 0.85f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.95f, 0.8f),
            EmissionEnergyMultiplier = 1.6f,
        };
        var taillight = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.1f, 0.1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.12f, 0.1f),
            EmissionEnergyMultiplier = 1.4f,
        };
        _tailMat = taillight;

        // ---- body: boxier and taller when the SUV kind is selected ----
        float chassisH = suv ? 0.68f : 0.55f;
        float chassisY = suv ? 0.56f : 0.52f;
        _shell.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BodyWid, chassisH, BodyLen) },
            MaterialOverride = red,
            Position = new Vector3(0, chassisY, 0),
        });
        _shell.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BodyWid - 0.14f, suv ? 0.40f : 0.30f, suv ? 2.35f : 2.16f) },
            MaterialOverride = glass,
            Position = new Vector3(0, suv ? 1.06f : 0.95f, 0.25f),
        });
        _shell.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BodyWid - 0.2f, 0.20f, suv ? 2.2f : 2.0f) },
            MaterialOverride = red,
            Position = new Vector3(0, suv ? 1.36f : 1.20f, 0.22f),
        });
        if (suv)
        {
            // roof rails — the giveaway SUV silhouette from top-down
            foreach (var x in new[] { -(BodyWid / 2f - 0.22f), BodyWid / 2f - 0.22f })
            {
                _shell.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.06f, 2.0f) },
                    MaterialOverride = dark,
                    Position = new Vector3(x, 1.49f, 0.22f),
                });
            }
        }

        // bumpers + lights (front = -Z)
        foreach (var z in new[] { -BodyLen / 2f - 0.03f, BodyLen / 2f + 0.03f })
        {
            _shell.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(BodyWid + 0.06f, suv ? 0.36f : 0.30f, 0.12f) },
                MaterialOverride = dark,
                Position = new Vector3(0, suv ? 0.42f : 0.38f, z),
            });
        }
        foreach (var x in new[] { -0.55f, 0.55f })
        {
            float lx = suv ? x * 1.18f : x;
            float ly = suv ? 0.60f : 0.55f;
            _shell.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.30f, 0.12f, 0.06f) },
                MaterialOverride = headlight,
                Position = new Vector3(lx, ly, -BodyLen / 2f - 0.02f),
            });
            _shell.AddChild(new MeshInstance3D
            {
                // chunky enough to read as glowing brake lights from top-down
                Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.18f, 0.10f) },
                MaterialOverride = taillight,
                Position = new Vector3(lx, ly, BodyLen / 2f + 0.04f),
            });
        }

        foreach (var (x, z) in new[] { (-0.80f, -1.48f), (0.80f, -1.48f), (-0.80f, 1.48f), (0.80f, 1.48f) })
        {
            var pivot = new Node3D { Position = new Vector3(x, 0.10f, z) };
            var mesh = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.34f, Height = 0.26f },
                MaterialOverride = tire,
                Rotation = new Vector3(0, 0, Mathf.Pi / 2f), // cylinder axis → X
            };
            pivot.AddChild(mesh);
            // hubcap flush with the wheel's outer face
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.05f },
                MaterialOverride = hub,
                Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
                Position = new Vector3(Mathf.Sign(x) * 0.13f, 0, 0),
            });
            _shell.AddChild(pivot);
            if (z < 0)
            {
                if (x < 0) _flPivot = pivot; else _frPivot = pivot;
            }
        }
    }

    public void SelectGear(Gear g) => CurrentGear = g;

    /// <summary>Garage repaint: body + roof share the paint material.</summary>
    public void ApplySkin(Color paint)
    {
        _skin = paint;
        _paintMat.AlbedoColor = paint;
    }

    /// <summary>Brake lights: taillights glow much brighter while braking or on the handbrake.</summary>
    public void SetBrakeLights(bool on) => _tailMat.EmissionEnergyMultiplier = on ? 4.5f : 1.6f;

    /// <summary>Weather grip: scales every wheel's friction slip (snow ≈ 0.55, rain ≈ 0.78).</summary>
    public void SetGrip(float scale)
    {
        foreach (var w in new[] { _fl, _fr, _rl, _rr })
            w.WheelFrictionSlip = 11f * scale;
    }

    public void ResetTo(Vector3 pos, float yawDeg)
    {
        CurrentGear = Gear.N;
        Throttle = BrakeInput = SteerInput = 0;
        Handbrake = false;
        _steer = 0;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Steering = 0;
        EngineForce = 0;
        Brake = 1;
        GlobalPosition = pos;
        Rotation = new Vector3(0, Mathf.DegToRad(yawDeg), 0);
        CollisionCount = 0;
        _recentHits.Clear();
        Sleeping = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float speed = ForwardSpeed;

        // ---- engine by gear (realistic curve: strong at low speed, capped) ----
        // Sign note (verified empirically on 4.7.1): body-level EngineForce is
        // POSITIVE-toward-+Z — the opposite of the Bullet raycast-vehicle docs.
        // Forward (-Z) drive therefore needs NEGATIVE force values here.
        float engine = 0f;
        switch (CurrentGear)
        {
            case Gear.D:
                if (Throttle > 0.01f)
                {
                    if (speed < TopSpeedD)
                    {
                        float fade = Mathf.Clamp(1f - speed / TopSpeedD, 0f, 1f);
                        engine = -MaxEngineForce * Throttle * (0.4f + 0.6f * fade);
                    }
                }
                else if (speed > 0.3f)
                {
                    engine = EngineBrakeForce; // coasting forward → drag toward +Z
                }
                break;
            case Gear.R:
                if (Throttle > 0.01f)
                {
                    if (speed > -TopSpeedR)
                    {
                        float fade = Mathf.Clamp(1f + speed / TopSpeedR, 0f, 1f);
                        engine = MaxReverseForce * Throttle * (0.4f + 0.6f * fade);
                    }
                }
                else if (speed < -0.3f)
                {
                    engine = -EngineBrakeForce; // coasting backward → drag toward -Z
                }
                break;
            case Gear.N:
                break; // freewheel — no drive, no engine brake
        }
        EngineForce = engine;

        // ---- brakes (all wheels; handbrake clamps to a strong stop) ----
        float brake = BrakeInput * MaxBrake;
        if (Handbrake)
            brake = Mathf.Max(brake, MaxBrake * 0.9f);
        // holding the brake at a standstill pins the car (no creep)
        if (BrakeInput > 0.5f && Mathf.Abs(speed) < 0.1f && Throttle < 0.01f)
        {
            LinearDamp = 8f;
            brake = MaxBrake;
        }
        else
        {
            LinearDamp = 0f;
        }
        Brake = brake;

        // ---- steering: speed-sensitive limit + rate-limited actuator ----
        float maxSteer = Mathf.DegToRad(MaxSteerDeg) *
                         Mathf.Clamp(1f - Mathf.Abs(speed) / SteerFadeSpeed, 0.3f, 1f);
        float target = Mathf.Clamp(SteerInput, -1f, 1f) * maxSteer;
        _steer = Mathf.Lerp(_steer, target, Mathf.Clamp(SteerLerpRate * dt, 0f, 1f));
        Steering = _steer;

        if (_flPivot != null) _flPivot.Rotation = new Vector3(0, _steer, 0);
        if (_frPivot != null) _frPivot.Rotation = new Vector3(0, _steer, 0);

        // ---- collision bookkeeping (walls / parked cars / cones — not the floor) ----
        CollisionThisTick = false;
        _collideCooldown -= dt;
        if (_collideCooldown <= 0f)
        {
            _recentHits.Clear();
        }
    }

    private void OnBodyEntered(Node body)
    {
        if (body.Name == "Floor" || body == this)
            return;
        // a fresh burst within the cooldown window only counts once
        if (_recentHits.Add(body))
        {
            CollisionCount++;
            CollisionThisTick = true;
            _collideCooldown = 0.5f;
        }
    }

    /// <summary>Four chassis corners in world space, as a proper cyclic ring
    /// (used both by the corner-in-slot check and by the polygon-area clip).</summary>
    public Vector3[] Corners()
    {
        var b = GlobalTransform.Basis;
        var o = GlobalPosition;
        Vector3 hw = b.X * (BodyWid / 2f);
        Vector3 hf = b.Z * (BodyLen / 2f);
        return new[]
        {
            o + hw + hf, o + hw - hf, o - hw - hf, o - hw + hf,
        };
    }
}
