"""Snapshot complete room build outputs and compare repeated runs."""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent
folders = ["Assets/MagicCampus/CampusGrid/Rooms/room-dorm",
           "Art/CampusGrid/prepared/room-dorm",
           "Art/CampusGrid/review/room-dorm"]
current = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
           for folder in folders for p in sorted((ROOT / folder).rglob("*")) if p.is_file()}
if sys.argv[1] == "snapshot":
    (RUN / "first-final-hashes.json").write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
    print(f"Snapshot {len(current)} files.")
else:
    first = json.loads((RUN / "first-final-hashes.json").read_text(encoding="utf-8"))
    differences = [p for p in sorted(set(first) | set(current)) if first.get(p) != current.get(p)]
    result = {"identical": not differences, "fileCount": len(current), "differences": differences,
              "first": first, "second": current}
    (RUN / "reproducibility.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    assert not differences, differences
    print(f"PASS: {len(current)} files byte-identical, including prepared art, runtime outputs and reviews.")
