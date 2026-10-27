#!/usr/bin/env bash
# CI only: runs a command and, if it fails, repeats its errors and last lines as a GitHub annotation.
# Job logs need a GitHub sign-in; annotations are public, so a failure can be read without one.
# Usage: tools/ci-annotate.sh <title> <command> [args...]
set -uo pipefail

title="$1"
shift
log="$(mktemp)"
trap 'rm -f "$log"' EXIT

"$@" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
if [ "$status" -ne 0 ]; then
    # %, CR and LF are escaped as the workflow-command syntax requires (one annotation, many lines).
    summary="$({ grep -E 'error:|Error [0-9]+|ERROR|Falta|No se' "$log" | head -n 15; echo '--- últimas líneas ---'; tail -n 25 "$log"; } \
        | sed -e 's/%/%25/g' -e 's/\r/%0D/g' | awk '{ printf "%s%%0A", $0 }')"
    echo "::error title=$title::$summary"
fi
exit "$status"
