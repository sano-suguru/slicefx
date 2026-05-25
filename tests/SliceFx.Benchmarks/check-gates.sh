#!/usr/bin/env bash
# Compares BenchmarkDotNet JSON results against gates.json and exits non-zero on regression.
# Used by .github/workflows/perf.yml to close the perf-regression detection loop.

set -euo pipefail

GATES_FILE="${1:-tests/SliceFx.Benchmarks/gates.json}"
RESULTS_DIR="${2:-BenchmarkDotNet.Artifacts/results}"

if [ ! -f "$GATES_FILE" ]; then
  echo "::error::Gates file not found: $GATES_FILE" >&2
  exit 2
fi

if [ ! -d "$RESULTS_DIR" ]; then
  echo "::error::Results directory not found: $RESULTS_DIR" >&2
  exit 2
fi

mapfile -t reports < <(find "$RESULTS_DIR" \( -name '*-report-full-compressed.json' -o -name '*-report-full.json' \) -type f | sort)
if [ "${#reports[@]}" -eq 0 ]; then
  echo "::error::No BenchmarkDotNet JSON report found under $RESULTS_DIR" >&2
  exit 2
fi

if [ "${#reports[@]}" -gt 1 ]; then
  echo "::error::Multiple BenchmarkDotNet JSON reports found under $RESULTS_DIR; expected exactly one." >&2
  printf '  %s\n' "${reports[@]}" >&2
  exit 2
fi

REPORT_JSON="${reports[0]}"

echo "Gates: $GATES_FILE"
echo "Report: $REPORT_JSON"
echo ""

violations=0
gate_count=$(jq '.gates | length' "$GATES_FILE")

is_number() {
  [[ "$1" =~ ^-?[0-9]+([.][0-9]+)?([eE][-+]?[0-9]+)?$ ]]
}

for ((i = 0; i < gate_count; i++)); do
  method=$(jq -r ".gates[$i].method" "$GATES_FILE")
  feature_count=$(jq -r ".gates[$i].featureCount" "$GATES_FILE")
  max_mean_ms=$(jq -r ".gates[$i].maxMeanMs" "$GATES_FILE")
  max_alloc_mb=$(jq -r ".gates[$i].maxAllocatedMB" "$GATES_FILE")

  if [ -z "$method" ] || ! is_number "$feature_count" || ! is_number "$max_mean_ms" || ! is_number "$max_alloc_mb"; then
    echo "::error::Invalid gate configuration at index $i" >&2
    violations=$((violations + 1))
    continue
  fi

  match_count=$(jq -r --arg m "$method" --argjson fc "$feature_count" '
    [
      .Benchmarks[]
      | select(.Method == $m and (.Parameters | test("FeatureCount=" + ($fc | tostring) + "$")))
    ]
    | length
  ' "$REPORT_JSON")

  if ! [[ "$match_count" =~ ^[0-9]+$ ]] || [ "$match_count" -ne 1 ]; then
    echo "::error::Expected exactly one measurement for $method(FeatureCount=$feature_count), found $match_count" >&2
    violations=$((violations + 1))
    continue
  fi

  mean_ns=$(jq -r --arg m "$method" --argjson fc "$feature_count" '
    .Benchmarks[]
    | select(.Method == $m and (.Parameters | test("FeatureCount=" + ($fc | tostring) + "$")))
    | (.Statistics.Mean // empty)
  ' "$REPORT_JSON")

  alloc_bytes=$(jq -r --arg m "$method" --argjson fc "$feature_count" '
    .Benchmarks[]
    | select(.Method == $m and (.Parameters | test("FeatureCount=" + ($fc | tostring) + "$")))
    | (.Memory.BytesAllocatedPerOperation // empty)
  ' "$REPORT_JSON")

  if ! is_number "$mean_ns" || ! is_number "$alloc_bytes"; then
    echo "::error::Invalid measurement for $method(FeatureCount=$feature_count): mean='$mean_ns', allocated='$alloc_bytes'" >&2
    violations=$((violations + 1))
    continue
  fi

  mean_ms=$(awk -v ns="$mean_ns" 'BEGIN {printf "%.3f", ns / 1000000}')
  alloc_mb=$(awk -v bytes="$alloc_bytes" 'BEGIN {printf "%.3f", bytes / 1048576}')

  mean_ok=$(awk -v actual="$mean_ms" -v gate="$max_mean_ms" 'BEGIN {print (actual <= gate) ? 1 : 0}')
  alloc_ok=$(awk -v actual="$alloc_mb" -v gate="$max_alloc_mb" 'BEGIN {print (actual <= gate) ? 1 : 0}')

  status="ok"
  if [ "$mean_ok" -eq 0 ] || [ "$alloc_ok" -eq 0 ]; then
    status="FAIL"
    violations=$((violations + 1))
  fi

  printf "  %-18s F=%-3d  Mean=%7.3f ms (gate %6.2f)  Alloc=%7.3f MB (gate %5.2f)  [%s]\n" \
    "$method" "$feature_count" "$mean_ms" "$max_mean_ms" "$alloc_mb" "$max_alloc_mb" "$status"
done

echo ""
if [ "$violations" -gt 0 ]; then
  echo "::error::Performance regression: $violations gate(s) breached. See errors and lines marked [FAIL] above." >&2
  exit 1
fi

echo "All gates passed."
