using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// GLB prop loader: instantiates an imported model scene, measures its combined
/// mesh AABB, and rescales it to a target height with the base centered on the
/// returned node's origin (y=0 at the feet). Returns null when the file is
/// missing so callers can fall back to programmatic geometry.
/// </summary>
public static class Glb
{
    private static readonly Dictionary<string, PackedScene> Cache = new();

    /// <summary>Half extents (X, Y, Z) of the model scaled to targetHeight. Zero if absent.</summary>
    public static Vector3 ScaledHalfExtents(string resPath, float targetHeight)
    {
        var ps = Load(resPath);
        if (ps == null) return Vector3.Zero;
        var probe = (Node3D)ps.Instantiate();
        var aabb = Measure(probe);
        probe.QueueFree();
        if (aabb.Size.Y <= 0f) return Vector3.Zero;
        return aabb.Size * (targetHeight / aabb.Size.Y) * 0.5f;
    }

    /// <summary>Load a GLB, scale it to targetHeight, bottom-align y=0, center the footprint on the node origin.
    /// Returns a named placement node ("&lt;model&gt;_wrap"); the single child is the scene instance carrying the
    /// normalize transform, so callers can freely set Position/Rotation/Scale on the returned node.</summary>
    public static Node3D Create(string resPath, float targetHeight)
    {
        var ps = Load(resPath);
        if (ps == null) return null;
        var inst = (Node3D)ps.Instantiate();
        var aabb = Measure(inst);
        if (aabb.Size.Y <= 0f)
        {
            inst.QueueFree();
            return null;
        }

        float s = targetHeight / aabb.Size.Y;
        // bake the normalize transform (target-height scale + bottom-center align) into the instance root:
        // one parent level instead of two, and the wrap stays free for placement transforms
        var normalize = new Transform3D(
            Basis.Identity.Scaled(Vector3.One * s),
            new Vector3(-aabb.GetCenter().X * s, -aabb.Position.Y * s, -aabb.GetCenter().Z * s));
        inst.Transform = normalize * inst.Transform;

        var wrap = new Node3D { Name = $"{resPath.GetFile().GetBaseName()}_wrap" };
        wrap.AddChild(inst);
        return wrap;
    }

    private static PackedScene Load(string resPath)
    {
        if (Cache.TryGetValue(resPath, out var ps)) return ps;
        if (!ResourceLoader.Exists(resPath)) return null;
        ps = ResourceLoader.Load<PackedScene>(resPath);
        Cache[resPath] = ps;
        return ps;
    }

    /// <summary>Combined AABB of all meshes under the instance, in its local space.</summary>
    private static Aabb Measure(Node3D root)
    {
        var aabb = new Aabb();
        bool any = false;
        MeasureInto(root, Transform3D.Identity, ref aabb, ref any);
        return aabb;
    }

    private static void MeasureInto(Node3D node, Transform3D xform, ref Aabb acc, ref bool any)
    {
        Transform3D t = xform * node.Transform;
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            Aabb local = mi.Mesh.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? local.Position.X : local.End.X,
                    (i & 2) == 0 ? local.Position.Y : local.End.Y,
                    (i & 4) == 0 ? local.Position.Z : local.End.Z);
                var w = t * c;
                acc = any ? acc.Expand(w) : new Aabb(w, Vector3.Zero);
                any = true;
            }
        }
        foreach (var child in node.GetChildren())
            if (child is Node3D n)
                MeasureInto(n, t, ref acc, ref any);
    }
}
