#!/usr/bin/env python3
"""Fail when a Revit API package is pinned above the supported floor.

    python3 tools/check_revit_api_floor.py   (CI runs it in _build.yml)

Gantt-It supports every update of Revit 2025, 2026 and 2027, so it compiles
against each year's earliest update. A reference to a later update would let the
code use API that older updates lack, which fails only at run time on those updates
(TypeLoadException / MissingMethodException).

Checks every tracked *.csproj, *.props and *.targets for Nice3point.Revit.Api.*
PackageReference or PackageVersion items. Fails when a Version or VersionOverride is
above FLOOR for its Revit year, when an item has no version, or when a year has no
floor here.

Raising the floor is deliberate: change FLOOR below and Directory.Packages.props in
the same PR that needs the newer API, and update the Compatibility section of
README.md with the new minimum update.
"""

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# The earliest supported update per Revit year (Nice3point.Revit.Api package versions).
FLOOR = {
    2025: (2025, 0, 2),
    2026: (2026, 0, 4),
    2027: (2027, 0, 0),
}

ITEM = re.compile(r"<(PackageReference|PackageVersion)\b([^>]*)>", re.S)
ATTR = re.compile(r'\b(Include|Update|Version|VersionOverride)\s*=\s*"([^"]*)"')


def parse(version):
    parts = version.strip().split("-")[0].split(".")
    if not all(p.isdigit() for p in parts):
        return None
    return tuple(int(p) for p in parts) + (0,) * (3 - len(parts))


def main():
    files = subprocess.check_output(
        ["git", "ls-files", "*.csproj", "*.props", "*.targets"], cwd=ROOT, text=True
    ).split()

    problems, checked = [], 0
    for rel in files:
        text = (ROOT / rel).read_text(encoding="utf-8-sig")
        for m in ITEM.finditer(text):
            attrs = dict(ATTR.findall(m.group(2)))
            package = attrs.get("Include") or attrs.get("Update") or ""
            if not package.startswith("Nice3point.Revit.Api."):
                continue
            versions = [(k, attrs[k]) for k in ("Version", "VersionOverride") if k in attrs]
            if m.group(1) == "PackageReference" and not versions:
                continue  # versioned centrally by a PackageVersion item, checked there
            where = f"{rel}:{text.count(chr(10), 0, m.start()) + 1} {package}"
            if not versions:
                problems.append(f"{where}: no Version")
                continue
            for key, value in versions:
                checked += 1
                v = parse(value)
                if v is None:
                    problems.append(f'{where}: {key}="{value}" is not a plain version')
                    continue
                floor = FLOOR.get(v[0])
                if floor is None:
                    problems.append(f'{where}: {key}="{value}": no floor for Revit {v[0]} in tools/check_revit_api_floor.py')
                elif v > floor:
                    problems.append(
                        f'{where}: {key}="{value}" is above the Revit {v[0]} floor {".".join(map(str, floor))}'
                    )

    if problems:
        print("Revit API floor check FAILED:")
        for p in problems:
            print("  " + p)
        print("To raise the floor on purpose, edit FLOOR in tools/check_revit_api_floor.py"
              " and the README's Compatibility section in the same PR.")
        return 1
    if checked == 0:
        print("Revit API floor check FAILED: no Nice3point.Revit.Api versions found (is the pattern stale?)")
        return 1
    floors = ", ".join(".".join(map(str, f)) for f in FLOOR.values())
    print(f"Revit API floor check passed: {checked} versions across {len(files)} project files,"
          f" all at or below {floors}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
