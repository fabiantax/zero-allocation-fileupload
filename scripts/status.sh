#!/usr/bin/env bash
# What is the agent actually doing right now? Run: watch -n5 scripts/status.sh
cd "$(dirname "$0")/.." || exit 1
printf '\n=== %s ===\n' "$(date '+%H:%M:%S')"

printf '\n-- codex processes --\n'
if ps aux | grep -q "[c]odex exec"; then
  ps aux | grep "[c]odex exec" | awk '{printf "  RUNNING pid=%s  cpu=%s%%  elapsed=%s\n", $2, $3, $10}'
else
  printf '  none running\n'
fi

printf '\n-- job logs (size + last write) --\n'
shopt -s nullglob
for f in /tmp/claude/*.log /tmp/claude/spike/*.log; do
  printf '  %-28s %8sB  %s\n' "$(basename "$f")" "$(wc -c <"$f" | tr -d ' ')" \
    "$(date -r "$f" '+%H:%M:%S')"
done
[ -z "$(echo /tmp/claude/*.log)" ] && printf '  no logs yet\n'

printf '\n-- git --\n'
printf '  branch: %s\n' "$(git rev-parse --abbrev-ref HEAD)"
printf '  uncommitted: %s file(s)\n' "$(git status --porcelain | wc -l | tr -d ' ')"
printf '  last commit: %s\n' "$(git log --oneline -1)"

printf '\n-- open PRs --\n'
gh pr list --limit 5 --json number,title,mergeStateStatus \
  --jq '.[] | "  #\(.number) \(.mergeStateStatus)  \(.title)"' 2>/dev/null || printf '  (gh unavailable)\n'
printf '\n'
