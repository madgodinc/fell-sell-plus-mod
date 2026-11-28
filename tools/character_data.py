"""Builds the data files embedded in Fell & Sell Character from the game's own Sidekick data.

Inputs (not in the repo, they are game assets), by default in tools/assets/out/sidekick:
  Side_Kick_Data.db     the TextAsset Resources/Database/Side_Kick_Data, saved as a file (SQLite)
  resources_paths.json  [[resources path, type, object name], ...] from the ResourceManager of globalgamemanagers
Outputs: src/FellSellCharacter/data/{parts,colors,rig}.txt
"""
import json, re, sqlite3, sys, os

src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "assets", "out", "sidekick")
out = os.path.join(os.path.dirname(__file__), "..", "src", "FellSellCharacter", "data")
os.makedirs(out, exist_ok=True)

# Parts: every SK_* prefab that Resources.Load can reach, except the base model's own meshes.
paths = json.load(open(os.path.join(src, "resources_paths.json")))
rx = re.compile(r"^SK_[A-Z]+_[A-Z]+_\d+_(\d\d)([A-Z]{4})_[A-Z]{2}\d\d$")
parts = []
for path, kind, name in paths:
    m = rx.match(name or "")
    if kind != "GameObject" or not m or not path.startswith("meshes/"):
        continue
    parts.append((int(m.group(1)), name, path.rsplit("/", 1)[0] + "/" + name))
parts.sort()
with open(os.path.join(out, "parts.txt"), "w", newline="\n") as f:
    for slot, name, path in parts:
        f.write(f"{slot}\t{path}\n")

db = sqlite3.connect(os.path.join(src, "Side_Kick_Data.db"))
with open(os.path.join(out, "colors.txt"), "w", newline="\n") as f:
    # S <id> <group> <u> <v> <name>: a named swatch cell of the 32x32 colour map (one cell is 2x2 pixels).
    for i, group, name, u, v in db.execute("select id,color_group,name,u,v from sk_color_property order by id"):
        f.write(f"S\t{i}\t{group}\t{u}\t{v}\t{name}\n")
    # P <group> <name> <id=RRGGBB,...>: Synty's colour presets for humans (1 skin, 2 outfit, 3 attachments, 4 materials).
    for pid, group, name in db.execute("select id,color_group,name from sk_color_preset where ptr_species=1 and color_group in (1,2,3,4) order by color_group,name"):
        rows = db.execute("select ptr_color_property,color from sk_color_preset_row where ptr_color_preset=? order by ptr_color_property", (pid,)).fetchall()
        f.write(f"P\t{group}\t{name}\t" + ",".join(f"{p}={c}" for p, c in rows) + "\n")

with open(os.path.join(out, "rig.txt"), "w", newline="\n") as f:
    # <part type> <blend type> <offset xyz> <rotation xyz>: how far an attachment bone moves at full blend.
    for r in db.execute("select part_type,blend_type,max_offset_x,max_offset_y,max_offset_z,max_rotation_x,max_rotation_y,max_rotation_z from sk_blend_shape_rig_movement order by part_type,blend_type"):
        f.write("\t".join(f"{x:g}" if isinstance(x, float) else str(x) for x in r) + "\n")
print(len(parts), "parts")
