#!/usr/bin/env python3
"""Render SliceFx runtime BenchmarkDotNet results as SVG charts.

Produces two subplots:
  - top:    startup-family  (BuildHostOnly, Startup)   — hundreds of ms
  - bottom: per-request-family (Request_SimpleGet, Request_PostWithValidation) — sub-ms

Run from the repository root:
    python3 tests/SliceFx.Benchmarks/render-runtime-chart.py \\
        --results-dir BenchmarkDotNet.Artifacts.Runtime/results \\
        --gates tests/SliceFx.Benchmarks.Runtime/runtime-gates.json \\
        --output docs/perf/runtime-latest.svg \\
        --history-dir docs/perf/runtime-history
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

os.environ.setdefault("SOURCE_DATE_EPOCH", "0")

import matplotlib

matplotlib.use("Agg")
matplotlib.rcParams["svg.fonttype"] = "none"
matplotlib.rcParams["svg.hashsalt"] = "slice-runtime-perf"

import matplotlib.pyplot as plt

# Methods plotted in the startup subplot (y-axis: ms, ~100–400 ms range)
STARTUP_METHODS = ("BuildHostOnly", "Startup")
# Methods plotted in the request subplot (y-axis: ms, sub-ms range)
REQUEST_METHODS = ("Request_SimpleGet", "Request_PostWithValidation")
ALL_METHODS = STARTUP_METHODS + REQUEST_METHODS

COLORS = {
    "BuildHostOnly": "#16a34a",
    "Startup": "#2563eb",
    "Request_SimpleGet": "#dc2626",
    "Request_PostWithValidation": "#ea580c",
}

DISPLAY_NAMES = {
    "BuildHostOnly": "BuildHostOnly",
    "Startup": "Startup",
    "Request_SimpleGet": "SimpleGet",
    "Request_PostWithValidation": "PostValidation",
}


@dataclass(frozen=True)
class BenchmarkPoint:
    method: str
    feature_count: int
    mean_ms: float
    gate_ms: float


def main() -> int:
    args = parse_args()

    try:
        report_path = find_report(args.results_dir)
        report = load_json(report_path)
        gates = load_json(args.gates)
        points = collect_points(report, gates)
        run_date = args.run_date or datetime.now(timezone.utc).strftime("%Y-%m-%d")
        measured_at = args.measured_at or datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
        commit_sha = args.commit_sha or resolve_commit_sha()

        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.history_dir.mkdir(parents=True, exist_ok=True)
        history_output = args.history_dir / f"{run_date}.svg"

        for dest in (args.output, history_output):
            render_chart(
                points=points,
                report=report,
                output=dest,
                measured_at=measured_at,
                commit_sha=commit_sha,
                report_path=report_path,
            )

        print(f"Wrote {args.output}")
        print(f"Wrote {history_output}")
        return 0
    except ChartError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 2


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Render SliceFx runtime BenchmarkDotNet results as SVG charts."
    )
    parser.add_argument(
        "--results-dir",
        type=Path,
        default=Path("BenchmarkDotNet.Artifacts.Runtime/results"),
        help="Directory containing a BenchmarkDotNet *-report-full.json report.",
    )
    parser.add_argument(
        "--gates",
        type=Path,
        default=Path("tests/SliceFx.Benchmarks.Runtime/runtime-gates.json"),
        help="Gate configuration JSON.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("docs/perf/runtime-latest.svg"),
        help="Path for the latest SVG chart.",
    )
    parser.add_argument(
        "--history-dir",
        type=Path,
        default=Path("docs/perf/runtime-history"),
        help="Directory for dated SVG history files.",
    )
    parser.add_argument(
        "--run-date",
        help="Date used for the history filename, in YYYY-MM-DD format. Defaults to UTC today.",
    )
    parser.add_argument(
        "--measured-at",
        help="Human-readable measurement timestamp for the chart caption.",
    )
    parser.add_argument(
        "--commit-sha",
        help="Short commit SHA for the chart caption. Defaults to GITHUB_SHA or git rev-parse.",
    )
    return parser.parse_args()


def find_report(results_dir: Path) -> Path:
    if not results_dir.is_dir():
        raise ChartError(f"Results directory not found: {results_dir}")

    reports = sorted(
        path
        for path in results_dir.iterdir()
        if path.is_file()
        and (
            path.name.endswith("-report-full-compressed.json")
            or path.name.endswith("-report-full.json")
        )
    )

    if not reports:
        raise ChartError(f"No BenchmarkDotNet full JSON report found under {results_dir}")

    return max(reports, key=lambda p: p.stat().st_mtime)


def load_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise ChartError(f"JSON file not found: {path}")

    with path.open("r", encoding="utf-8") as file:
        value = json.load(file)

    if not isinstance(value, dict):
        raise ChartError(f"Expected a JSON object in {path}")

    return value


def collect_points(report: dict[str, Any], gates: dict[str, Any]) -> list[BenchmarkPoint]:
    benchmarks = report.get("Benchmarks")
    if not isinstance(benchmarks, list):
        raise ChartError("Benchmark report is missing a Benchmarks array")

    gate_rows = gates.get("gates")
    if not isinstance(gate_rows, list):
        raise ChartError("Gates file is missing a gates array")

    points: list[BenchmarkPoint] = []
    seen: set[tuple[str, int]] = set()

    for gate in gate_rows:
        if not isinstance(gate, dict):
            raise ChartError("Gate entries must be JSON objects")

        method = gate.get("method")
        if method not in ALL_METHODS:
            continue

        feature_count = require_int(gate.get("featureCount"), f"{method}.featureCount")
        gate_ms = require_number(gate.get("maxMeanMs"), f"{method}[{feature_count}].maxMeanMs")
        key = (method, feature_count)
        if key in seen:
            raise ChartError(f"Duplicate gate for {method}(FeatureCount={feature_count})")
        seen.add(key)

        matches = [
            benchmark
            for benchmark in benchmarks
            if isinstance(benchmark, dict)
            and benchmark.get("Method") == method
            and parse_feature_count(benchmark.get("Parameters")) == feature_count
        ]

        if len(matches) != 1:
            raise ChartError(
                f"Expected exactly one measurement for {method}(FeatureCount={feature_count}), "
                f"found {len(matches)}"
            )

        statistics = matches[0].get("Statistics")
        if not isinstance(statistics, dict):
            raise ChartError(
                f"Measurement for {method}(FeatureCount={feature_count}) is missing Statistics"
            )

        mean_ns = require_number(
            statistics.get("Mean"), f"{method}[{feature_count}].Statistics.Mean"
        )
        points.append(
            BenchmarkPoint(
                method=method,
                feature_count=feature_count,
                mean_ms=mean_ns / 1_000_000,
                gate_ms=gate_ms,
            )
        )

    return sorted(points, key=lambda p: (ALL_METHODS.index(p.method), p.feature_count))


def parse_feature_count(parameters: object) -> int | None:
    if not isinstance(parameters, str):
        return None

    match = re.search(r"(?:^|[,\s])FeatureCount\s*=\s*(\d+)(?:$|[,\s])", parameters)
    if match is None:
        return None

    return int(match.group(1))


def require_int(value: object, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise ChartError(f"Expected integer for {name}, got {value!r}")
    return value


def require_number(value: object, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ChartError(f"Expected number for {name}, got {value!r}")
    return float(value)


def render_chart(
    *,
    points: list[BenchmarkPoint],
    report: dict[str, Any],
    output: Path,
    measured_at: str,
    commit_sha: str,
    report_path: Path,
) -> None:
    startup_pts = [p for p in points if p.method in STARTUP_METHODS]
    request_pts = [p for p in points if p.method in REQUEST_METHODS]

    fig, (ax_top, ax_bot) = plt.subplots(2, 1, figsize=(9.5, 8.5))
    fig.patch.set_facecolor("white")
    fig.suptitle("SliceFx runtime benchmark", fontsize=16, fontweight="bold", y=0.97)

    _plot_group(ax_top, startup_pts, STARTUP_METHODS, title="Host startup (ms)", ylabel="Mean (ms)")
    _plot_group(ax_bot, request_pts, REQUEST_METHODS, title="Per-request latency (ms)", ylabel="Mean (ms)")

    environment = _format_environment(report)
    caption = (
        f"Measured {measured_at} | commit {commit_sha} | {environment}\n"
        f"Source: {report_path.as_posix()} | Gates: tests/SliceFx.Benchmarks.Runtime/runtime-gates.json"
    )
    fig.text(0.5, 0.01, caption, ha="center", va="bottom", fontsize=8.0, color="#475569")
    fig.tight_layout(rect=(0, 0.06, 1, 0.95))

    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(
        output,
        format="svg",
        bbox_inches="tight",
        metadata={"Date": None, "Creator": "SliceFx runtime benchmark chart renderer"},
    )
    plt.close(fig)


def _plot_group(
    ax: Any,
    pts: list[BenchmarkPoint],
    method_order: tuple[str, ...],
    *,
    title: str,
    ylabel: str,
) -> None:
    by_method = {m: [p for p in pts if p.method == m] for m in method_order}
    all_values = [p.mean_ms for p in pts] + [p.gate_ms for p in pts]
    y_max = max(all_values) * 1.22 if all_values else 1.0

    ax.set_title(title, fontsize=13, fontweight="bold", pad=10)
    ax.set_xlabel("FeatureCount")
    ax.set_ylabel(ylabel)
    ax.set_xticks([50, 100, 200])
    ax.set_xlim(35, 215)
    ax.set_ylim(0, y_max)
    ax.grid(True, axis="y", linestyle=":", linewidth=0.8, alpha=0.45)
    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)

    for method in method_order:
        series = by_method.get(method, [])
        if not series:
            continue
        xs = [p.feature_count for p in series]
        ys = [p.mean_ms for p in series]
        gates = [p.gate_ms for p in series]
        color = COLORS[method]
        label = DISPLAY_NAMES[method]

        ax.plot(xs, ys, marker="o", linewidth=2.4, markersize=7, label=label, color=color)
        ax.plot(xs, gates, linestyle=(0, (3, 3)), linewidth=1.6, color=color, alpha=0.55,
                label=f"{label} gate")

        for x, y in zip(xs, ys):
            ax.annotate(
                f"{y:.3f} ms",
                (x, y),
                textcoords="offset points",
                xytext=(0, 9),
                ha="center",
                fontsize=8.5,
                color=color,
            )

    ax.legend(loc="upper left", frameon=False, ncols=2)


def _format_environment(report: dict[str, Any]) -> str:
    host = report.get("HostEnvironmentInfo")
    if not isinstance(host, dict):
        return "BenchmarkDotNet environment unavailable"

    os_version = str(host.get("OsVersion") or "unknown OS")
    processor = str(host.get("ProcessorName") or "unknown CPU")
    architecture = str(host.get("Architecture") or "unknown architecture")
    runtime = str(host.get("RuntimeVersion") or "unknown runtime")
    dotnet_cli = str(host.get("DotNetCliVersion") or "unknown SDK")
    runner = os.environ.get("RUNNER_OS")
    runner_prefix = f"GitHub Actions {runner} | " if runner else ""

    return f"{runner_prefix}{processor} ({architecture}) | {os_version} | {runtime} | SDK {dotnet_cli}"


def resolve_commit_sha() -> str:
    github_sha = os.environ.get("GITHUB_SHA")
    if github_sha:
        return github_sha[:7]

    try:
        result = subprocess.run(
            ["git", "rev-parse", "--short=7", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return "unknown"

    return result.stdout.strip() or "unknown"


class ChartError(Exception):
    pass


if __name__ == "__main__":
    raise SystemExit(main())
