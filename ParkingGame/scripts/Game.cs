using System.Collections.Generic;
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
    private Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private ProceduralSkyMaterial _skyMat = null!;
    private WeatherFx _weather = null!;
    private Effects _fx = null!;
    private SkidMarks _skids = null!;
    private GarageUI _garage = null!;
    private Editor _editor = null!;
    private LevelDef _customDef = null!;
    private float _shake;
    private float _explodeCd;
    private AudioStreamPlayer _jingle = null!;
    private AudioStreamWav _fanfareNormal = null!;
    private AudioStreamWav _fanfareSuv = null!;

    /// <summary>Which success jingle the last verdict played ("normal"/"suv")
    /// — asserted by --verify-features.</summary>
    public string LastFanfare { get; private set; } = "";

    private int _levelIndex;
    private bool _started;
    private bool _success;
    private float _timer;
    private float _endFreeze;
    private float _reportCooldown;   // fail-toast spam guard, also paces the demo retry
    private float _settleTime;        // demo: seconds the car has stood still in phase 4

    // ---- demo mode ----
    private bool _demo;
    private float _demoT;
    private float _dumpTimer;
    private float _quitTimer = -1f;
    private string _demoLogPath = "";

    // ---- --verify-report: teleports the car into a bad pose and a perfect
    // pose and submits both through ReportParked, proving the verdict UI ----
    private bool _reportTest;
    private int _rtStage;
    private float _rtTimer;
    private bool _rtPass1, _rtPass2;
    private string _reportLogPath = "";
    private string _pendingShot = "";   // screenshot name to take next frame

    // ---- --verify-hazards: walks all six levels, asserting spawn safety and
    // that hazards actually move; L2 additionally parks the car on the
    // pedestrian lane so the walker must collide and be counted ----
    private bool _hazardTest;
    private int _hvLevel;
    private int _hvStage;
    private float _hvTimer;
    private bool _hvPass = true;
    private Vector3[] _hvPosA = System.Array.Empty<Vector3>();
    private string _hazardLogPath = "";

    // ---- --verify-features: weather shots + explosion/skid/garage/editor
    // assertions for the enhancement batch ----
    private bool _featuresTest;
    private int _ftStage;
    private float _ftTimer;
    private bool _ftPass = true;
    private int _ftCoins0;
    private string _ftCustomBackup = null!;
    private string _ftSaveBackup = null!;
    private string _featuresLogPath = "";
    private bool _editorArg;

    public override void _Ready()
    {
        BuildEnvironment();
        BuildCamera();
        _hud = new HUD { Name = "HUD" };
        _hud.ReportRequested += () => ReportParked();
        AddChild(_hud);
        _car = new Car { Name = "PlayerCar" };
        AddChild(_car);

        // weather / effects / skids / garage subsystems
        _weather = new WeatherFx();
        _weather.Bind(_env, _sun, _skyMat, _car);
        AddChild(_weather);
        _fx = new Effects();
        AddChild(_fx);
        _skids = new SkidMarks();
        AddChild(_skids);
        _garage = new GarageUI(_car);
        AddChild(_garage);
        _car.ApplySkin(Garage.SelectedPaint);

        // success fanfares (procedural wavs from tools/make_audio.py)
        _fanfareNormal = LoadWav("res://assets/audio/success.wav");
        _fanfareSuv = LoadWav("res://assets/audio/success_suv.wav");
        _jingle = new AudioStreamPlayer();
        AddChild(_jingle);

        var customDto = Editor.LoadCustomDto();
        _customDef = customDto != null ? Editor.DtoToDef(customDto) : null;

        var args = OS.GetCmdlineUserArgs(); // args after "--" — GetCmdlineArgs() only holds engine flags
        foreach (var a in args)
        {
            if (a == "--demo") _demo = true;
            if (a == "--verify-report") _reportTest = true;
            if (a == "--verify-hazards") _hazardTest = true;
            if (a == "--verify-features") _featuresTest = true;
            if (a == "--editor") _editorArg = true;
            if (a.StartsWith("--level") && a.Contains('='))
            {
                if (int.TryParse(a.Split('=')[1], out int lv))
                    _levelIndex = Mathf.Clamp(lv - 1, 0, LevelDef.All.Length - 1);
            }
        }

        LoadLevel(_levelIndex);

        // scripted runs (demo/verify) always drive the sedan — the autopilot's
        // kinematics are tuned for the 4.6 × 1.8 body; normal play uses the
        // saved vehicle
        bool scripted = _demo || _reportTest || _hazardTest || _featuresTest;
        if (!scripted)
            _car.SetVehicle(Garage.SelectedVehicleId == "suv"
                ? Car.VehicleKind.Suv : Car.VehicleKind.Sedan);

        _hud.ShowStart(!scripted);
        _started = _demo || _reportTest || _hazardTest || _featuresTest;
        if (_demo)
        {
            _demoLogPath = ProjectSettings.GlobalizePath("res://demo_state.txt");
            File.WriteAllText(_demoLogPath, "demo start level=" + (_levelIndex + 1) + "\n");
        }
        if (_reportTest)
        {
            _reportLogPath = ProjectSettings.GlobalizePath("res://verify_report.txt");
            File.WriteAllText(_reportLogPath, "report test level=" + (_levelIndex + 1) + "\n");
        }
        if (_hazardTest)
        {
            _hazardLogPath = ProjectSettings.GlobalizePath("res://verify_hazards.txt");
            File.WriteAllText(_hazardLogPath, "hazard test\n");
        }
        if (_featuresTest)
        {
            _featuresLogPath = ProjectSettings.GlobalizePath("res://verify_features.txt");
            File.WriteAllText(_featuresLogPath, "features test\n");
        }
        if (_editorArg) OpenEditor();
    }

    private void BuildEnvironment()
    {
        _skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.32f, 0.48f, 0.72f),
            SkyHorizonColor = new Color(0.78f, 0.82f, 0.88f),
            GroundBottomColor = new Color(0.28f, 0.27f, 0.24f),
        };
        _env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMat },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.65f,
            TonemapMode = Environment.ToneMapper.Filmic,
            // depth cues: SSAO grounds the cars and walls, Glow picks up the
            // headlights and the emissive slot markings
            SsaoEnabled = true,
            GlowEnabled = true,
            GlowIntensity = 0.65f,
            GlowHdrThreshold = 0.95f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.35f,
            LightColor = new Color(1f, 0.95f, 0.86f), // warm afternoon sun
            DirectionalShadowMaxDistance = 70f,
            ShadowBlur = 1.5f,
        };
        _sun.RotationDegrees = new Vector3(-62f, 35f, 0f);
        AddChild(_sun);

        // edge smoothing for the procedural boxes (cars, walls, cones):
        // MSAA 8x for geometry + FXAA to soften the high-contrast painted lines
        GetViewport().Msaa3D = Viewport.Msaa.Msaa8X;
        GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
    }

    private void BuildCamera()
    {
        _cam = new Camera3D { Fov = 42f, Near = 0.1f, Far = 300f };
        AddChild(_cam);
        _cam.MakeCurrent();
    }

    private static AudioStreamWav LoadWav(string resPath) =>
        AudioStreamWav.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(resPath));

    private void LoadLevel(int index)
    {
        if (index >= LevelDef.All.Length)
        {
            if (_customDef == null) index = 0;
            else
            {
                _levelIndex = index;
                LoadLevelDef(_customDef);
                return;
            }
        }
        _levelIndex = Mathf.Clamp(index, 0, LevelDef.All.Length - 1);
        LoadLevelDef(LevelDef.All[_levelIndex]);
    }

    private void LoadLevelDef(LevelDef def)
    {
        if (_level != null)
        {
            RemoveChild(_level);
            _level.QueueFree();
        }
        _level = Level.Build(def);
        AddChild(_level);

        _car.ResetTo(def.Spawn, def.SpawnYawDeg);
        _weather.Apply(def.Weather);
        _hud.SetPrompt(def.Title, def.Hint);
        _hud.SetGear(Car.Gear.N);
        _hud.ShowEnd(false);

        _timer = 0f;
        _success = false;
        _endFreeze = 0f;
        _reportCooldown = 0f;
        _settleTime = 0f;
        _hud.SetReportEnabled(true);

        // snap the camera over the spawn point
        var p = _car.GlobalPosition;
        _cam.GlobalPosition = p + new Vector3(0, def.CamH, def.CamBack);
        _cam.LookAt(p, Vector3.Up);
    }

    // ═══════════════ editor hooks ═══════════════

    /// <summary>Rebuild the level from the editor's draft (resets the car too).</summary>
    public void EditorPreview(LevelDef def) => LoadLevelDef(def);

    public void SetCustomDef(LevelDef def) => _customDef = def;

    public void BeginEditorTest() => _started = true;

    public void EndEditorTest() => _started = false;

    public void CloseEditor()
    {
        if (_editor == null) return;
        _editor.QueueFree();
        _editor = null!;
        LoadLevel(0);
        _hud.ShowStart(true);
    }

    private void OpenEditor()
    {
        _hud.ShowStart(false);
        _editor = new Editor();
        AddChild(_editor);
        _editor.Setup(this);
    }

    private void ToggleGarage()
    {
        if (_garage.IsOpen) _garage.Close();
        else _garage.Open();
    }

    // ═══════════════ input ═══════════════

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey k || !k.Pressed || k.Echo)
            return;

        // the editor owns the keyboard and mouse while its palette is up
        if (_editor != null && _editor.Active)
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
            if (k.Keycode == Key.E)
            {
                OpenEditor();
                return;
            }
            if (k.Keycode == Key.B)
            {
                ToggleGarage();
                return;
            }
            if (k.Keycode == Key.Key0 && _customDef != null)
            {
                _hud.ShowStart(false);
                _started = true;
                LoadLevel(LevelDef.All.Length);
                return;
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
            else if (k.Keycode == Key.B)
                ToggleGarage();
            return;
        }

        switch (k.Keycode)
        {
            case Key.R: _car.SelectGear(Car.Gear.R); _hud.SetGear(Car.Gear.R); break;
            case Key.N: _car.SelectGear(Car.Gear.N); _hud.SetGear(Car.Gear.N); break;
            case Key.D: _car.SelectGear(Car.Gear.D); _hud.SetGear(Car.Gear.D); break;
            case Key.G: ReportParked(); break; // 「报告我停好了」
            case Key.Enter: LoadLevel(_levelIndex); break; // restart
            case Key.Pageup: LoadLevel((_levelIndex + LevelDef.All.Length - 1) % LevelDef.All.Length); break;
            case Key.Pagedown: LoadLevel((_levelIndex + 1) % LevelDef.All.Length); break;
        }
    }

    private void ReadKeyboardControls()
    {
        // arrows only — WASD would collide with the R/N/D gearbox keys (D!)
        float throttle = Input.IsKeyPressed(Key.Up) ? 1f : 0f;
        float brake = Input.IsKeyPressed(Key.Down) ? 1f : 0f;
        float steer = 0f;
        if (Input.IsKeyPressed(Key.Left)) steer += 1f;
        if (Input.IsKeyPressed(Key.Right)) steer -= 1f;
        _car.Throttle = throttle;
        _car.BrakeInput = brake;
        _car.SteerInput = steer;
        _car.Handbrake = Input.IsKeyPressed(Key.Space);
    }

    // ═══════════════ demo autopilot (closed-loop pure pursuit on the rear axle,
    // tuned against demo_state.txt — open-loop timing proved too brittle) ═══════════════

    // Rear-axle path in world XZ, sampled every ~17° of arc from a
    // kinematically exact two-arc S (r = 4.8 m ≈ 31.7° steer, near the 34°
    // lock): arc 1 tucks the tail north while the nose swings out to the road
    // (yaw -90 → -152), arc 2 unwinds to yaw ≈ -102 at the slot line. The
    // last ~10° of heading cannot be unwound inside the slot's longitudinal
    // space by any single S — the phase-2/3 shuffle rotates them out, exactly
    // like a human driver's 回一把方向.
    private static readonly Vector2[] DemoPath =
    {
        new(12.32f, 2.20f),  // rear-axle pose at spawn (car 13.80, yaw -90)
        new(10.91f, 1.99f),
        new(9.65f, 1.39f),
        new(8.59f, 0.39f),
        new(8.08f, -0.35f),  // end of tuck arc (yaw ≈ -152)
        new(7.23f, -1.49f),
        new(6.24f, -2.26f),
        new(4.84f, -2.79f),  // end of unwind arc (yaw ≈ -102)
    };
    // dock targets: slot center line (L1: center (6.5,-2.6), yaw -90)
    private const float DockX = 6.5f, DockZ = -2.6f, DockYawDeg = -90f;
    private int _demoPhase; // 0 swing arc, 1 reverse-dock, 2 forward-straighten, 3 final back
    private float _fwdStartX;

    /// <summary>Carrot point `ahead` m along the polyline from the rear axle's
    /// nearest-point projection, plus the path length left to the end.</summary>
    private (Vector2 carrot, float endDist) PathCarrot(Vector2 p, float ahead)
    {
        int best = 0; float bestT = 0f, bestD = float.MaxValue;
        for (int i = 0; i < DemoPath.Length - 1; i++)
        {
            Vector2 a = DemoPath[i], ab = DemoPath[i + 1] - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            float d = (p - (a + ab * t)).LengthSquared();
            if (d < bestD) { bestD = d; best = i; bestT = t; }
        }
        float endDist = (1f - bestT) * (DemoPath[best + 1] - DemoPath[best]).Length();
        for (int i = best + 1; i < DemoPath.Length - 1; i++)
            endDist += (DemoPath[i + 1] - DemoPath[i]).Length();
        Vector2 cur = DemoPath[best] + (DemoPath[best + 1] - DemoPath[best]) * bestT;
        float remain = ahead;
        int j = best;
        while (remain > 0f && j < DemoPath.Length - 1)
        {
            Vector2 nxt = DemoPath[j + 1];
            float d = (nxt - cur).Length();
            if (d <= remain) { cur = nxt; j++; remain -= d; }
            else { cur += (nxt - cur) / d * remain; remain = 0f; }
        }
        return (cur, endDist);
    }

    private void DemoTick(float dt)
    {
        _demoT += dt;

        if (_demoT > 30f && !_success)
        {
            File.AppendAllText(_demoLogPath, "RESULT FAILED timeout\n");
            SaveScreenshot("demo_failed.png");
            GetTree().Quit(1);
        }

        if (_demoPhase == 4)
        {
            // done — release the controls so the car settles, then submit the
            // same "报告我停好了" report a human player would press
            _car.Throttle = 0f;
            _car.BrakeInput = 1f;
            _car.SteerInput = 0f;
            _car.Handbrake = true;
            _car.SelectGear(Car.Gear.N);
            _hud.SetGear(Car.Gear.N);

            if (_car.LinearVelocity.Length() < 0.12f)
            {
                _settleTime += dt;
                if (_settleTime > 0.8f && _reportCooldown <= 0f)
                {
                    _settleTime = 0f;
                    ReportParked(); // success → quit flow; fail → cooldown, retry
                }
            }
            else _settleTime = 0f;

            _dumpTimer -= dt;
            if (_dumpTimer <= 0f)
            {
                _dumpTimer = 0.2f;
                var p4 = _car.GlobalPosition;
                var geo = SlotGeometry();
                File.AppendAllText(_demoLogPath,
                    $"t={_demoT:0.00} gear=N spd={_car.ForwardSpeed:0.00} pos=({p4.X:0.00},{p4.Z:0.00}) " +
                    $"yaw={_car.Rotation.Y * 180f / Mathf.Pi:0.0} ph=4 inSlot={geo.inCount} angleOK={geo.angleOk}\n");
            }
            return;
        }

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
            // ---- phase 0: pure pursuit on a carrot point at fixed lookahead
            // along the path. The command is the exact bicycle-model law
            // δ = atan(2·L·sin(err)/lookahead): a plain proportional gain
            // understeers a 4.8 m-radius arc by ~6×, the car drifts outside
            // the path and hands the dock a ~40° heading debt. Cruise is
            // capped at 0.9 m/s so speed-fade never steals the ~31.7° steer
            // the arcs need. ----
            float lookDist = Mathf.Clamp(0.75f + 0.5f * Mathf.Abs(_car.ForwardSpeed), 0.9f, 1.35f);
            var (carrot, endDist) = PathCarrot(rearPos, lookDist);
            Vector2 toT = carrot - rearPos;
            float desired = Mathf.Atan2(toT.Y, toT.X);
            float heading = Mathf.Atan2(rearDir.Y, rearDir.X);
            float err = WrapAngle(desired - heading);
            float dRad = Mathf.Atan(2f * 2.96f * Mathf.Sin(err) / Mathf.Max(lookDist, 0.2f));
            steer = Mathf.Clamp(dRad / Mathf.DegToRad(32f), -1f, 1f);
            vt = endDist > 2.2f ? 0.9f : Mathf.Clamp(0.55f * endDist, 0.45f, 0.9f);

            if (endDist < 0.15f)
                _demoPhase = 1; // hand over to the reverse dock
        }
        else
        {
            // reverse straight-line control: steer RIGHT (-) raises yaw toward
            // -90; steer LEFT (+) moves the rear north. (trace-verified)
            steer = Mathf.Clamp(2.5f * h + 1.1f * lz, -1f, 1f);
            vt = Mathf.Clamp(0.45f + 0.55f * Mathf.Abs(h), 0.45f, 1.1f);

            // "centered" = the game's own slot check (all four corners inside +
            // angle within tolerance) — the exact condition the still-detector
            // confirms after the stop; no geometric approximation of it.
            var geo = SlotGeometry();
            bool centered = geo.inCount == 4 && geo.angleOk;
            bool docked = pos.X < DockX + 0.25f;   // far enough west to shuffle

            if (_demoPhase == 1)
            {
                if (centered) _demoPhase = 4;
                else if (docked)
                {
                    _demoPhase = 2;               // shuffle: pull forward to straighten
                    _fwdStartX = pos.X;
                }
            }
            else if (_demoPhase == 2)
            {
                // forward straighten: in D, steering LEFT raises yaw (sign flips
                // vs reverse); creep until the body is on line or 1.0 m used up
                _car.SelectGear(Car.Gear.D);
                steer = Mathf.Clamp(-4.5f * h + 0.8f * lz, -1f, 1f);
                vt = 0.55f;
                if (Mathf.Abs(h) < 0.05f || pos.X - _fwdStartX > 1.0f || pos.X > DockX + 0.95f)
                    _demoPhase = 3;
            }
            else if (_demoPhase == 3)
            {
                if (centered || pos.X < DockX - 0.75f) _demoPhase = 4;
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
                          $"ph={_demoPhase} lz={lz:0.000} h={h:0.000} " +
                          $"inSlot={SlotGeometry().inCount} angleOK={SlotGeometry().angleOk}";
            File.AppendAllText(_demoLogPath, line + "\n");
        }

    }

    private static float WrapAngle(float a)
    {
        while (a > Mathf.Pi) a -= Mathf.Tau;
        while (a < -Mathf.Pi) a += Mathf.Tau;
        return a;
    }

    // ═══════════════ parking report (「报告我停好了」→ verdict) ═══════════════

    private (int inCount, bool angleOk, float areaFrac) SlotGeometry()
    {
        var def = _level.Def;
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(def.SlotYawDeg));
        Vector3 slotX = basis.X;  // long axis
        Vector3 slotZ = basis.Z;  // width axis

        int inCount = 0;
        var poly = new List<Vector2>();
        foreach (var c in _car.Corners())
        {
            Vector3 d = c - def.SlotCenter;
            float lx = d.Dot(slotX);
            float lz = d.Dot(slotZ);
            poly.Add(new Vector2(lx, lz));
            if (Mathf.Abs(lx) <= def.SlotLen / 2f - 0.04f && Mathf.Abs(lz) <= def.SlotWid / 2f - 0.04f)
                inCount++;
        }

        // exact overlap fraction: clip the car polygon against the slot rect
        // (Sutherland–Hodgman — both convex). The SUV verdict is area-based.
        var clipped = ClipPoly(poly, def.SlotLen / 2f - 0.04f, def.SlotWid / 2f - 0.04f);
        float carArea = _car.BodyLen * _car.BodyWid;
        float areaFrac = clipped.Count < 3 ? 0f : Mathf.Abs(PolyArea(clipped)) / carArea;

        Vector3 fwd = -_car.GlobalTransform.Basis.Z;
        bool angleOk = Mathf.Abs(fwd.Dot(slotX)) >= Mathf.Cos(Mathf.DegToRad(def.AngleTolDeg));
        return (inCount, angleOk, areaFrac);
    }

    private static List<Vector2> ClipPoly(List<Vector2> poly, float hx, float hz)
    {
        var cur = new List<Vector2>(poly);
        cur = ClipHalfPlane(cur, 0, hx, true);     // x ≤ hx
        cur = ClipHalfPlane(cur, 0, -hx, false);  // x ≥ -hx
        cur = ClipHalfPlane(cur, 1, hz, true);     // y ≤ hz
        cur = ClipHalfPlane(cur, 1, -hz, false);  // y ≥ -hz
        return cur;
    }

    private static List<Vector2> ClipHalfPlane(List<Vector2> inPoly, int axis, float bound, bool keepLess)
    {
        var result = new List<Vector2>();
        int n = inPoly.Count;
        if (n == 0) return result;
        for (int i = 0; i < n; i++)
        {
            var a = inPoly[i];
            var b = inPoly[(i + 1) % n];
            float av = axis == 0 ? a.X : a.Y;
            float bv = axis == 0 ? b.X : b.Y;
            bool ain = keepLess ? av <= bound : av >= bound;
            bool bin = keepLess ? bv <= bound : bv >= bound;
            if (ain) result.Add(a);
            if (ain != bin)
            {
                float t = (bound - av) / (bv - av);
                result.Add(a + (b - a) * t);
            }
        }
        return result;
    }

    private static float PolyArea(List<Vector2> poly)
    {
        float s = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2f; // signed
    }

    /// <summary>Everything the verdict needs: slot fit, angle, stillness,
    /// centering offsets and, when it fails, human-readable reasons.</summary>
    public record ParkResult(bool Success, int InSlot, float AngleDeg, float AngleTolDeg,
        bool Still, float LatOff, float LongOff, string[] FailReasons);

    private ParkResult EvaluateParking()
    {
        var def = _level.Def;
        var (inCount, angleOk, areaFrac) = SlotGeometry();
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(def.SlotYawDeg));
        var fwd = -_car.GlobalTransform.Basis.Z;
        float angleDeg = Mathf.RadToDeg(Mathf.Acos(
            Mathf.Clamp(Mathf.Abs(fwd.Dot(basis.X)), 0f, 1f)));
        bool still = _car.LinearVelocity.Length() < 0.12f;
        Vector3 d = _car.GlobalPosition - def.SlotCenter;
        float latOff = d.Dot(basis.Z);   // width axis
        float longOff = d.Dot(basis.X);  // long axis

        // fit rule: sedans need all four corners inside; the SUV is the
        // relaxed special vehicle — up to 30% of its area may hang outside
        bool suv = _car.Vehicle == Car.VehicleKind.Suv;
        bool fit = suv ? areaFrac >= 0.70f : inCount == 4;

        var reasons = new List<string>();
        if (!still) reasons.Add("车辆还在移动，停稳后再报告");
        if (!fit)
            reasons.Add(suv
                ? $"SUV 宽容判定允许 30% 面积在线外，当前约 {Mathf.Clamp(100f - areaFrac * 100f, 0f, 100f):0}% 在外"
                : $"车身只有 {inCount}/4 个角在库位内");
        if (!angleOk) reasons.Add($"车身与库位长轴夹角 {angleDeg:0.0}°，超过 {def.AngleTolDeg:0}° 容差");
        return new ParkResult(still && fit && angleOk, inCount, angleDeg,
            def.AngleTolDeg, still, latOff, longOff, reasons.ToArray());
    }

    /// <summary>The "报告我停好了" entry point — HUD button, G key and the demo
    /// autopilot all funnel through here. Success freezes the match and shows
    /// the verdict overlay; failure explains why via a toast and lets the
    /// player keep adjusting. Returns the verdict, or null when guarded out
    /// (not started / already succeeded / on report cooldown).</summary>
    public ParkResult ReportParked()
    {
        if (!_started || _success || _reportCooldown > 0f) return null;
        var r = EvaluateParking();
        if (r.Success)
        {
            _success = true;
            bool suv = _car.Vehicle == Car.VehicleKind.Suv;
            // the verdict jingle: normal arpeggio, or the SUV's brassier fanfare
            _jingle.Stream = suv ? _fanfareSuv : _fanfareNormal;
            LastFanfare = suv ? "suv" : "normal";
            _jingle.Play();
            bool perfect = _car.CollisionCount == 0 && r.AngleDeg <= 5f && Mathf.Abs(r.LatOff) <= 0.15f;
            // scripted runs don't touch the player's wallet
            int reward = (_demo || _reportTest) ? 0
                : perfect ? Garage.PerfectReward : Garage.SuccessReward;
            if (reward > 0) Garage.Award(reward);
            _hud.SetVerdict(r, _timer, _car.CollisionCount, perfect, reward);
            _hud.ShowEnd(true);
            _hud.SetReportEnabled(false);
            if (_demo)
            {
                File.AppendAllText(_demoLogPath,
                    $"RESULT SUCCESS time={_timer:0.00} collisions={_car.CollisionCount} " +
                    $"angle={r.AngleDeg:0.0} lat={r.LatOff:0.00}\n");
                _pendingShot = "demo_success.png"; // next frame, once the verdict overlay has rendered
            }
        }
        else
        {
            _reportCooldown = 1.5f;
            _hud.ShowFailToast(r.FailReasons);
            if (_demo)
                File.AppendAllText(_demoLogPath,
                    "REPORT FAIL " + string.Join("; ", r.FailReasons) + "\n");
        }
        return r;
    }

    /// <summary>Scripted verification of both verdict paths without input
    /// injection: pose the car badly → report (must fail, toast visible),
    /// wait the toast out, pose perfectly → report (must succeed, verdict
    /// overlay up), then quit with a pass/fail exit code.</summary>
    private void RunReportTest(float dt)
    {
        _rtTimer += dt;
        if (_rtStage == 0 && _rtTimer > 0.6f)
        {
            _car.ResetTo(new Vector3(6.5f, 0, -2.2f), -50f); // crooked in the slot mouth
            var r = ReportParked();
            _rtPass1 = r is { Success: false } && r.FailReasons.Length > 0;
            File.AppendAllText(_reportLogPath,
                $"REPORT1 expect-fail got={(r?.Success == true ? "PASS-BAD" : "fail")} " +
                $"inSlot={r?.InSlot} angle={r?.AngleDeg:0.0} reasons=" +
                $"{string.Join("; ", r?.FailReasons ?? new string[0])}\n");
            _pendingShot = "report_fail_toast.png";
            _rtStage = 1; _rtTimer = 0f;
        }
        else if (_rtStage == 1 && _rtTimer > 5f) // toast ttl is 4 s — must be gone by now
        {
            _car.ResetTo(new Vector3(6.5f, 0, -2.6f), -90f); // dead-center, aligned
            var r = ReportParked();
            _rtPass2 = r is { Success: true };
            File.AppendAllText(_reportLogPath,
                $"REPORT2 expect-pass got={(r?.Success == true ? "pass" : "FAIL")} " +
                $"inSlot={r?.InSlot} angle={r?.AngleDeg:0.0} lat={r?.LatOff:0.00}\n");
            _rtStage = 2; _rtTimer = 0f;
        }
        else if (_rtStage == 2 && _rtTimer > 0.4f)
        {
            _pendingShot = "report_perfect.png";
            _rtStage = 3; _rtTimer = 0f;
        }
        else if (_rtStage == 3 && _rtTimer > 0.6f)
        {
            bool ok = _rtPass1 && _rtPass2;
            File.AppendAllText(_reportLogPath, "RESULT REPORT_TEST " + (ok ? "PASS" : "FAIL") + "\n");
            GetTree().Quit(ok ? 0 : 1);
        }
    }

    private void RunHazardTest(float dt)
    {
        _hvTimer += dt;
        switch (_hvStage)
        {
            case 0: // settle after load → spawn must be collision-free
                if (_hvTimer > 0.9f)
                {
                    bool spawnClean = _car.CollisionCount == 0;
                    _hvPass &= spawnClean;
                    File.AppendAllText(_hazardLogPath,
                        $"LV{_hvLevel + 1} spawn-collisions={_car.CollisionCount} " +
                        (spawnClean ? "ok" : "BAD") + "\n");
                    _hvPosA = SnapHazards();
                    _hvStage = 1; _hvTimer = 0f;
                }
                break;
            case 1: // hazards must have moved meanwhile
                if (_hvTimer > 1.6f)
                {
                    var b = SnapHazards();
                    bool moved = b.Length == 0; // hazard-free level passes vacuously
                    if (b.Length == _hvPosA.Length)
                    {
                        for (int i = 0; i < b.Length; i++)
                            if ((b[i] - _hvPosA[i]).Length() > 0.05f) { moved = true; break; }
                    }
                    _hvPass &= moved;
                    File.AppendAllText(_hazardLogPath,
                        $"LV{_hvLevel + 1} hazards={b.Length} moved={(moved ? "yes" : "NO-BAD")}\n");
                    _pendingShot = $"verify_lv{_hvLevel + 1}.png";
                    _hvStage = 2; _hvTimer = 0f;
                }
                break;
            case 2: // shot taken; L2 additionally proves hazard blocking + counting
                if (_hvTimer > 0.4f)
                {
                    if (_hvLevel == 1)
                    {
                        _car.ResetTo(new Vector3(7f, 0.8f, 0.8f), 0f); // park on the walker's lane
                        _hvStage = 3; _hvTimer = 0f;
                    }
                    else NextHazardLevel();
                }
                break;
            case 3: // the pedestrian must stop short of the parked car and wait
                if (_hvTimer > 4f)
                {
                    var peds = SnapHazards();
                    float dx = Mathf.Abs(peds[0].X - _car.GlobalPosition.X);
                    bool frozen = dx > 1.2f && dx < 3.5f && _car.CollisionCount == 0;
                    _hvPass &= frozen;
                    File.AppendAllText(_hazardLogPath,
                        $"LV2 ped-blocked dx={dx:0.00} collisions={_car.CollisionCount} " +
                        (frozen ? "ok" : "BAD") + "\n");
                    _car.ResetTo(peds[0] + new Vector3(-0.6f, 0.8f, 0f), 0f); // overlap the walker
                    _hvStage = 4; _hvTimer = 0f;
                }
                break;
            case 4: // kinematic contact must register through the hitbox area
                if (_hvTimer > 0.4f)
                {
                    bool hit = _car.CollisionCount > 0;
                    _hvPass &= hit;
                    File.AppendAllText(_hazardLogPath,
                        $"LV2 hazard-collision count={_car.CollisionCount} " +
                        (hit ? "ok" : "BAD") + "\n");
                    _car.ResetTo(new Vector3(0.65f, 0.8f, -2.6f), 0f); // overlap a parked car
                    _hvStage = 5; _hvTimer = 0f;
                }
                break;
            case 5: // static contact must register through the area as well
                if (_hvTimer > 0.4f)
                {
                    bool hit2 = _car.CollisionCount > 0;
                    _hvPass &= hit2;
                    File.AppendAllText(_hazardLogPath,
                        $"LV2 static-collision count={_car.CollisionCount} " +
                        (hit2 ? "ok" : "BAD") + "\n");
                    _hvStage = 6; _hvTimer = 0f;
                }
                break;
            case 6:
                if (_hvTimer > 0.3f) NextHazardLevel();
                break;
        }
    }

    private void NextHazardLevel()
    {
        _hvLevel++;
        if (_hvLevel >= LevelDef.All.Length)
        {
            File.AppendAllText(_hazardLogPath,
                "RESULT HAZARD_TEST " + (_hvPass ? "PASS" : "FAIL") + "\n");
            GetTree().Quit(_hvPass ? 0 : 1);
        }
        else
        {
            LoadLevel(_hvLevel);
            _hvStage = 0; _hvTimer = 0f;
        }
    }

    private Vector3[] SnapHazards()
    {
        var list = new List<Vector3>();
        foreach (var c in _level.GetChildren())
            if (c is Hazard h) list.Add(h.GlobalPosition);
        return list.ToArray();
    }

    // ═══════════════ --verify-features ═══════════════
    // Drives the car directly through the same control API the demo uses:
    // three weather showcases, a wall ram (explosion), a brake slide (skid
    // marks), a perfect park (coin payout), a garage purchase, and an editor
    // save→load→play round-trip. Screenshots every step for visual review.

    private void RunFeaturesTest(float dt)
    {
        _ftTimer += dt;
        switch (_ftStage)
        {
            case 0:
                LoadLevel(6); // 第 7 关 · 暴雨侧位
                Next();
                break;
            case 1:
                if (_ftTimer > 1.5f) { _pendingShot = "verify_rain.png"; Next(); }
                break;
            case 2:
                if (_ftTimer > 0.4f) { LoadLevel(7); Next(); } // 第 8 关 · 风雪窄巷
                break;
            case 3:
                if (_ftTimer > 1.5f) { _pendingShot = "verify_snow.png"; Next(); }
                break;
            case 4:
                if (_ftTimer > 0.4f)
                {
                    LoadLevel(8); // 第 9 关 · 烈日广场
                    _car.ResetTo(new Vector3(5f, 0.8f, 2.0f), -90f); // frame the plaza + the patrol car
                    Next();
                }
                break;
            case 5:
                if (_ftTimer > 1.5f) { _pendingShot = "verify_blaze.png"; Next(); }
                break;
            case 6:
                if (_ftTimer > 0.4f) { LoadLevel(0); Next(); } // back to sunny L1
                break;
            case 7: // ram the east wall — the collision must explode
                if (_ftTimer > 0.6f)
                {
                    _car.ResetTo(new Vector3(16.2f, 0.8f, 1.0f), -90f); // front pokes the wall
                    Next();
                }
                break;
            case 8:
                if (_ftTimer > 0.5f)
                {
                    LogFt($"explosion bursts={_fx.BurstCount}", _fx.BurstCount >= 1);
                    _car.ResetTo(new Vector3(4f, 0.8f, 3.8f), -90f); // open road, drive east
                    _car.SelectGear(Car.Gear.D);
                    _car.Throttle = 1f;
                    Next();
                }
                break;
            case 9:
                if (_ftTimer > 1.3f)
                {
                    _car.Throttle = 0f;
                    _car.BrakeInput = 1f;
                    Next();
                }
                break;
            case 10: // hard braking at speed must leave skid marks
                if (_ftTimer > 0.5f)
                {
                    LogFt($"skid-marks live={_skids.LiveCount}", _skids.LiveCount > 0);
                    _pendingShot = "verify_skid.png";
                    Next();
                }
                break;
            case 11: // park perfectly, report, expect the perfect payout
                if (_ftTimer > 0.6f)
                {
                    // pin the wallet so the run is idempotent; the player's real
                    // save is backed up and restored at the end of the test
                    _ftSaveBackup = File.Exists(Garage.SavePath)
                        ? File.ReadAllText(Garage.SavePath) : null!;
                    Garage.ResetTo(0, "classic");
                    _ftCoins0 = Garage.Coins;
                    _car.ResetTo(new Vector3(6.5f, 0.8f, -2.6f), -90f);
                    _car.BrakeInput = 1f;
                    Next();
                }
                break;
            case 12:
                if (_ftTimer > 0.5f)
                {
                    ReportParked();
                    LogFt($"garage-award coins={_ftCoins0}->{Garage.Coins} fanfare={LastFanfare}",
                        Garage.Coins == _ftCoins0 + Garage.PerfectReward && LastFanfare == "normal");
                    _pendingShot = "verify_verdict.png";
                    Next();
                }
                break;
            case 13: // buy + select a skin through the real API
                if (_ftTimer > 0.5f)
                {
                    bool bought = Garage.TryBuy("ocean");
                    bool selected = bought && Garage.TrySelect("ocean");
                    LogFt($"garage-buy ocean bought={bought} coins={Garage.Coins}",
                        bought && selected && Garage.Coins == _ftCoins0);
                    _car.ApplySkin(Garage.SelectedPaint);
                    _garage.Open();
                    _pendingShot = "verify_garage.png";
                    Next();
                }
                break;
            case 14: // editor save → load → play round-trip
                if (_ftTimer > 0.5f)
                {
                    _garage.Close();
                    _ftCustomBackup = File.Exists(Editor.CustomPath)
                        ? File.ReadAllText(Editor.CustomPath) : null!;
                    Editor.SaveCustom(new Editor.CustomDto
                    {
                        Weather = "Rain",
                        Walls = { new Editor.WallDto { C = new[] { 7f, 0.175f, -4.6f }, S = new[] { 24f, 0.35f, 0.35f } } },
                        Parked = { new Editor.ParkedDto { X = 0.9f, Z = -2.6f, Yaw = 0, Color = 0 } },
                        Peds = { new Editor.PedDto { Ax = 2, Az = 0.8f, Bx = 11, Bz = 0.8f } },
                    });
                    var dto = Editor.LoadCustomDto();
                    bool loaded = dto != null && dto.Peds.Count == 1 && dto.Walls.Count == 1;
                    if (loaded)
                    {
                        _customDef = Editor.DtoToDef(dto);
                        LoadLevel(LevelDef.All.Length); // the custom slot
                    }
                    LogFt("editor round-trip save/load", loaded);
                    Next();
                }
                break;
            case 15:
                if (_ftTimer > 1.2f)
                {
                    var hazards = SnapHazards();
                    LogFt($"custom-level playable hazards={hazards.Length}",
                        hazards.Length == 1 && _level.Def.Title == "自定义关卡");
                    _pendingShot = "verify_custom.png";
                    Next();
                }
                break;
            case 16: // SUV relaxed verdict: same tilted pose — sedan rejects
                if (_ftTimer > 0.5f)
                {
                    LoadLevel(0); // clears _success so ReportParked is live again
                    Garage.ResetTo(1000, "classic");
                    _car.SetVehicle(Car.VehicleKind.Sedan);
                    // yaw -103° = parked along the slot (yaw -90) tilted 13°:
                    // inside the 15° angle rule, sedan corners poke out of the
                    // 2.5 m strip while the SUV's area fraction stays ≈93%
                    _car.ResetTo(new Vector3(6.5f, 0.8f, -2.6f), -103f);
                    _car.BrakeInput = 1f;
                    Next();
                }
                break;
            case 17:
                if (_ftTimer > 0.4f)
                {
                    var rs = ReportParked();
                    LogFt("suv-rule sedan-rejects", rs is { Success: false });
                    bool bought = Garage.TryBuy("suv");
                    bool sel = bought && Garage.TrySelectVehicle("suv");
                    LogFt($"suv garage buy/select bought={bought} coins={Garage.Coins}",
                        bought && sel && Garage.Coins == 200);
                    _car.SetVehicle(Car.VehicleKind.Suv);
                    _car.ResetTo(new Vector3(6.5f, 0.8f, -2.6f), -103f);
                    _car.BrakeInput = 1f;
                    Next();
                }
                break;
            case 18: // ...and the SUV accepts it, with its own fanfare
                if (_ftTimer > 2.2f) // let the fail-report cooldown lapse
                {
                    var rs2 = ReportParked();
                    var (_, _, areaFrac) = SlotGeometry();
                    LogFt($"suv-rule suv-accepts vehicle={_car.Vehicle} success={(rs2?.Success == true)} " +
                        $"areaFrac={areaFrac:0.00} fanfare={LastFanfare}",
                        rs2 is { Success: true } && LastFanfare == "suv");
                    _pendingShot = "verify_suv.png";
                    Next();
                }
                break;
            case 19: // restore the player's custom file and garage save, then report
                if (_ftTimer > 0.5f)
                {
                    if (_ftCustomBackup != null) File.WriteAllText(Editor.CustomPath, _ftCustomBackup);
                    else if (File.Exists(Editor.CustomPath)) File.Delete(Editor.CustomPath);
                    if (_ftSaveBackup != null) File.WriteAllText(Garage.SavePath, _ftSaveBackup);
                    else if (File.Exists(Garage.SavePath)) File.Delete(Garage.SavePath);
                    File.AppendAllText(_featuresLogPath,
                        "RESULT FEATURES_TEST " + (_ftPass ? "PASS" : "FAIL") + "\n");
                    GetTree().Quit(_ftPass ? 0 : 1);
                }
                break;
        }
    }

    private void Next()
    {
        _ftStage++;
        _ftTimer = 0f;
    }

    private void LogFt(string what, bool ok)
    {
        _ftPass &= ok;
        File.AppendAllText(_featuresLogPath, what + (ok ? " ok" : " BAD") + "\n");
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_pendingShot.Length > 0)
        {
            string shot = _pendingShot;
            _pendingShot = "";
            SaveScreenshot(shot);
        }
        if (!_started) return;
        _reportCooldown = Mathf.Max(0f, _reportCooldown - dt);

        // live feedback in every mode: brake lights, skid marks, collisions explode
        _car.SetBrakeLights(_car.BrakeInput > 0.5f || _car.Handbrake);
        _skids.Tick(_car);
        _explodeCd = Mathf.Max(0f, _explodeCd - dt);
        if (_car.CollisionThisTick && _explodeCd <= 0f)
        {
            _explodeCd = 0.5f;
            _fx.Explode(_car.GlobalPosition);
            _shake = 1f;
        }

        if (_reportTest)
        {
            RunReportTest(dt);
            return;
        }

        if (_hazardTest)
        {
            RunHazardTest(dt);
            return;
        }

        if (_featuresTest)
        {
            RunFeaturesTest(dt);
            return;
        }

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

        // no auto-verdict here — the player reports via the button/G key
        // (the demo autopilot submits the same report once settled)
        _timer += dt;
        _hud.SetTimer(_timer, _car.CollisionCount);
        _hud.SetCoins(Garage.Coins);
    }

    public override void _Process(double delta)
    {
        // the editor drives the camera itself while its palette is up
        if (_editor != null && _editor.Active)
            return;

        // top-down follow camera (fixed north-up)
        var def = _level.Def;
        var p = _car.GlobalPosition;
        var desired = p + new Vector3(0, def.CamH, def.CamBack);
        float k = Mathf.Clamp((float)delta * 5f, 0f, 1f);
        _cam.GlobalPosition = _cam.GlobalPosition.Lerp(desired, k);
        _cam.LookAt(p + new Vector3(0, 0.2f, 0), Vector3.Up);

        // collision shake, decaying fast
        if (_shake > 0.01f)
        {
            _cam.GlobalPosition += new Vector3(GD.Randf() - 0.5f, 0, GD.Randf() - 0.5f)
                * (_shake * 0.25f);
            _shake *= Mathf.Exp(-5f * (float)delta);
        }

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
