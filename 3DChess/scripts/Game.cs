using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>Root of the match: camera, lights, pieces, input picking, move flow, AI scheduling.
/// Taps arrive as (emulated) left mouse clicks — one path for touch and mouse; finger drags orbit/zoom the camera.</summary>
public partial class Game : Node3D
{
    public enum Mode { VsAI, TwoPlayers }

    public static Game Instance { get; private set; }

    public new Position Position = Position.Initial();
    public Mode CurrentMode = Mode.VsAI;
    public bool GameStarted;
    public bool GameOver;

    public Camera3D Cam { get; private set; }
    public Board Board { get; private set; }
    public HUD Hud { get; private set; }
    public AudioPlayer Audio { get; private set; }
    public readonly List<Piece> AllPieces = new();
    private readonly Dictionary<int, Piece> _pieces = new();
    private readonly Dictionary<int, Vector2> _touchPos = new();
    private float _lastPinchDist;

    private Piece _selected;
    private List<Move> _legalCache = new();
    private Piece _movingPiece;
    private System.Threading.Tasks.Task<AI.Result> _aiTask;    private Move? _pendingAiMove;
    private float _aiApplyDelay;
    private int _redCaptured, _blackCaptured;

    // camera orbit
    public float Yaw, Pitch = 0.88f, Dist = 14.2f;
    private readonly Vector3 _camTarget = new(0f, 0f, 0.3f);

    // cinematic focus: zooms in on a point, holds, then eases back
    private Vector3 _focusPos = new(0f, 0f, 0.3f);
    private float _focusDist = 14.2f;
    private float _focusTimer;          // >0 = active; counts down in _Process
    private const float FocusHoldTime = 1.2f;
    private const float FocusZoomDist = 6.0f;
    private float _camEaseSpeed = 6f;

    public override void _Ready()
    {
        Instance = this;
        BuildEnvironment();
        Board = new Board();
        AddChild(Board);
        SpawnPieces();
        Hud = new HUD { Name = "HUD" };
        AddChild(Hud);
        if (OS.IsDebugBuild())
            AddChild(new Fps { Name = "Fps" }); // release builds: no node, zero overhead
        UpdateCamera();

        // instant rematch: re-enter the previous mode without the selection screen
        if (AutoStart != null)
        {
            var mode = AutoStart.Value;
            AutoStart = null;
            Hud.HideStart();
            ChooseMode(mode);
        }
    }

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.25f, 0.38f, 0.66f),
            SkyHorizonColor = new Color(0.74f, 0.81f, 0.90f),
            GroundBottomColor = new Color(0.22f, 0.16f, 0.11f),
        };
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.5f,
            FogEnabled = true,
            FogLightColor = new Color(0.72f, 0.78f, 0.88f),
            FogDensity = 0.006f,
            FogSkyAffect = 0.15f,
            SsaoEnabled = true,
            SsaoIntensity = 2.5f,
            SsaoRadius = 1.2f,
            SdfgiEnabled = true,
            SdfgiUseOcclusion = true,
            SdfgiReadSkyLight = true,
            SdfgiBounceFeedback = 0.3f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.05f,
            TonemapMode = Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.65f,
            DirectionalShadowMaxDistance = 40f,
        };
        sun.RotationDegrees = new Vector3(-58f, 30f, 0f);
        AddChild(sun);

        var fill = new DirectionalLight3D { LightEnergy = 0.3f, ShadowEnabled = false };
        fill.RotationDegrees = new Vector3(-30f, -140f, 0f);
        AddChild(fill);

        Cam = new Camera3D { Fov = 38f, Near = 0.1f, Far = 100f };
        AddChild(Cam);
        Cam.MakeCurrent();

        Audio = new AudioPlayer();
        AddChild(Audio);
    }

    private void SpawnPieces()
    {
        for (int i = 0; i < 90; i++)
        {
            int p = Position.Cells[i];
            if (p == 0) continue;
            var piece = new Piece
            {
                Index = i,
                Side = p > 0 ? Side.Red : Side.Black,
                Type = (PieceType)System.Math.Abs(p),
            };
            AddChild(piece);
            AllPieces.Add(piece);
            _pieces[i] = piece;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_focusTimer > 0f) _focusTimer -= dt;
        UpdateCamera();

        // move animation completion: landing dust, then post-move logic
        if (_movingPiece != null && _movingPiece.AnimDone)
        {
            var dust = FX.Burst(_movingPiece.Position + new Vector3(0f, 0.05f, 0f),
                new Color(0.55f, 0.44f, 0.30f), 8, 0.8f, 1.8f);
            AddChild(dust);
            var dustTimer = GetTree().CreateTimer(1.2f);
            dustTimer.Timeout += dust.QueueFree;
            _movingPiece = null;
            Audio.Play("move");
            FinishMove();
        }

        // AI result polling: apply only when the board is at rest, keep the move until it is played
        if (_aiTask != null && _aiTask.IsCompleted)
        {
            var result = _aiTask.Result;
            _aiTask = null;
            _aiApplyDelay = 0.4f;
            _pendingAiMove = result.Move;
            Audio.StopLoop();
        }
        if (_pendingAiMove != null && _movingPiece == null)
        {
            _aiApplyDelay -= dt;
            if (_aiApplyDelay <= 0f)
            {
                var m = _pendingAiMove.Value;
                var legal = Rules.LegalMoves(Position.Cells, Position.Turn);
                if (legal.Count > 0)
                {
                    var chosen = legal.Contains(m) ? m : legal[0]; // never stall
                    _pendingAiMove = null;
                    ExecuteMove(chosen);
                }
            }
        }
    }

    private void UpdateCamera()
    {
        Pitch = Mathf.Clamp(Pitch, 0.45f, 1.35f);
        Dist = Mathf.Clamp(Dist, 7f, 20f);

        // determine effective target and distance
        Vector3 tgt;
        float effDist;
        if (_focusTimer > 0f)
        {
            tgt = _focusPos;
            effDist = FocusZoomDist;
        }
        else
        {
            tgt = _camTarget;
            effDist = Dist;
        }

        var off = new Vector3(
            Mathf.Sin(Yaw) * Mathf.Cos(Pitch),
            Mathf.Sin(Pitch),
            Mathf.Cos(Yaw) * Mathf.Cos(Pitch)) * effDist;

        // smooth lerp — use the frame delta from the scene tree
        float dt = (float)GetProcessDeltaTime();
        float k = Mathf.Min(1f, dt * _camEaseSpeed);
        Cam.Position = Cam.Position.Lerp(tgt + off, k);
        Cam.LookAt(Cam.Position - off, Vector3.Up); // look at the target, not camera-self
    }

    // ---- input: taps via (emulated) left click; camera via touch drag / pinch / right-drag / wheel ----

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton mouse:
                if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed) TapScreen(mouse.Position);
                else if (mouse.ButtonIndex == MouseButton.WheelUp) Dist *= 0.92f;
                else if (mouse.ButtonIndex == MouseButton.WheelDown) Dist *= 1.08f;
                break;

            case InputEventMouseMotion hover when hover.ButtonMask == 0:
                int hi = PickIndex(hover.Position);
                Board?.ShowHover(hi >= 0 ? hi : null);
                break;

            case InputEventScreenTouch touch:
                if (touch.Pressed) _touchPos[touch.Index] = touch.Position;
                else
                {
                    _touchPos.Remove(touch.Index);
                    if (_touchPos.Count < 2) _lastPinchDist = 0f;
                }
                break;

            case InputEventScreenDrag drag:
                _touchPos[drag.Index] = drag.Position;
                if (_touchPos.Count >= 2)
                {
                    var pts = new List<Vector2>(_touchPos.Values);
                    float d1 = pts[0].DistanceTo(pts[1]);
                    if (_lastPinchDist > 1f && d1 > 1f)
                        Dist *= Mathf.Clamp(_lastPinchDist / d1, 0.9f, 1.1f);
                    _lastPinchDist = d1;
                }
                else if (drag.Relative.Length() > 4f)
                {
                    Yaw -= drag.Relative.X * 0.005f;
                    Pitch += drag.Relative.Y * 0.004f;
                }
                break;

            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Right) != 0:
                Yaw -= motion.Relative.X * 0.006f;
                Pitch += motion.Relative.Y * 0.005f;
                break;
        }
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true } key)
        {
            if (key.PhysicalKeycode == Key.R && GameOver) Rematch();
            else if (key.PhysicalKeycode == Key.U) Undo();
        }
    }

    /// <summary>Screen point → nearest grid point (tests the board plane and the piece-top plane), then acts.</summary>
    private void TapScreen(Vector2 screenPos) => HandleTapIndex(PickIndex(screenPos));

    /// <summary>Screen point → nearest grid index within the pick tolerance, or -1.</summary>
    private int PickIndex(Vector2 screenPos)
    {
        var origin = Cam.ProjectRayOrigin(screenPos);
        var dir = Cam.ProjectRayNormal(screenPos);
        int bestIdx = -1;
        float bestDist = 1e9f;

        foreach (float planeY in new[] { 0f, 0.45f })
        {
            if (Mathf.IsEqualApprox(dir.Y, 0f)) continue;
            float t = (planeY - origin.Y) / dir.Y;
            if (t <= 0f) continue;
            var p = origin + dir * t;
            int f = Mathf.RoundToInt(p.X / Board.S + 4f);
            int r = Mathf.RoundToInt(4.5f - p.Z / Board.S);
            if (f < 0 || f > 8 || r < 0 || r > 9) continue;
            var wp = Board.WorldOf(r * 9 + f);
            float d2 = (p.X - wp.X) * (p.X - wp.X) + (p.Z - wp.Z) * (p.Z - wp.Z);
            if (d2 < 0.36f && t < bestDist) { bestDist = t; bestIdx = r * 9 + f; }
        }
        return bestIdx;
    }

    private void HandleTapIndex(int idx)
    {
        if (!GameStarted || GameOver || _movingPiece != null) return;
        if (CurrentMode == Mode.VsAI && Position.Turn == Side.Black) return;

        if (_selected != null && idx >= 0)
        {
            int mi = _legalCache.FindIndex(x => x.To == idx);
            if (mi >= 0)
            {
                ExecuteMove(_legalCache[mi]);
                return;
            }
            if (Position.Cells[idx] != 0 && (Position.Cells[idx] > 0) == (Position.Turn == Side.Red))
            {
                Select(idx); // switch to another own piece
                return;
            }
            Board.ShowIllegal(idx); // non-legal target: brief red flash, keep the selection
            Audio.Play("illegal");
            return;
        }

        if (idx >= 0 && Position.Cells[idx] != 0 &&
            (Position.Cells[idx] > 0) == (Position.Turn == Side.Red))
            Select(idx);
        else
            Deselect();
    }

    private void Select(int idx)
    {
        Deselect();
        _selected = _pieces.GetValueOrDefault(idx);
        if (_selected == null) return;
        _legalCache = Rules.LegalMoves(Position.Cells, Position.Turn);
        _selected.SetSelected(true);
        Board.ShowSelection(idx);
        Board.ShowMoves(_legalCache, idx, Position.Cells);
        Audio.Play("select");
    }

    private void Deselect()
    {
        if (_selected != null) _selected.SetSelected(false);
        _selected = null;
        Board.HideSelection();
        Board.ClearMoves();
    }

    // ---- move flow ----

    public void ExecuteMove(Move m)
    {
        if (_movingPiece != null) return;
        Deselect();

        var mover = _pieces.GetValueOrDefault(m.From);
        if (mover == null) return;
        _pieces.Remove(m.From);

        Piece victim = _pieces.GetValueOrDefault(m.To);
        if (victim != null) _pieces.Remove(m.To);

        Position.MakeMove(m);
        mover.Index = m.To;
        _pieces[m.To] = mover;
        Board.ShowLastMove(m);

        if (victim != null)
        {
            victim.Alive = false;
            Audio.Play("capture");
            var w = Board.WorldOf(m.To);
            var burst = FX.Burst(w + new Vector3(0f, 0.3f, 0f),
                victim.Side == Side.Red ? new Color(0.85f, 0.3f, 0.2f) : new Color(0.3f, 0.26f, 0.22f));
            AddChild(burst);
            var timer = GetTree().CreateTimer(1.2f);
            timer.Timeout += burst.QueueFree;
            victim.AnimateCapture(TraySlot(victim));

            // cinematic camera: zoom in on the capture point
            _focusPos = w + new Vector3(0f, 0f, 0f);
            _focusDist = FocusZoomDist;
            _focusTimer = FocusHoldTime;
        }
        mover.AnimateMove(Board.WorldOf(m.To), victim != null);
        _movingPiece = mover;
    }

    private Vector3 TraySlot(Piece victim)
    {
        // red casualties go to the left tray, black to the right; 5 per column, stacked beyond that
        int n = victim.Side == Side.Red ? _redCaptured++ : _blackCaptured++;
        float x = victim.Side == Side.Red ? -6.6f : 6.6f;
        float z = 2.2f - (n % 5) * 1.1f;
        float y = 0.02f + (n / 5) * 0.55f;
        return new Vector3(x, y, z);
    }

    private void FinishMove()
    {
        if (Rules.IsGameOver(Position.Cells, Position.Turn, out bool mate, out Side loser))
        {
            GameOver = true;
            Board.ShowCheck(Rules.InCheck(Position.Cells, Position.Turn) ? Rules.FindKing(Position.Cells, Position.Turn) : null);
            Hud.SetTurn(null, false, 0);
            Hud.ShowEnd(loser == Side.Red ? Side.Black : Side.Red, mate);
            Audio.Play(mate ? "checkmate" : "stalemate");
            GetTree().CreateTimer(0.8f).Timeout += () => Audio.Play(loser == Side.Red ? "victory" : "defeat");

            // celebration burst over the defeated king
            var kPos = Board.WorldOf(Rules.FindKing(Position.Cells, loser));
            var burst = FX.Burst(kPos + new Vector3(0f, 0.6f, 0f), new Color(1f, 0.85f, 0.3f), 42, 3f, 6.5f);
            AddChild(burst);
            var timer = GetTree().CreateTimer(1.4f);
            timer.Timeout += burst.QueueFree;
            return;
        }

        bool inCheck = Rules.InCheck(Position.Cells, Position.Turn);
        Board.ShowCheck(inCheck ? Rules.FindKing(Position.Cells, Position.Turn) : null);
        if (inCheck) { Hud.FlashCheck(); Audio.Play("check"); }

        bool aiTurn = CurrentMode == Mode.VsAI && Position.Turn == Side.Black;
        Hud.SetTurn(Position.Turn, aiTurn, Position.History.Count + 1);
        if (aiTurn) StartAI();
    }

    private void StartAI()
    {
        if (_aiTask != null) return;
        var cells = (int[])Position.Cells.Clone();
        _aiTask = new System.Threading.Tasks.Task<AI.Result>(() => AI.Search(cells, Position.Turn, 900));
        _aiTask.Start();
        Audio.StartLoop("ai_thinking");
    }

    // ---- lifecycle ----

    /// <summary>Rematch instantly in the same mode; the static survives the scene reload.</summary>
    public static Mode? AutoStart;

    public void ChooseMode(Mode mode)
    {
        CurrentMode = mode;
        GameStarted = true;
        Hud.SetTurn(Position.Turn, false, 1);
        Audio.Play("game_start");
    }

    public void Rematch()
    {
        AutoStart = CurrentMode;
        GetTree().ReloadCurrentScene();
    }

    public void Menu() => GetTree().ReloadCurrentScene();

    public void Undo()
    {
        if (!GameStarted || GameOver || _movingPiece != null || _aiTask != null || Position.History.Count == 0) return;
        if (_pendingAiMove != null) return;
        Audio.Play("undo");

        int plies = CurrentMode == Mode.VsAI
            ? (Position.Turn == Side.Red && Position.History.Count >= 2 ? 2 : 1)
            : 1;

        for (int i = 0; i < plies && Position.History.Count > 0; i++)
        {
            var (m, captured) = Position.History[^1];
            Position.UnmakeMove();
            var mover = _pieces.GetValueOrDefault(m.To);
            if (mover != null)
            {
                _pieces.Remove(m.To);
                mover.Index = m.From;
                _pieces[m.From] = mover;
                mover.Teleport(Board.WorldOf(m.From));
            }
            if (captured != 0)
            {
                var victim = AllPieces.Find(p =>
                    !p.Alive && p.Index == m.To &&
                    p.Side == (captured > 0 ? Side.Red : Side.Black) &&
                    p.Type == (PieceType)System.Math.Abs(captured));
                if (victim != null)
                {
                    victim.Alive = true;
                    victim.Index = m.To;
                    _pieces[m.To] = victim;
                    victim.Teleport(Board.WorldOf(m.To));
                    if (victim.Side == Side.Red) _redCaptured--; else _blackCaptured--;
                }
            }
        }

        Deselect();
        if (Position.History.Count > 0) Board.ShowLastMove(Position.History[^1].Move);
        else Board.HideLastMove();
        Board.ShowCheck(Rules.InCheck(Position.Cells, Position.Turn) ? Rules.FindKing(Position.Cells, Position.Turn) : null);
        Hud.SetTurn(Position.Turn, false, Position.History.Count + 1);
        if (CurrentMode == Mode.VsAI && Position.Turn == Side.Black) StartAI();
    }
}
