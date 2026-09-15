using System.IO;
using Godot;

namespace ParkingGame;

/// <summary>Root of the match: environment, follow camera, input mapping,
/// level flow, slot-check success detection, and the --demo autopilot that
/// parks the car headlessly for verification (input injection is not
/// available in this environment, so the script drives the same control API).</summary>
public partial class Game : Node3D
{
    private Car _car = null!;
    private Level _level = null!;
    private Camera3D _cam = null!;
    private HUD _hud = null!;

    private int _levelIndex;
    private bool _started;
    private bool _success;
    private float _timer;
    private float _stillTime;
    private float _endFreeze;

    // ---- demo mode ----
    private bool _demo;
    private float _demoT;
    private float _dumpTimer;
    private float _quitTimer = -1f;
    private string _demoLogPath = "";

    public override void _Ready()
    {
        BuildEnvironment();
        BuildCamera();
        _hud = new HUD { Name = "HUD" };
        AddChild(_hud);
        _car = new Car { Name = "PlayerCar" };
        AddChild(_car);

        var args = OS.GetCmdlineUserArgs(); // args after "--" — GetCmdlineArgs() only holds engine flags
        foreach (var a in args)
        {
            if (a == "--demo") _demo = true;
            if (a.StartsWith("--level") && a.Contains('='))
            {
                if (int.TryParse(a.Split('=')[1], out int lv))
                    _levelIndex = Mathf.Clamp(lv - 1, 0, LevelDef.All.Length - 1);
            }
        }

        LoadLevel(_levelIndex);
        _hud.ShowStart(!_demo);
        _started = _demo;
        if (_demo)
        {
            _demoLogPath = ProjectSettings.GlobalizePath("res://demo_state.txt");
            File.WriteAllText(_demoLogPath, "demo start level=" + (_levelIndex + 1) + "\n");
        }
    }

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.32f, 0.48f, 0.72f),
            SkyHorizonColor = new Color(0.78f, 0.82f, 0.88f),
            GroundBottomColor = new Color(0.28f, 0.27f, 0.24f),
        };
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.65f,
            TonemapMode = Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.35f,
            DirectionalShadowMaxDistance = 70f,
        };
        sun.RotationDegrees = new Vector3(-62f, 35f, 0f);
        AddChild(sun);
    }

    private void BuildCamera()
    {
        _cam = new Camera3D { Fov = 42f, Near = 0.1f, Far = 300f };
        AddChild(_cam);
        _cam.MakeCurrent();
    }

    private void LoadLevel(int index)
    {
        _levelIndex = index;
        var def = LevelDef.All[index];

        if (_level != null)
        {
            RemoveChild(_level);
            _level.QueueFree();
        }
        _level = Level.Build(def);
        AddChild(_level);

        _car.ResetTo(def.Spawn, def.SpawnYawDeg);
        _hud.SetPrompt(def.Title, def.Hint);
        _hud.SetGear(Car.Gear.N);
        _hud.ShowEnd(false);

        _timer = 0f;
        _stillTime = 0f;
        _success = false;
        _endFreeze = 0f;

        // snap the camera over the spawn point
        var p = _car.GlobalPosition;
        _cam.GlobalPosition = p + new Vector3(0, def.CamH, def.CamBack);
        _cam.LookAt(p, Vector3.Up);
    }

    // ═══════════════ input ═══════════════

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey k || !k.Pressed || k.Echo)
            return;

        if (k.Keycode == Key.P)
        {
            SaveScreenshot("manual_" + Time.GetTicksMsec() % 100000 + ".png");
            return;
        }

        if (!_started)
        {
            if (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter)
            {
                _hud.ShowStart(false);
                _started = true;
                LoadLevel(0);
            }
            for (int i = 0; i < LevelDef.All.Length; i++)
            {
                if (k.Keycode == (Key)((int)Key.Key1 + i))
                {
                    _hud.ShowStart(false);
                    _started = true;
                    LoadLevel(i);
                    return;
                }
            }
            return;
        }

        if (_success)
        {
            if (k.Keycode == Key.Enter)
                LoadLevel((_levelIndex + 1) % LevelDef.All.Length);
            else if (k.Keycode == Key.Backspace)
                LoadLevel(_levelIndex);
            return;
        }

        switch (k.Keycode)
        {
            case Key.R: _car.SelectGear(Car.Gear.R); _hud.SetGear(Car.Gear.R); break;
            case Key.N: _car.SelectGear(Car.Gear.N); _hud.SetGear(Car.Gear.N); break;
            case Key.D: _car.SelectGear(Car.Gear.D); _hud.SetGear(Car.Gear.D); break;
            case Key.Enter: LoadLevel(_levelIndex); break; // restart
            case Key.Pageup: LoadLevel((_levelIndex + LevelDef.All.Length - 1) % LevelDef.All.Length); break;
            case Key.Pagedown: LoadLevel((_levelIndex + 1) % LevelDef.All.Length); break;
        }
    }

    private void ReadKeyboardControls()
    {
        float throttle = (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W)) ? 1f : 0f;
        float brake = (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S)) ? 1f : 0f;
        float steer = 0f;
        if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A)) steer += 1f;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D)) steer -= 1f;
        _car.Throttle = throttle;
        _car.BrakeInput = brake;
        _car.SteerInput = steer;
        _car.Handbrake = Input.IsKeyPressed(Key.Space);
    }

    // ═══════════════ demo autopilot (closed-loop pure pursuit on the rear axle,
    // tuned against demo_state.txt — open-loop timing proved too brittle) ═══════════════

    // Path in world XZ for the REAR AXLE of the car (the point that traces the
    // parking arc). The net north offset of a side slot forces the S-arc to
    // arrive with ~15-25° of residual heading error — the shuffle phase
    // below rotates it out, exactly like a human driver's 回一把方向.
    private static readonly Vector2[] DemoPath =
    {
        new(12.4f, 2.20f), new(11.5f, 2.03f), new(10.6f, 1.76f),
        new(9.75f, 1.38f), new(8.98f, 0.90f), new(8.30f, 0.33f),
        new(7.72f, -0.31f), new(7.24f, -1.00f), new(6.86f, -1.70f),
        new(6.50f, -2.35f), new(5.95f, -2.50f),
    };
    private const int DemoWpCap = 10;
    // dock targets: slot center line (L1: center (6.5,-2.6), yaw -90)
    private const float DockX = 6.5f, DockZ = -2.6f, DockYawDeg = -90f;
    private int _wpIndex = 1;
    private int _demoPhase; // 0 swing, 1 reverse-dock, 2 forward-straighten, 3 final back
    private float _fwdStartX;

    private void DemoTick(float dt)
    {
        _demoT += dt;

        var b = _car.GlobalTransform.Basis;
        var rear = _car.GlobalPosition + b.Z * 1.48f; // local +Z = rear axle
        Vector2 rearPos = new(rear.X, rear.Z);
        Vector2 rearDir = new(b.Z.X, b.Z.Z);          // travel direction while reversing
        if (rearDir.LengthSquared() < 1e-6f) rearDir = new Vector2(0f, 1f);
        rearDir = rearDir.Normalized();

        float steer, vt, throttle, brake;
        var pos = _car.GlobalPosition;
        float lz = pos.Z - DockZ;                     // south of the line = +
        float h = WrapAngle(_car.Rotation.Y - Mathf.DegToRad(DockYawDeg));

        if (_demoPhase == 0)
        {
            // ---- phase 1: pure-pursuit the swing arc (advance by proximity only) ----
            Vector2 target = DemoPath[Mathf.Min(_wpIndex, DemoWpCap)];
            Vector2 toT = target - rearPos;
            if (_wpIndex < DemoWpCap && toT.Length() < 0.40f)
            {
                _wpIndex++;
                target = DemoPath[Mathf.Min(_wpIndex, DemoWpCap)];
                toT = target - rearPos;
            }

            float desired = Mathf.Atan2(toT.Y, toT.X);
            float heading = Mathf.Atan2(rearDir.Y, rearDir.X);
            float err = WrapAngle(desired - heading);
            steer = Mathf.Clamp(err * 1.3f, -1f, 1f);
            float distLast = (DemoPath[DemoWpCap] - rearPos).Length();
            vt = distLast > 2.5f ? 1.6f : Mathf.Clamp(0.5f * distLast, 0.45f, 1.1f);

            if (_wpIndex >= DemoWpCap || (rearPos - DemoPath[DemoWpCap]).Length() < 0.5f)
                _demoPhase = 1; // hand over to the reverse dock
        }
        else
        {
            // reverse straight-line control: steer RIGHT (-) raises yaw toward
            // -90; steer LEFT (+) moves the rear north. (trace-verified)
            steer = Mathf.Clamp(2.5f * h + 1.1f * lz, -1f, 1f);
            vt = Mathf.Clamp(0.45f + 0.55f * Mathf.Abs(h), 0.45f, 1.1f);

            bool centered = pos.X < DockX + 0.4f && pos.X > DockX - 0.6f &&
                            Mathf.Abs(lz) < 0.30f && Mathf.Abs(h) < 0.22f;
            bool tooDeep = pos.X < DockX - 0.9f;

            if (_demoPhase == 1)
            {
                if (centered) _demoPhase = 4;
                else if (tooDeep || (pos.X < DockX + 0.25f &&
                                     (Mathf.Abs(h) > 0.14f || Mathf.Abs(lz) > 0.24f)))
                {
                    _demoPhase = 2;               // shuffle: pull forward to straighten
                    _fwdStartX = pos.X;
                }
            }
            else if (_demoPhase == 2)
            {
                // forward straighten: in D, steering LEFT raises yaw (sign flips
                // vs reverse); creep until the body is on line or 1.1 m used up
                float back = -vt;                  // switch to forward below
                _car.SelectGear(Car.Gear.D);
                steer = Mathf.Clamp(-1.6f * h + 0.6f * lz, -1f, 1f);
                vt = 0.5f;
                if (Mathf.Abs(h) < 0.10f || pos.X - _fwdStartX > 1.1f || pos.X > DockX + 1.1f)
                    _demoPhase = 3;
            }
            else if (_demoPhase == 3)
            {
                if (centered || pos.X < DockX - 0.8f) _demoPhase = 4;
            }
        }

        float v = -_car.ForwardSpeed; // reverse speed, positive
        if (_demoPhase == 2) v = _car.ForwardSpeed; // forward phase: positive forward
        throttle = Mathf.Clamp((vt - Mathf.Abs(v)) * 1.2f, 0f, 0.85f);
        brake = Mathf.Abs(v) > vt + 0.3f || vt <= 0.01f
            ? Mathf.Clamp((Mathf.Abs(v) - vt) * 0.8f, 0f, 1f) : 0f;

        if (_demoPhase != 2) _car.SelectGear(Car.Gear.R);
        _car.SteerInput = steer;
        _car.Throttle = throttle;
        _car.BrakeInput = brake;
        _car.Handbrake = false;
        _hud.SetGear(_demoPhase == 2 ? Car.Gear.D : Car.Gear.R);

        _dumpTimer -= dt;
        if (_dumpTimer <= 0f)
        {
            _dumpTimer = 0.2f;
            float yaw = _car.Rotation.Y * 180f / Mathf.Pi;
            string line = $"t={_demoT:0.00} gear={_car.CurrentGear} spd={_car.ForwardSpeed:0.00} " +
                          $"pos=({pos.X:0.00},{pos.Z:0.00}) yaw={yaw:0.0} steer={Mathf.RadToDeg(_car.Steering):0.0} " +
                          $"wp={_wpIndex} ph={_demoPhase} " +
                          $"inSlot={SlotGeometry().inCount} angleOK={SlotGeometry().angleOk}";
            File.AppendAllText(_demoLogPath, line + "\n");
        }

        if (_demoT > 30f && !_success)
        {
            File.AppendAllText(_demoLogPath, "RESULT FAILED timeout\n");
            SaveScreenshot("demo_failed.png");
            GetTree().Quit(1);
        }
    }

    private static float WrapAngle(float a)
    {
        while (a > Mathf.Pi) a -= Mathf.Tau;
        while (a < -Mathf.Pi) a += Mathf.Tau;
        return a;
    }

    // ═══════════════ success detection ═══════════════

    private (int inCount, bool angleOk) SlotGeometry()
    {
        var def = _level.Def;
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(def.SlotYawDeg));
        Vector3 slotX = basis.X;  // long axis
        Vector3 slotZ = basis.Z;  // width axis

        int inCount = 0;
        foreach (var c in _car.Corners())
        {
            Vector3 d = c - def.SlotCenter;
            float lx = d.Dot(slotX);
            float lz = d.Dot(slotZ);
            if (Mathf.Abs(lx) <= def.SlotLen / 2f - 0.04f && Mathf.Abs(lz) <= def.SlotWid / 2f - 0.04f)
                inCount++;
        }

        Vector3 fwd = -_car.GlobalTransform.Basis.Z;
        bool angleOk = Mathf.Abs(fwd.Dot(slotX)) >= Mathf.Cos(Mathf.DegToRad(def.AngleTolDeg));
        return (inCount, angleOk);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!_started) return;

        if (_demo && !_success)
            DemoTick(dt);
        else if (!_success)
            ReadKeyboardControls();

        if (_success)
        {
            // pin the car while the end screen shows
            _car.Throttle = 0;
            _car.BrakeInput = 1;
            _car.SelectGear(Car.Gear.N);
            _endFreeze += dt;
            if (_demo && _quitTimer < 0f && _endFreeze > 1.2f)
            {
                _quitTimer = 2.0f; // screenshot already taken at the moment of success
            }
            if (_demo && _quitTimer >= 0f)
            {
                _quitTimer -= dt;
                if (_quitTimer <= 0f)
                    GetTree().Quit(0);
            }
            return;
        }

        _timer += dt;
        var (inCount, angleOk) = SlotGeometry();
        bool still = _car.LinearVelocity.Length() < 0.12f;
        if (inCount == 4 && angleOk && still)
        {
            _stillTime += dt;
            if (_stillTime >= 1.0f)
            {
                _success = true;
                _hud.SetEndStats(_timer, _car.CollisionCount);
                _hud.ShowEnd(true);
                if (_demo)
                {
                    File.AppendAllText(_demoLogPath,
                        $"RESULT SUCCESS time={_timer:0.00} collisions={_car.CollisionCount}\n");
                    SaveScreenshot("demo_success.png");
                }
            }
        }
        else
        {
            _stillTime = 0f;
        }

        _hud.SetTimer(_timer, _car.CollisionCount);
    }

    public override void _Process(double delta)
    {
        // top-down follow camera (fixed north-up)
        var def = _level.Def;
        var p = _car.GlobalPosition;
        var desired = p + new Vector3(0, def.CamH, def.CamBack);
        float k = Mathf.Clamp((float)delta * 5f, 0f, 1f);
        _cam.GlobalPosition = _cam.GlobalPosition.Lerp(desired, k);
        _cam.LookAt(p + new Vector3(0, 0.2f, 0), Vector3.Up);

        float kmh = Mathf.Abs(_car.ForwardSpeed) * 3.6f;
        _hud.SetSpeed(kmh, Mathf.RadToDeg(_car.Steering));
    }

    private void SaveScreenshot(string name)
    {
        string dir = ProjectSettings.GlobalizePath("res://screenshots");
        Directory.CreateDirectory(dir);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(Path.Combine(dir, name));
        GD.Print("screenshot saved: " + name);
    }
}
