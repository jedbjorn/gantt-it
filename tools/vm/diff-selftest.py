#!/usr/bin/env python3
"""diff-selftest.py — host side. Compare two Self-Test reports (e.g. Revit 2026 vs 2027, A13).

    python3 tools/vm/diff-selftest.py <a.json> <b.json> [--tolerance-mm 0.0001]

Checks are matched by name. For each, the result and every number are compared; anything that
is an id, a path, a time or a version is ignored, since those differ between runs by design.
Exits 0 when the reports agree apart from ignored fields, 1 otherwise.
"""
import argparse
import json
import sys

IGNORED_KEYS = {
    "at", "path", "document", "documentTitle", "revit", "revitYear", "runtime", "addinVersion",
    "elapsedMs", "timingsMs", "dialogs", "trigger", "request", "how",
}


def ignored(key: str) -> bool:
    k = key.lower()
    # Element ids and whole-model element counts differ between years by design (2027 upgrades
    # the model in memory); counts of generated ids are kept.
    return key in IGNORED_KEYS or k == "id" or "uniqueid" in k or k.startswith("elements")


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


def checks(report):
    return {c["name"]: c for c in report.get("checks", [])}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--tolerance-mm", type=float, default=1e-4)
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
        na = dict(flatten(ca[name].get("numbers", {})))
        nb = dict(flatten(cb[name].get("numbers", {})))
        for key in sorted(set(na) | set(nb)):
            va, vb = na.get(key), nb.get(key)
            if isinstance(va, (int, float)) and isinstance(vb, (int, float)) and not isinstance(va, bool):
                if abs(va - vb) > args.tolerance_mm:
                    diffs.append(f"{name} :: {key}: {va} vs {vb}")
            elif va != vb:
                diffs.append(f"{name} :: {key}: {va!r} vs {vb!r}")
    print(f"a: {args.a} overall={a.get('overall')} {a.get('summary', {}).get('passed')} passed")
    print(f"b: {args.b} overall={b.get('overall')} {b.get('summary', {}).get('passed')} passed")
    print(f"checks compared: {len(set(ca) & set(cb))}; differences (ids, paths, times ignored): {len(diffs)}")
    for d in diffs:
        print("  " + d)
    return 0 if not diffs else 1


if __name__ == "__main__":
    sys.exit(main())
