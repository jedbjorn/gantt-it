#!/usr/bin/env python3
"""pack.py — host side. Zip a source folder for the VM (no .NET SDK on the host).

    python3 tools/vm/pack.py <src-dir> <out.zip> [--nuget-feed <dir-inside-src>] [--pkg id/version ...]

* Skips bin/, obj/, .vs/ and anything under a dot-directory.
* --pkg id/version downloads that package from nuget.org into <src-dir>/<feed> (cached; not
  committed) so the guest can restore offline from a nuget.config whose only source is that folder.
* The zip must land inside the repo or .sc-state/local/ for `./sc vm push` to accept it.

Example (the add-in, built on the VM by build-deploy.ps1; the VM restores from nuget.org):
    python3 tools/vm/pack.py . .sc-state/local/vm-stage/gantt-it-src.zip

The usual route is the CI artifact instead (no build on the VM): see deploy-artifact.ps1.
"""
import argparse
import os
import sys
import urllib.request
import zipfile

SKIP_DIRS = {"bin", "obj", ".vs"}


def fetch(feed: str, spec: str) -> None:
    pid, ver = spec.split("/")
    name = f"{pid.lower()}.{ver.lower()}.nupkg"
    dest = os.path.join(feed, name)
    if os.path.exists(dest):
        return
    url = f"https://api.nuget.org/v3-flatcontainer/{pid.lower()}/{ver.lower()}/{name}"
    os.makedirs(feed, exist_ok=True)
    print(f"download {url}")
    urllib.request.urlretrieve(url, dest)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--nuget-feed")
    ap.add_argument("--pkg", action="append", default=[])
    a = ap.parse_args()
    if a.pkg:
        if not a.nuget_feed:
            ap.error("--pkg needs --nuget-feed")
        for p in a.pkg:
            fetch(os.path.join(a.src, a.nuget_feed), p)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    n = 0
    with zipfile.ZipFile(a.out, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(a.src):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
            for f in files:
                full = os.path.join(root, f)
                z.write(full, os.path.relpath(full, a.src))
                n += 1
    print(f"wrote {a.out}: {n} files, {os.path.getsize(a.out)} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
