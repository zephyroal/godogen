"""Parse GLB JSON chunk: node tree, mesh extents, face counts. Temp inspection tool."""
import json
import struct
import sys
from pathlib import Path

def parse_glb(path: Path):
    data = path.read_bytes()
    assert data[:4] == b"glTF", "not a GLB"
    json_len = struct.unpack_from("<I", data, 12)[0]
    header = json.loads(data[20 : 20 + json_len])

    # binary buffer for accessor min/max fallback (min/max stored in JSON anyway)
    accessors = header.get("accessors", [])
    meshes = header.get("meshes", [])
    nodes = header.get("nodes", [])

    mesh_info = []
    for i, m in enumerate(meshes):
        faces = 0
        for prim in m["primitives"]:
            idx_acc = prim["indices"]
            faces += accessors[idx_acc]["count"] // 3
        mesh_info.append({"name": m.get("name", f"mesh{i}"), "faces": faces})

    def node_aabb(node, parent_xform):
        # gather world-space min/max from accessors of this node's mesh
        mins, maxs = [], []
        if "mesh" in node:
            for prim in meshes[node["mesh"]]["primitives"]:
                acc = accessors[prim["attributes"]["POSITION"]]
                mn, mx = acc.get("min"), acc.get("max")
                if mn and mx:
                    mins.append(mn)
                    maxs.append(mx)
        return mins, maxs

    def mat_mult(m, v):
        # column-major glTF 4x4
        x = m[0] * v[0] + m[4] * v[1] + m[8] * v[2] + m[12]
        y = m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13]
        z = m[2] * v[0] + m[6] * v[1] + m[10] * v[2] + m[14]
        return [x, y, z]

    # approximate global AABB: transform each mesh's min/max corners by node matrix (ignore nesting)
    gmin = [float("inf")] * 3
    gmax = [float("-inf")] * 3
    for n in nodes:
        if "mesh" not in n:
            continue
        m = n.get("matrix", [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])
        rot = n.get("rotation", [0, 0, 0, 1])
        scale = n.get("scale", [1, 1, 1])
        translation = n.get("translation", [0, 0, 0])
        # only handle TRS if no matrix given
        if "matrix" not in n:
            # build matrix from TRS (quaternion)
            x, y, z, w = rot
            rm = [
                1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y),
            ]
            m = [
                rm[0] * scale[0], rm[1] * scale[0], rm[2] * scale[0], 0,
                rm[3] * scale[1], rm[4] * scale[1], rm[5] * scale[1], 0,
                rm[6] * scale[2], rm[7] * scale[2], rm[8] * scale[2], 0,
                translation[0], translation[1], translation[2], 1,
            ]
        mins, maxs = node_aabb(n, m)
        for mn, mx in zip(mins, maxs):
            for corner in [
                [mn[0], mn[1], mn[2]], [mx[0], mn[1], mn[2]],
                [mn[0], mx[1], mn[2]], [mx[0], mx[1], mn[2]],
                [mn[0], mn[1], mx[2]], [mx[0], mn[1], mx[2]],
                [mn[0], mx[1], mx[2]], [mx[0], mx[1], mx[2]],
            ]:
                w = mat_mult(m, corner)
                for i in range(3):
                    gmin[i] = min(gmin[i], w[i])
                    gmax[i] = max(gmax[i], w[i])

    total_faces = sum(mi["faces"] for mi in mesh_info)
    print(f"== {path.name} ==")
    print(f"  meshes: {len(meshes)}, total faces: {total_faces}")
    for mi in mesh_info[:12]:
        print(f"    {mi['name']}: {mi['faces']} faces")
    if len(mesh_info) > 12:
        print(f"    ... +{len(mesh_info) - 12} more meshes")
    if gmin[0] != float("inf"):
        print(f"  approx AABB min: [{gmin[0]:.3f}, {gmin[1]:.3f}, {gmin[2]:.3f}]")
        print(f"  approx AABB max: [{gmax[0]:.3f}, {gmax[1]:.3f}, {gmax[2]:.3f}]")
        print(f"  size: X={gmax[0]-gmin[0]:.3f} Y={gmax[1]-gmin[1]:.3f} Z={gmax[2]-gmin[2]:.3f}")
    print(f"  textures: {len(header.get('images', []))}, materials: {len(header.get('materials', []))}")
    print(f"  root nodes: {[n.get('name') for n in nodes if 'mesh' in n][:5]}")

for p in sorted(Path(sys.argv[1]).glob("*.glb")):
    parse_glb(p)
