"""Convert the Godot reference's GLB piece models into CocosChess .xmodel files.

cocos2d-x v4 has no glTF loader, so this offline step flattens each GLB into a
compact binary bundle the game reads directly: the mesh (positions / normals /
uvs + uint16 indices) already scaled to the board exactly like Godot's
Piece.BuildFromGlb does — TargetHeight sizing, bottom aligned at y=0, XZ
centered — plus the base-color JPEG that the engine's unlit POSITION_TEXTURE_3D
material samples.

Usage:
    python glb_to_xmodel.py <glb_dir> <out_models_dir>

Reads every <type>_<side>.glb in <glb_dir> and writes <type>_<side>.xmodel plus
a manifest.json into <out_models_dir>.
"""
import json
import os
import struct
import sys

# Godot Piece.TargetHeight * 1.1 (world units); TARGET_D mirrors Piece.cs
TYPE_HEIGHT = {
    "king": 0.95 * 1.1,
    "advisor": 0.75 * 1.1,
    "elephant": 0.80 * 1.1,
    "horse": 0.85 * 1.1,
    "chariot": 0.85 * 1.1,
    "cannon": 0.85 * 1.1,
    "soldier": 0.65 * 1.1,
}
TARGET_D = 0.84
MAGIC = b"XMOD"
VERSION = 1


def read_glb(path):
    data = open(path, "rb").read()
    magic, version, _total = struct.unpack("<4sII", data[:12])
    assert magic == b"glTF", path
    jlen, jtype = struct.unpack("<I4s", data[12:20])
    assert jtype == b"JSON", path
    js = json.loads(data[20:20 + jlen])
    pos = 20 + jlen
    while pos % 4:
        pos += 1  # JSON chunk padded with spaces
    blen, btype = struct.unpack("<II", data[pos:pos + 8])
    assert btype == 0x004E4942, path  # "BIN\0"
    binb = data[pos + 8:pos + 8 + blen]
    return js, binb


# glTF componentType -> (struct char, bytes)
CT = {5120: ("B", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
      5125: ("I", 4), 5126: ("f", 4)}


def read_accessor(js, binb, acc, shape):
    """Return a flat list of numbers for the accessor (stride-aware)."""
    fmt_char, comp_bytes = CT[acc["componentType"]]
    bv = js["bufferViews"][acc["bufferView"]]
    base = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = acc.get("byteStride") or comp_bytes * shape
    count = acc["count"]
    out = []
    for i in range(count):
        off = base + i * stride
        out.extend(struct.unpack_from("<%d%s" % (shape, fmt_char), binb, off))
    return out


def convert(path, out_dir):
    name = os.path.splitext(os.path.basename(path))[0]
    ptype = name.split("_")[0]
    assert ptype in TYPE_HEIGHT, "unknown piece type: %s" % name

    js, binb = read_glb(path)
    meshes = js["meshes"]
    prims = [p for m in meshes for p in m.get("primitives", [])]
    assert len(prims) == 1, "%s: expected a single primitive, got %d" % (name, len(prims))
    prim = prims[0]
    attrs = prim["attributes"]
    assert "POSITION" in attrs and "NORMAL" in attrs and "TEXCOORD_0" in attrs, name

    verts = js["accessors"][attrs["POSITION"]]
    pos = read_accessor(js, binb, verts, 3)
    nrm = read_accessor(js, binb, js["accessors"][attrs["NORMAL"]], 3)
    uv = read_accessor(js, binb, js["accessors"][attrs["TEXCOORD_0"]], 2)
    idx = read_accessor(js, binb, js["accessors"][prim["indices"]], 1)
    vc = verts["count"]

    xs, ys, zs = pos[0::3], pos[1::3], pos[2::3]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    zmin, zmax = min(zs), max(zs)
    h = ymax - ymin
    d = max(xmax - xmin, zmax - zmin)
    scale = min(TYPE_HEIGHT[ptype] / h, TARGET_D / d) * 0.95
    cx, cz = (xmin + xmax) / 2.0, (zmin + zmax) / 2.0

    out_pos = []
    for i in range(vc):
        out_pos.append((pos[3 * i] - cx) * scale)
        out_pos.append((pos[3 * i + 1] - ymin) * scale)
        out_pos.append((pos[3 * i + 2] - cz) * scale)

    out_idx = []
    for v in idx:
        assert v < 65536, "%s: vertex index %d exceeds uint16" % (name, v)
        out_idx.append(v)

    # base-color texture (JPEG embedded in the GLB)
    mat = js["materials"][prim["material"]]
    tex_i = mat["pbrMetallicRoughness"]["baseColorTexture"]["index"]
    img = js["images"][js["textures"][tex_i]["source"]]
    bv = js["bufferViews"][img["bufferView"]]
    tex_bytes = binb[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]]
    mime = img.get("mimeType", "image/jpeg")
    tex_type = 0 if "jpeg" in mime else 1

    blob = struct.pack("<4sIIIIBB", MAGIC, VERSION, vc, len(out_idx),
                       len(tex_bytes), tex_type, 0)  # 22-byte header, pad keeps alignment
    blob += struct.pack("<%df" % len(out_pos), *out_pos)
    blob += struct.pack("<%df" % len(nrm), *nrm)
    blob += struct.pack("<%df" % len(uv), *uv)
    blob += struct.pack("<%dH" % len(out_idx), *out_idx)
    blob += tex_bytes

    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, name + ".xmodel")
    with open(out_path, "wb") as f:
        f.write(blob)
    return {
        "file": os.path.basename(out_path),
        "source": path,
        "verts": vc,
        "tris": len(out_idx) // 3,
        "baked_scale": round(scale, 5),
        "height_world": round(h * scale, 4),
        "tex_kb": round(len(tex_bytes) / 1024),
        "out_kb": round(len(blob) / 1024),
    }


def main():
    src, dst = sys.argv[1], sys.argv[2]
    results = [convert(os.path.join(src, f), dst)
               for f in sorted(os.listdir(src)) if f.endswith(".glb")]
    with open(os.path.join(dst, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(results, f, indent=1, ensure_ascii=False)
    print(json.dumps(results, indent=1))


if __name__ == "__main__":
    main()
