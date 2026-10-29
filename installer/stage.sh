#!/usr/bin/env bash
# Stage a built add-in for the MSI (and the manual-install zips).
#
#   installer/stage.sh <year> [configuration]     e.g. installer/stage.sh 2026
#
# Copies the GanttIt.Revit build output of "Release R26" / "Release R27" (or the
# configuration given) into installer/stage/<year>/, laid out exactly like
# %AppData%\Autodesk\Revit\Addins\<year>\ — the same layout as build.yml's
# "Stage add-in" step for the Debug artifacts:
#
#   installer/stage/<year>/GanttIt.addin
#   installer/stage/<year>/GanttIt/GanttIt.Revit.dll, GanttIt.Core.dll, …,
#                                  Resources/icons/…
#
# Product.wxs harvests installer/stage/2026/** and installer/stage/2027/**.
# Run `dotnet build GanttIt.sln -c "Release R26"` (and R27) first. Works in Git
# Bash on Windows and on Linux. Re-running replaces the year's stage folder.

set -euo pipefail

year="${1:?usage: installer/stage.sh <2026|2027> [configuration]}"
case "$year" in
  2026) tfm=net8.0-windows;  suffix=R26 ;;
  2027) tfm=net10.0-windows; suffix=R27 ;;
  *) echo "stage.sh: unsupported Revit year '$year'" >&2; exit 2 ;;
esac
config="${2:-Release $suffix}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/src/GanttIt.Revit/bin/$config/$tfm"
dest="$root/installer/stage/$year"

if [[ ! -f "$out/GanttIt.Revit.dll" || ! -f "$out/GanttIt.addin" ]]; then
  echo "stage.sh: no build output in '$out' — build \"$config\" first" >&2
  exit 1
fi

rm -rf "$dest"
mkdir -p "$dest/GanttIt"
cp "$out/GanttIt.addin" "$dest/"
cp -r "$out/." "$dest/GanttIt/"
rm "$dest/GanttIt/GanttIt.addin"

echo "staged $config -> installer/stage/$year"
(cd "$dest" && find . -type f | sort)
