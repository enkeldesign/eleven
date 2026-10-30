#!/usr/bin/env bash
# Loop measurements for the engine spike's K1 and K2 (#4), from the GitHub API.
#
#   unity/ci/loop-metrics.sh <branch> [owner/repo]
#
# One line per "Unity host" workflow run on the branch: when it started, whether the build
# succeeded, how long the build job took, and push to link as reported by the deploy job's
# annotation, counted only when the link answered HTTP 200. Then medians and the build success rate. Needs gh, authenticated.
set -euo pipefail

branch=${1:?usage: loop-metrics.sh <branch> [owner/repo]}
repo=${2:-enkeldesign/eleven}

runs=$(gh api "repos/$repo/actions/workflows/unity.yml/runs?branch=$branch&per_page=50" \
  --jq '.workflow_runs[] | "\(.id) \(.created_at) \(.head_sha[0:7])"')

printf '%-12s %-20s %-8s %-8s %10s %14s\n' run created head build build_s push_to_link_s
builds=() links=() ok=0 total=0
while read -r id created head; do
  [ -n "$id" ] || continue
  jobs=$(gh api "repos/$repo/actions/runs/$id/jobs?filter=latest" \
    --jq '.jobs[] | "\(.name)|\(.conclusion)|\(.started_at)|\(.completed_at)|\(.id)"')
  build=$(echo "$jobs" | grep '^Build Web player|' || true)
  deploy=$(echo "$jobs" | grep '^Deploy playable link|' || true)
  [ -n "$build" ] || continue

  IFS='|' read -r _ conclusion started completed _ <<< "$build"
  case "$conclusion" in cancelled|null|"") continue ;; esac
  total=$((total + 1))
  seconds=-
  if [ "$conclusion" = "success" ]; then
    ok=$((ok + 1))
    seconds=$(( $(date -d "$completed" +%s) - $(date -d "$started" +%s) ))
    builds+=("$seconds")
  fi

  link=-
  if [ -n "$deploy" ]; then
    deploy_id=${deploy##*|}
    link=$(gh api "repos/$repo/check-runs/$deploy_id/annotations" \
      --jq '.[] | select(.title == "Playable link") | .message' | grep '(HTTP 200)' | grep -o 'push to link [0-9]*' | grep -o '[0-9]*$' || echo -)
    [ "$link" != "-" ] && links+=("$link")
  fi
  printf '%-12s %-20s %-8s %-8s %10s %14s\n' "$id" "$created" "$head" "$conclusion" "$seconds" "$link"
done <<< "$runs"

median() { printf '%s\n' "$@" | sort -n | awk '{ a[NR] = $1 } END { if (NR == 0) print "-"; else if (NR % 2) print a[(NR + 1) / 2]; else print (a[NR / 2] + a[NR / 2 + 1]) / 2 }'; }
echo
echo "builds: $ok of $total succeeded"
echo "median build job: $(median "${builds[@]}") s over ${#builds[@]} builds"
echo "median push to link: $(median "${links[@]}") s over ${#links[@]} deploys"
