"""Tidies a glb exported from a Blender session that holds other characters: keeps only the scene that
has nodes, makes it the default, and drops animations whose name starts with a skipped prefix.

python tidy_glb.py in.glb out.glb [skip_prefix ...]
"""
import json, struct, sys

src, dst, skip = sys.argv[1], sys.argv[2], tuple(sys.argv[3:])
data = open(src, "rb").read()
jlen = struct.unpack("<I", data[12:16])[0]
j = json.loads(data[20:20 + jlen])
rest = data[20 + jlen:]

scenes = [s for s in j["scenes"] if s.get("nodes")]
j["scenes"] = scenes
j["scene"] = 0
if skip and "animations" in j:
    j["animations"] = [a for a in j["animations"] if not a["name"].startswith(skip)]
    if not j["animations"]:
        del j["animations"]

body = json.dumps(j, separators=(",", ":")).encode()
body += b" " * (-len(body) % 4)
out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(body) + len(rest)) + struct.pack("<II", len(body), 0x4E4F534A) + body + rest
open(dst, "wb").write(out)
print(dst, len(out), "scenes", len(scenes), "animations", [a["name"] for a in j.get("animations", [])])
