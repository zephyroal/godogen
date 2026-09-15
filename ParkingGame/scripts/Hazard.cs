using Godot;

namespace ParkingGame;

public enum HazardKind { Pedestrian, PatrolCar }

/// <summary>A level-definition entry: a moving hazard. Pedestrians ping-pong
/// along their path (cross and recross); patrol cars drive the waypoint
/// circuit endlessly — no dead end, no about-face flip.</summary>
public record HazardDef(HazardKind Kind, Vector3[] Path, float Speed = 1.2f)
{
    public bool Loop => Kind == HazardKind.PatrolCar;
}

/// <summary>Kinematic moving obstacle — a walking pedestrian or a patrolling
/// car. Hitting one is an ordinary collision on the player car (it shows in
/// the verdict and costs the perfect rating). SyncToPhysics must stay ON — it
/// is what pushes the moved transform to the physics server, so the collider
/// travels with the visuals and kinematic contacts register.</summary>
public partial class Hazard : AnimatableBody3D
{
    public HazardDef Def = new(HazardKind.Pedestrian, new[] { Vector3.Zero });
    private int _seg;
    private bool _back;
    private float _bob;
    private Vector3 _dir = new(0, 0, -1);
    private Node3D _visual = null!;

    public static Hazard Build(HazardDef def)
    {
        var h = new Hazard
        {
            Name = "Obstacle", // Car.OnBodyEntered counts anything that isn't the Floor
            Def = def,
            // the default true matters: it is this mode that pushes the moved
            // transform to the physics server each step, so the collider
            // actually travels with the visuals and kinematic contacts register
            SyncToPhysics = true,
            Position = def.Path[0],
        };
        h.AddChild(new CollisionShape3D
        {
            Shape = def.Kind == HazardKind.Pedestrian
                ? new CapsuleShape3D { Radius = 0.3f, Height = 1.4f }
                : new BoxShape3D { Size = new Vector3(Car.SedanWid, 1.05f, Car.SedanLen) },
            Position = def.Kind == HazardKind.Pedestrian
                ? new Vector3(0, 0.7f, 0)
                : new Vector3(0, 0.55f, 0),
        });
        h._visual = def.Kind == HazardKind.Pedestrian ? BuildPedVisual() : BuildPatrolVisual();
        h.AddChild(h._visual);
        return h;
    }

    private static Node3D BuildPedVisual()
    {
        var g = new Node3D();
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.45f, 0.70f) };
        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.87f, 0.70f, 0.58f) };
        g.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = 0.85f },
            MaterialOverride = cloth,
            Position = new Vector3(0, 0.72f, 0),
        });
        g.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f },
            MaterialOverride = skin,
            Position = new Vector3(0, 1.34f, 0),
        });
        return g;
    }

    private static Node3D BuildPatrolVisual()
    {
        var g = new Node3D();
        var paint = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.70f, 0.15f), // safety-amber security car
            Roughness = 0.4f,
            Metallic = 0.1f,
        };
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.13f, 0.16f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f,
            Metallic = 0.9f,
        };
        var tire = new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.09f, 0.10f), Roughness = 0.95f };
        var light = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.97f, 0.85f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.95f, 0.8f),
            EmissionEnergyMultiplier = 1.4f,
        };
        var tail = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.1f, 0.1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.12f, 0.1f),
            EmissionEnergyMultiplier = 1.1f,
        };

        g.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Car.SedanWid, 0.55f, Car.SedanLen) },
            MaterialOverride = paint,
            Position = new Vector3(0, 0.52f, 0),
        });
        g.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Car.SedanWid - 0.14f, 0.30f, 2.16f) },
            MaterialOverride = glass,
            Position = new Vector3(0, 0.95f, 0.25f),
        });
        g.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Car.SedanWid - 0.2f, 0.20f, 2.0f) },
            MaterialOverride = paint,
            Position = new Vector3(0, 1.20f, 0.22f),
        });
        foreach (var x in new[] { -0.55f, 0.55f })
        {
            g.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.30f, 0.12f, 0.06f) },
                MaterialOverride = light,
                Position = new Vector3(x, 0.55f, -Car.SedanLen / 2f - 0.02f),
            });
            g.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.30f, 0.12f, 0.06f) },
                MaterialOverride = tail,
                Position = new Vector3(x, 0.55f, Car.SedanLen / 2f + 0.02f),
            });
        }
        foreach (var (x, z) in new[] { (-0.80f, -1.48f), (0.80f, -1.48f), (-0.80f, 1.48f), (0.80f, 1.48f) })
        {
            g.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.34f, Height = 0.26f },
                MaterialOverride = tire,
                Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
                Position = new Vector3(x, 0.10f, z),
            });
        }
        return g;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var path = Def.Path;
        if (path.Length >= 2)
        {
            int next = _seg + (_back ? -1 : 1);
            if (Def.Loop)
            {
                if (next < 0) next = path.Length - 1;
                else if (next >= path.Length) next = 0;
            }
            else if (next < 0 || next >= path.Length)
            {
                _back = !_back;
                next = _seg + (_back ? -1 : 1);
            }

            Vector3 to = path[next] - GlobalPosition;
            to.Y = 0f;
            float step = Def.Speed * dt;
            bool arriving = to.Length() <= step;
            if (to.Length() > 1e-4f) _dir = to / to.Length();

            // look ahead along the travel direction: hazards stand and wait
            // when something solid blocks the way (usually the player's car) —
            // a pedestrian must not plow a 1200 kg vehicle down the road.
            // The ray starts outside the hazard's own shape, so it never self-hits.
            bool patrol = Def.Kind == HazardKind.PatrolCar;
            Vector3 origin = GlobalPosition + _dir * (patrol ? 2.45f : 0.05f);
            origin.Y = patrol ? 0.55f : 0.7f;
            var query = new PhysicsRayQueryParameters3D
            {
                From = origin,
                To = origin + _dir * (patrol ? 0.45f : 0.35f + step),
            };
            bool blocked = GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;

            if (!blocked)
            {
                if (arriving)
                {
                    GlobalPosition = path[next];
                    _seg = next;
                }
                else
                {
                    GlobalPosition += _dir * step;
                }
            }
        }

        // face the travel direction (smooth); pedestrians add a walking bob
        float yaw = Mathf.Atan2(-_dir.X, -_dir.Z);
        _visual.Rotation = new Vector3(0,
            Mathf.LerpAngle(_visual.Rotation.Y, yaw, Mathf.Clamp(10f * dt, 0f, 1f)), 0);
        if (Def.Kind == HazardKind.Pedestrian)
        {
            _bob += Def.Speed * 7f * dt;
            _visual.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(_bob)) * 0.06f, 0);
        }
    }
}
