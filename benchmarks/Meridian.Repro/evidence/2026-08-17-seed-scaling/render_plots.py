from __future__ import annotations

import csv
import html
import json
import math
import statistics
import sys
from pathlib import Path


ROOT = Path(sys.argv[1]).resolve()


def read_csv(name: str) -> list[dict[str, str]]:
    with (ROOT / name).open(newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def polyline_plot(
    filename: str,
    title: str,
    subtitle: str,
    points: list[tuple[float, float]],
    x_label: str,
    y_label: str,
    *,
    log_x: bool = False,
    log_y: bool = False,
    marker_points: set[float] | None = None,
) -> None:
    width, height = 1100, 650
    left, right, top, bottom = 105, 45, 90, 90
    plot_w, plot_h = width - left - right, height - top - bottom
    marker_points = marker_points or set()

    tx = (lambda value: math.log10(value)) if log_x else (lambda value: value)
    ty = (lambda value: math.log10(max(value, 1e-12))) if log_y else (lambda value: value)
    transformed = [(tx(x), ty(y), x, y) for x, y in points]
    xs = [p[0] for p in transformed]
    ys = [p[1] for p in transformed]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    if math.isclose(xmin, xmax): xmax = xmin + 1
    if math.isclose(ymin, ymax): ymax = ymin + 1
    ypad = (ymax - ymin) * 0.08
    ymin = max(0, ymin - ypad) if not log_y else ymin - ypad
    ymax += ypad

    def sx(value: float) -> float:
        return left + (value - xmin) / (xmax - xmin) * plot_w

    def sy(value: float) -> float:
        return top + plot_h - (value - ymin) / (ymax - ymin) * plot_h

    elements: list[str] = []
    elements.append(f'<rect width="{width}" height="{height}" fill="#0b1020"/>')
    elements.append(f'<text x="{left}" y="38" fill="#f8fafc" font-size="25" font-family="Segoe UI, sans-serif" font-weight="700">{html.escape(title)}</text>')
    elements.append(f'<text x="{left}" y="65" fill="#94a3b8" font-size="14" font-family="Segoe UI, sans-serif">{html.escape(subtitle)}</text>')

    for index in range(6):
        ratio = index / 5
        yv = ymin + (ymax - ymin) * ratio
        py = sy(yv)
        raw = 10 ** yv if log_y else yv
        label = f"{raw:.3g}"
        elements.append(f'<line x1="{left}" y1="{py:.2f}" x2="{left + plot_w}" y2="{py:.2f}" stroke="#263047" stroke-width="1"/>')
        elements.append(f'<text x="{left - 12}" y="{py + 5:.2f}" fill="#94a3b8" text-anchor="end" font-size="12" font-family="Consolas, monospace">{label}</text>')

    for index in range(6):
        ratio = index / 5
        xv = xmin + (xmax - xmin) * ratio
        px = sx(xv)
        raw = 10 ** xv if log_x else xv
        label = f"{raw:.3g}"
        elements.append(f'<line x1="{px:.2f}" y1="{top}" x2="{px:.2f}" y2="{top + plot_h}" stroke="#182237" stroke-width="1"/>')
        elements.append(f'<text x="{px:.2f}" y="{top + plot_h + 28}" fill="#94a3b8" text-anchor="middle" font-size="12" font-family="Consolas, monospace">{label}</text>')

    line = " ".join(f"{sx(x):.2f},{sy(y):.2f}" for x, y, _, _ in transformed)
    elements.append(f'<polyline points="{line}" fill="none" stroke="#38bdf8" stroke-width="3" stroke-linejoin="round" stroke-linecap="round"/>')

    for x, y, raw_x, raw_y in transformed:
        if raw_x in marker_points:
            elements.append(f'<circle cx="{sx(x):.2f}" cy="{sy(y):.2f}" r="7" fill="#f59e0b" stroke="#fff7ed" stroke-width="2"><title>{raw_x:g} seeds: {raw_y:.4f}s</title></circle>')

    elements.append(f'<line x1="{left}" y1="{top + plot_h}" x2="{left + plot_w}" y2="{top + plot_h}" stroke="#cbd5e1" stroke-width="2"/>')
    elements.append(f'<line x1="{left}" y1="{top}" x2="{left}" y2="{top + plot_h}" stroke="#cbd5e1" stroke-width="2"/>')
    elements.append(f'<text x="{left + plot_w / 2}" y="{height - 24}" fill="#cbd5e1" text-anchor="middle" font-size="15" font-family="Segoe UI, sans-serif">{html.escape(x_label)}</text>')
    elements.append(f'<text x="25" y="{top + plot_h / 2}" fill="#cbd5e1" text-anchor="middle" font-size="15" font-family="Segoe UI, sans-serif" transform="rotate(-90 25 {top + plot_h / 2})">{html.escape(y_label)}</text>')

    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">' + "".join(elements) + "</svg>"
    (ROOT / filename).write_text(svg, encoding="utf-8")


full_rows = read_csv("full_process_pairs.csv")
inprocess_rows = read_csv("inprocess_samples.csv")
full_summary = json.loads((ROOT / "full_process_summary.json").read_text(encoding="utf-8"))
inprocess_summary = json.loads((ROOT / "inprocess_summary.json").read_text(encoding="utf-8"))
narrative_summary = json.loads((ROOT / "narrative_seed_summary.json").read_text(encoding="utf-8"))

full_points = [(float(row["seed"]), float(row["pair_seconds"])) for row in full_rows]
inprocess_points = [(float(row["seeds"]), float(row["elapsed_seconds"])) for row in inprocess_rows]
checkpoints = {100.0, 1000.0, 10000.0}

polyline_plot(
    "full_process_pair_latency.svg",
    "Full executable + SQLite repeatability: seeds 1–40",
    f"Two fresh 20-turn processes per seed; mean pair {full_summary['mean_pair_seconds']:.3f}s; mismatches {full_summary['mismatches']}",
    full_points,
    "Seed",
    "Pair wall time (seconds)",
)

polyline_plot(
    "inprocess_cumulative_scaling.svg",
    "Stochastic 20-turn repeatability sweep",
    f"Two engine runs per seed; {inprocess_summary['total_turns']:,} total turns; mismatches {inprocess_summary['mismatches']}",
    inprocess_points,
    "Seeds completed",
    "Cumulative wall time (seconds)",
    marker_points=checkpoints,
)

checkpoint_points = [
    (float(item["seeds"]), float(item["elapsed_seconds"]))
    for item in inprocess_summary["checkpoints"]
]
polyline_plot(
    "inprocess_loglog_scaling.svg",
    "Checkpoint scaling: 100 → 1,000 → 10,000 seeds",
    "Log-log view; a slope near 1 indicates linear total-time scaling",
    checkpoint_points,
    "Seeds (log scale)",
    "Elapsed seconds (log scale)",
    log_x=True,
    log_y=True,
    marker_points=checkpoints,
)

full_pair_ms = float(full_summary["mean_pair_seconds"]) * 1000
inprocess_pair_ms = float(inprocess_summary["elapsed_seconds"]) / float(inprocess_summary["maximum_seed"]) * 1000
bar_width, bar_height = 900, 520
maximum = max(full_pair_ms, inprocess_pair_ms)
bars = [
    ("Full process + SQLite", full_pair_ms, "#f59e0b"),
    ("In-process stochastic engine", inprocess_pair_ms, "#38bdf8"),
]
elements = [f'<rect width="{bar_width}" height="{bar_height}" fill="#0b1020"/>']
elements.append('<text x="75" y="42" fill="#f8fafc" font-size="25" font-family="Segoe UI, sans-serif" font-weight="700">Cost per repeated seed pair</text>')
elements.append('<text x="75" y="68" fill="#94a3b8" font-size="14" font-family="Segoe UI, sans-serif">Each pair contains two independent 20-turn executions</text>')
for index, (label, value, color) in enumerate(bars):
    y = 145 + index * 145
    width = 650 * value / maximum
    elements.append(f'<text x="75" y="{y - 14}" fill="#cbd5e1" font-size="16" font-family="Segoe UI, sans-serif">{html.escape(label)}</text>')
    elements.append(f'<rect x="75" y="{y}" width="{width:.2f}" height="56" rx="8" fill="{color}"/>')
    elements.append(f'<text x="{85 + width:.2f}" y="{y + 36}" fill="#f8fafc" font-size="15" font-family="Consolas, monospace">{value:.3f} ms</text>')
elements.append(f'<text x="75" y="470" fill="#94a3b8" font-size="13" font-family="Segoe UI, sans-serif">Process mode includes CLR startup, filesystem work, SQLite schema/transactions, serialization, and output rendering.</text>')
(ROOT / "mode_cost_comparison.svg").write_text(
    f'<svg xmlns="http://www.w3.org/2000/svg" width="{bar_width}" height="{bar_height}" viewBox="0 0 {bar_width} {bar_height}">' + "".join(elements) + "</svg>",
    encoding="utf-8",
)

report = f"""# Meridian seed-scaling benchmark

## Results

- Full process seeds: {full_summary['seeds']}, two fresh executable/SQLite runs each
- Full process mismatches: {full_summary['mismatches']}
- Full process distinct stdout trajectories: {full_summary['distinct_stdout_trajectories']}
- Full process distinct databases: {full_summary['distinct_database_files']} (seed metadata is persisted)
- Stochastic probe seeds: {inprocess_summary['maximum_seed']:,}, two 20-turn engine runs each
- Stochastic probe total turns: {inprocess_summary['total_turns']:,}
- Stochastic probe mismatches: {inprocess_summary['mismatches']}
- Stochastic probe distinct behavioral trajectories: {inprocess_summary['distinct_trajectories']:,}
- Narrative seeds consuming RNG: {narrative_summary['seeds_that_consumed_rng']} / {narrative_summary['seeds']}
- Narrative distinct behavioral trajectories: {narrative_summary['distinct_behavioral_trajectories']}

## Timing plots

- `full_process_pair_latency.svg`
- `inprocess_cumulative_scaling.svg`
- `inprocess_loglog_scaling.svg`
- `mode_cost_comparison.svg`
"""
(ROOT / "README.md").write_text(report, encoding="utf-8")

print(json.dumps({
    "full_process": full_summary,
    "inprocess": inprocess_summary,
    "narrative": narrative_summary,
    "output_directory": str(ROOT),
}, indent=2))
