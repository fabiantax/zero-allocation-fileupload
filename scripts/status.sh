#!/usr/bin/env bash
# Live view of delegated work. Run: watch -n5 scripts/status.sh
# Deliberately avoids ps/pgrep: process introspection is blocked by the sandbox, and
# "a process exists" never answered the real question anyway. Log growth does.
cd "$(dirname "$0")/.." || exit 1
printf '\n=== %s ===\n' "$(date '+%H:%M:%S')"
now=$(date +%s)

printf '\n-- delegated jobs --\n'
shopt -s nullglob
found=0
for f in /tmp/claude/task*.log /tmp/claude/spike/*.log; do
  found=1
  base=$(basename "$f" .log)
  res="/tmp/claude/${base}.result.md"
  age=$(( now - $(stat -f %m "$f" 2>/dev/null || echo "$now") ))
  size=$(wc -c <"$f" | tr -d ' ')
  if [ -f "$res" ]; then            state="DONE   (result file present)"
  elif [ "$age" -lt 90 ];  then     state="WORKING (wrote ${age}s ago)"
  elif [ "$age" -lt 300 ]; then     state="QUIET   (${age}s — long model turn, or dying)"
  else                              state="DEAD    (${age}s silent) -> relaunch via: codex exec ... - < prompt"
  fi
  printf '  %-22s %9sB  %s\n' "$base" "$size" "$state"
done
[ "$found" = 0 ] && printf '  no jobs\n'

printf '\n-- git --\n'
printf '  branch      : %s\n' "$(git rev-parse --abbrev-ref HEAD)"
printf '  uncommitted : %s file(s)\n' "$(git status --porcelain | wc -l | tr -d ' ')"
printf '  head        : %s\n' "$(git log --oneline -1)"

printf '\n-- github --\n'
gh pr list --limit 5 --json number,title,mergeStateStatus \
  --jq '.[] | "  PR #\(.number) \(.mergeStateStatus)  \(.title)"' 2>/dev/null || printf '  (gh unavailable)\n'
open=$(gh issue list --limit 30 --json number --jq 'length' 2>/dev/null) && printf '  open issues : %s\n' "$open"
printf '\n'
