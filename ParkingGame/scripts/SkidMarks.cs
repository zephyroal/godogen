using Godot;

namespace ParkingGame;

/// <summary>Brake skid marks: a ring buffer of flat dark quads dropped at the
/// rear wheels while braking at speed or sliding on the handbrake. Marks fade
/// out over ~10 s (per-instance GeometryInstance3D.Transparency).</summary>
public partial class SkidMarks : Node3D
{
    private const int Pool = 360;
    private const float Ttl = 10f;

    private readonly MeshInstance3D[] _marks = new MeshInstance3D[Pool];
    private readonly float[] _life = new float[Pool];
    private int _next;
    private int _tick;

    /// <summary>Marks currently visible — asserted by --verify-features.</summary>
    public int LiveCount { get; private set; }

    public override void _Ready()
    {
        var mesh = new BoxMesh
        {
            Size = new Vector3(0.22f, 0.012f, 0.42f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.04f, 0.04f, 0.05f, 0.8f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        for (int i = 0; i < Pool; i++)
        {
            var m = new MeshInstance3D { Mesh = mesh, Visible = false };
            AddChild(m);
            _marks[i] = m;
        }
    }

    /// <summary>Called every physics tick: drops pairs of marks at the rear
    /// wheels (every 3rd tick ≈ 20 Hz) while the car skids.</summary>
    public void Tick(Car car)
    {
        float speed = Mathf.Abs(car.ForwardSpeed);
        bool skidding = (car.BrakeInput > 0.55f || car.Handbrake) && speed > 0.9f;
        _tick++;
        if (!skidding || _tick % 3 != 0) return;

        float yaw = car.Rotation.Y;
        foreach (var side in new[] { -0.8f, 0.8f })
        {
            // y 0.064: above the road paint (0.034-0.054) to avoid z-fighting
            Vector3 p = car.GlobalTransform * new Vector3(side, 0.064f, 1.48f);
            var m = _marks[_next];
            _life[_next] = Ttl;
            _next = (_next + 1) % Pool;
            m.GlobalPosition = p;
            m.Rotation = new Vector3(0, yaw, 0);
            m.Visible = true;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        int live = 0;
        for (int i = 0; i < Pool; i++)
        {
            if (_life[i] <= 0f) continue;
            _life[i] -= dt;
            var m = _marks[i];
            if (_life[i] <= 0f)
            {
                m.Visible = false;
                m.Transparency = 1f;
                continue;
            }
            live++;
            m.Transparency = 1f - _life[i] / Ttl * 0.8f; // alpha 0.8 → 0
        }
        LiveCount = live;
    }
}
