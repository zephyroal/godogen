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

    // ---- control inputs (0..1 / -1..1), written externally each tick ----
    public float Throttle;    // 0..1
    public float BrakeInput;  // 0..1
    public float SteerInput;  // -1..1, +1 = wheels left (Godot steering convention)
    public bool Handbrake;

    public const float BodyLen = 4.6f;
    public const float BodyWid = 1.8f;

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

    public int CollisionCount { get; private set; }
    public bool CollisionThisTick { get; private set; }

    public override void _Ready()
    {
        Mass = 1200f;
        CanSleep = false;

        // chassis collision
        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(BodyWid, 1.05f, BodyLen) },
            Position = new Vector3(0, 0.55f, 0),
        };
        AddChild(shape);

        // ---- wheels: front steer, rear drive (classic RWD parking feel) ----
        _fl = MakeWheel(new Vector3(-0.80f, 0.10f, -1.48f), steering: true);
        _fr = MakeWheel(new Vector3(0.80f, 0.10f, -1.48f), steering: true);
        _rl = MakeWheel(new Vector3(-0.80f, 0.10f, 1.48f), steering: false, traction: true);
        _rr = MakeWheel(new Vector3(0.80f, 0.10f, 1.48f), steering: false, traction: true);

        BuildVisuals();

        BodyEntered += OnBodyEntered;
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
        var red = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.27f, 0.24f) };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.15f, 0.17f) };
        var tire = new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.09f, 0.10f) };

        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BodyWid, 0.55f, BodyLen) },
            MaterialOverlay = red,
            Position = new Vector3(0, 0.52f, 0),
        };
        AddChild(body);
        var cabin = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BodyWid - 0.2f, 0.48f, 2.1f) },
            MaterialOverride = dark,
            Position = new Vector3(0, 1.02f, 0.25f),
        };
        AddChild(cabin);

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
            AddChild(pivot);
            if (z < 0)
            {
                if (x < 0) _flPivot = pivot; else _frPivot = pivot;
            }
        }
    }

    public void SelectGear(Gear g) => CurrentGear = g;

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

    /// <summary>Four chassis corners in world space, used by the slot check.</summary>
    public Vector3[] Corners()
    {
        var b = GlobalTransform.Basis;
        var o = GlobalPosition;
        Vector3 hw = b.X * (BodyWid / 2f);
        Vector3 hf = b.Z * (BodyLen / 2f);
        return new[]
        {
            o + hw + hf, o + hw - hf, o - hw + hf, o - hw - hf,
        };
    }
}
