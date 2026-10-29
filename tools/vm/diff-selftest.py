#!/usr/bin/env python3
"""diff-selftest.py — host side. Compare two Self-Test reports (e.g. Revit 2026 vs 2027, A13).

    python3 tools/vm/diff-selftest.py <a.json> <b.json> [--tolerance-mm 0.0001] [--tolerance-ft 1e-6]

Checks are matched by name (renamed checks are mapped to their current name, see RENAMED). For each, the result and every number are compared. The report's
`sections` are compared too (per-string calibration rows, the per-element geometry in feet, the
settings). Anything that is an id, a path, a time, a version or an image file name is ignored,
since those differ between runs by design. Whole-model element counts are ignored as well:
Revit 2027 upgrades the model in memory, which changes them. Counts of generated ids are kept.
Exits 0 when the reports agree apart from ignored fields, 1 otherwise.
"""
import argparse
import json
import re
import sys

IGNORED_KEYS = {
    "at", "path", "document", "documentTitle", "revit", "revitYear", "runtime", "addinVersion",
    "elapsedMs", "timingsMs", "dialogs", "trigger", "request", "how", "images",
}


def ignored(key: str) -> bool:
    # Case-sensitive "Id" suffix (viewId, sheetId, viewportId): a lowercase "id" ending
    # would also drop keys like endpointOffGrid.
    k = key.lower()
    return key in IGNORED_KEYS or key == "id" or key.endswith("Id") or "uniqueid" in k or k.startswith("elements")


def flatten(node, prefix=""):
    if isinstance(node, dict):
        for k, v in node.items():
            if ignored(k):
                continue
            yield from flatten(v, f"{prefix}.{k}" if prefix else k)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from flatten(v, f"{prefix}[{i}]")
    else:
        yield prefix, node


def compare(prefix, na, nb, tolerance, diffs):
    for key in sorted(set(na) | set(nb)):
        va, vb = na.get(key), nb.get(key)
        if isinstance(va, (int, float)) and isinstance(vb, (int, float)) and not isinstance(va, bool):
            if abs(va - vb) > tolerance:
                diffs.append(f"{prefix} :: {key}: {va} vs {vb}")
        elif va != vb:
            diffs.append(f"{prefix} :: {key}: {va!r} vs {vb!r}")


# Check names that were reworded, old -> current, so reports from before the rename still
# line up with new ones. PR #9: the A8 viewport check named "both" whatever the sheet
# count (the default place-sheets count, 2, is assumed for old reports), and
# "placed on 1 sheets" lost its plural.
RENAMED = [
    (re.compile(r"^a8-check: both viewports are gone$"), "a8-check: all 2 recorded viewports are gone"),
    (re.compile(r"^(place-sheets: '.*' placed on 1) sheets$"), r"\1 sheet"),
]


def current_name(name: str) -> str:
    for pattern, replacement in RENAMED:
        name = pattern.sub(replacement, name)
    return name


def checks(report):
    return {current_name(c["name"]): c for c in report.get("checks", [])}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--tolerance-mm", type=float, default=1e-4)
    ap.add_argument("--tolerance-ft", type=float, default=1e-6, help="for sections.geometry (feet)")
    args = ap.parse_args()
    a = json.load(open(args.a, encoding="utf-8"))
    b = json.load(open(args.b, encoding="utf-8"))
    ca, cb = checks(a), checks(b)
    diffs = []
    for name in sorted(set(ca) | set(cb)):
        if name not in ca or name not in cb:
            diffs.append(f"only in {'b' if name in cb else 'a'}: {name}")
            continue
        if ca[name]["result"] != cb[name]["result"]:
            diffs.append(f"result {ca[name]['result']} vs {cb[name]['result']}: {name}")
        compare(name, dict(flatten(ca[name].get("numbers", {}))), dict(flatten(cb[name].get("numbers", {}))),
                args.tolerance_mm, diffs)

    sa, sb = a.get("sections", {}), b.get("sections", {})
    for section in sorted(set(sa) | set(sb)):
        if ignored(section):
            continue
        tolerance = args.tolerance_ft if section == "geometry" else args.tolerance_mm
        compare(f"sections.{section}", dict(flatten(sa.get(section))), dict(flatten(sb.get(section))), tolerance, diffs)

    print(f"a: {args.a} overall={a.get('overall')} {a.get('summary', {}).get('passed')} passed")
    print(f"b: {args.b} overall={b.get('overall')} {b.get('summary', {}).get('passed')} passed")
    print(f"checks compared: {len(set(ca) & set(cb))}; sections compared: {len(set(sa) & set(sb))}; "
          f"differences (ids, paths, times ignored): {len(diffs)}")
    for d in diffs:
        print("  " + d)
    return 0 if not diffs else 1


if __name__ == "__main__":
    sys.exit(main())
