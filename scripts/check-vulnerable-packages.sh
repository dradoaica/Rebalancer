#!/usr/bin/env bash
# ADR-0035: fails when any project of the solution uses a package, direct or transitive, with a known vulnerability of
# severity High or Critical. Lower severities are listed, not fatal. Needs a restored solution, network access to the
# package sources, and jq.
set -euo pipefail

report="$(dotnet list "${1:-Rebalancer.slnx}" package --vulnerable --include-transitive --format json)"
findings="$(jq -r '
  .projects[] | .path as $project | .frameworks[]? | (.topLevelPackages[]?, .transitivePackages[]?)
  | .id as $package | .resolvedVersion as $version | .vulnerabilities[]?
  | "\(.severity)\t\($package) \($version)\t\(.advisoryurl)\t\($project)"' <<<"$report")"

if [[ -z "$findings" ]]; then
  echo "No vulnerable packages."
  exit 0
fi

echo "$findings"
if grep -qE '^(High|Critical)'$'\t' <<<"$findings"; then
  echo "Vulnerable packages of severity High or Critical found (ADR-0035)." >&2
  exit 1
fi
