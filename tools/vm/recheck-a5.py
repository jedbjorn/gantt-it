#!/usr/bin/env python3
"""recheck-a5.py — host side. Re-check A5 off the VM from a Self-Test report.

    python3 tools/vm/recheck-a5.py <selftest.json> [...]

The report's `sections.geometry` holds, per chart state, the TextNote boxes and DetailCurve
endpoints read back from Revit plus the layout grid (feet, legend coordinates). This applies
the same three §6.3 rules as GanttIt.Core's GeometryChecker, independently:
  1. no text box intersects or touches a line;
  2. every line endpoint lies on a grid x and a grid y value (<= 1e-6 ft);
  3. no two collinear segments overlap (sharing an endpoint is fine).
Exits 0 when every chart state is clean.
"""
import bisect
import json
import sys

TOL = 1e-6


def on_grid(sorted_vals, v):
    i = bisect.bisect_left(sorted_vals, v)
    return any(0 <= j < len(sorted_vals) and abs(sorted_vals[j] - v) <= TOL for j in (i - 1, i))


def touches(seg, box):
    (x1, y1, x2, y2), (l, b, r, t) = seg, box
    # Lines are axis-aligned: compare the closed intervals.
    return min(x1, x2) <= r and max(x1, x2) >= l and min(y1, y2) <= t and max(y1, y2) >= b


def overlaps(lines):
    found = 0
    for vertical in (False, True):
        runs = []
        for x1, y1, x2, y2 in lines:
            if not vertical and abs(y1 - y2) <= TOL:
                runs.append((y1, min(x1, x2), max(x1, x2)))
            elif vertical and abs(x1 - x2) <= TOL and abs(y1 - y2) > TOL:
                runs.append((x1, min(y1, y2), max(y1, y2)))
        runs.sort()
        i = 0
        while i < len(runs):
            j = i
            while j < len(runs) and runs[j][0] - runs[i][0] <= TOL:
                j += 1
            reach = float("-inf")
            for _, a, b in sorted(runs[i:j], key=lambda r: r[1]):
                if a < reach - TOL:
                    found += 1
                reach = max(reach, b)
            i = j
    return found


def check(label, g):
    xs, ys = sorted(g["gridXs"]), sorted(g["gridYs"])
    lines, boxes = g["lines"], g["textBoxes"] + g.get("textBoxesFromCoord", [])
    crossing = sum(1 for s in lines for bx in boxes if touches(s, bx))
    off = sum(1 for x1, y1, x2, y2 in lines for x, y in ((x1, y1), (x2, y2)) if not (on_grid(xs, x) and on_grid(ys, y)))
    ov = overlaps(lines)
    ok = crossing == 0 and off == 0 and ov == 0 and lines and g["textBoxes"]
    print(f"{'PASS' if ok else 'FAIL'}  {label}: {len(g['textBoxes'])} boxes, {len(lines)} lines; "
          f"text-crosses-line {crossing}, endpoint-off-grid {off}, collinear-overlap {ov}")
    return bool(ok)


def main() -> int:
    all_ok = True
    for path in sys.argv[1:]:
        report = json.load(open(path, encoding="utf-8"))
        geometry = report.get("sections", {}).get("geometry", {})
        print(f"# {path}: {len(geometry)} chart states")
        if not geometry:
            all_ok = False
        for label, g in geometry.items():
            all_ok &= check(label, g)
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
