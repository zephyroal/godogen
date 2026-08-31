"""Scrub vendor metadata from generated 3D/image assets.

GLB: removes asset.extras and scenes[].extras (where generation pipelines
stamp vendor metadata), rebuilding chunk lengths; every other byte is kept
(JSON is re-serialized compactly; numeric values round-trip losslessly).

JPEG: drops watermark comment segments (COM with "MetaWatermark" payload)
and provenance boxes (APP11 with JUMBF / content-credentials payload).

Usage:
    python scrub_glb.py [dir_or_file]

Defaults to ../assets/glb relative to this script. Idempotent: files without
such metadata are not rewritten. Prints a JSON summary to stdout.
"""

import json
import os
import struct
import sys

COM_WATERMARK = b"MetaWatermark"  # COM segment payload prefix
JUMBF_APP11 = b"JP\x02\x11"       # APP11 provenance box payload prefix


def scrub_glb_bytes(data):
    """Return (new_bytes, removed_count) or (None, 0) if unparseable."""
    magic, version, _total = struct.unpack("<4sII", data[:12])
    if magic != b"glTF":
        return None, 0
    json_len, json_type = struct.unpack("<I4s", data[12:20])
    if json_type != b"JSON" or 20 + json_len > len(data):
        return None, 0
    js = data[20:20 + json_len]

    # BIN chunk (if any) starts at the next 4-byte boundary; JSON is padded with spaces
    bin_start = 20 + json_len
    while bin_start % 4:
        if data[bin_start] != 0x20:
            return None, 0
        bin_start += 1
    tail = data[bin_start:]
    if tail and tail[4:8] != b"BIN\x00":
        return None, 0

    obj = json.loads(js.decode("utf-8"))
    removed = 0
    if "extras" in obj.get("asset", {}):
        del obj["asset"]["extras"]
        removed += 1
    for scene in obj.get("scenes", []):
        if isinstance(scene, dict) and "extras" in scene:
            del scene["extras"]
            removed += 1
    if not removed:
        return data, 0

    new_js = json.dumps(obj, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    pad = (4 - len(new_js) % 4) % 4
    payload = new_js + b" " * pad
    new_total = 12 + 8 + len(payload) + len(tail)
    out = (struct.pack("<4sII", b"glTF", version, new_total)
           + struct.pack("<I4s", len(payload), b"JSON") + payload + tail)

    # verify the rebuilt file before returning
    assert struct.unpack("<I", out[8:12])[0] == new_total == len(out)
    v_len, v_type = struct.unpack("<I4s", out[12:20])
    assert v_type == b"JSON"
    v_obj = json.loads(out[20:20 + v_len].decode("utf-8"))
    assert "extras" not in v_obj.get("asset", {})
    assert all("extras" not in s for s in v_obj.get("scenes", []))
    assert out[20 + v_len:] == tail
    return out, removed


def scrub_jpeg_bytes(data):
    """Return (new_bytes, removed_count) or (None, 0) if unparseable."""
    if data[:2] != b"\xff\xd8":
        return None, 0
    out = bytearray(data[:2])
    pos = 2
    removed = 0
    n = len(data)
    while pos < n:
        if data[pos] != 0xFF:
            return None, 0
        end = pos + 1
        while end < n and data[end] == 0xFF:  # skip fill bytes
            end += 1
        if end >= n:
            out += data[pos:]
            break
        marker = data[end]
        seg_start = end + 1
        if marker == 0x01 or 0xD0 <= marker <= 0xD7:  # standalone markers
            out += data[pos:seg_start]
            pos = seg_start
            continue
        if seg_start + 2 > n:
            out += data[pos:]
            break
        seg_len = (data[seg_start] << 8) | data[seg_start + 1]
        seg_end = seg_start + seg_len
        if seg_end > n:
            return None, 0
        payload = data[seg_start + 2:seg_end]
        if marker == 0xFE and payload.startswith(COM_WATERMARK):
            removed += 1  # drop watermark comment segment
        elif marker == 0xEB and payload.startswith(JUMBF_APP11):
            removed += 1  # drop provenance box
        else:
            out += data[pos:seg_end]
        pos = seg_end
        if marker == 0xDA:  # SOS: entropy-coded data follows to EOI
            out += data[pos:]
            break
    result = bytes(out)
    if COM_WATERMARK in result or JUMBF_APP11 in result:
        return None, 0
    return result, removed


def scrub(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] == b"glTF":
        out, removed = scrub_glb_bytes(data)
        kind = "glb"
    elif data[:2] == b"\xff\xd8":
        out, removed = scrub_jpeg_bytes(data)
        kind = "jpeg"
    else:
        return {"path": path, "ok": False, "error": "unsupported format"}
    if out is None:
        return {"path": path, "ok": False, "error": "could not parse %s" % kind}
    if not removed:
        return {"path": path, "ok": True, "status": "clean", "removed": 0}
    with open(path, "wb") as f:
        f.write(out)
    return {"path": path, "ok": True, "status": "scrubbed", "kind": kind, "removed": removed,
            "old_total": len(data), "new_total": len(out)}


def main():
    default_dir = os.path.normpath(os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "assets", "glb"))
    target = sys.argv[1] if len(sys.argv) > 1 else default_dir
    results = []
    if os.path.isfile(target):
        results.append(scrub(target))
    elif os.path.isdir(target):
        for name in sorted(os.listdir(target)):
            if name.lower().endswith((".glb", ".jpg", ".jpeg")):
                results.append(scrub(os.path.join(target, name)))
        if not results:
            results.append({"path": target, "ok": False, "error": "no supported files found"})
    else:
        results.append({"path": target, "ok": False, "error": "no such file or directory"})
    print(json.dumps(results, ensure_ascii=False, indent=1))
    if not all(r.get("ok") for r in results):
        sys.exit(1)


if __name__ == "__main__":
    main()
