from __future__ import annotations

import csv
import json
import math
import sys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np


root = Path(sys.argv[1]).resolve()


def rows(name: str) -> list[dict[str, str]]:
    with (root / name).open(newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


full = rows("full_process_pairs.csv")
samples = rows("inprocess_samples.csv")
full_summary = json.loads((root / "full_process_summary.json").read_text(encoding="utf-8"))
inprocess_summary = json.loads((root / "inprocess_summary.json").read_text(encoding="utf-8"))

seed_x = np.array([int(row["seeds"]) for row in samples], dtype=float)
elapsed_y = np.array([float(row["elapsed_seconds"]) for row in samples], dtype=float)
throughput_y = np.array([float(row["seeds_per_second"]) for row in samples], dtype=float)
full_seed_x = np.array([int(row["seed"]) for row in full], dtype=float)
pair_time_y = np.array([float(row["pair_seconds"]) for row in full], dtype=float)

# Fit the steady region after startup/tiered-JIT effects have mostly amortized.
steady = seed_x >= 1000
slope, intercept = np.polyfit(seed_x[steady], elapsed_y[steady], 1)
prediction = slope * seed_x + intercept
residual = elapsed_y[steady] - (slope * seed_x[steady] + intercept)
ss_res = float(np.sum(residual ** 2))
ss_tot = float(np.sum((elapsed_y[steady] - np.mean(elapsed_y[steady])) ** 2))
r_squared = 1.0 - ss_res / ss_tot

plt.style.use("dark_background")
fig, axes = plt.subplots(2, 2, figsize=(15, 10))
fig.subplots_adjust(left=0.07, right=0.98, top=0.90, bottom=0.11, hspace=0.32, wspace=0.25)
fig.suptitle("Meridian deterministic seed-scaling benchmark", fontsize=20, fontweight="bold", y=0.975)

ax = axes[0, 0]
ax.plot(seed_x, elapsed_y, color="#38bdf8", linewidth=2.5, label="measured")
ax.plot(seed_x, prediction, color="#f59e0b", linestyle="--", linewidth=1.8,
        label=f"steady linear fit: {slope * 1000:.3f} ms/seed pair, R²={r_squared:.6f}")
for checkpoint in (100, 1000, 10000):
    row = next(item for item in inprocess_summary["checkpoints"] if item["seeds"] == checkpoint)
    ax.scatter([checkpoint], [row["elapsed_seconds"]], s=75, color="#f59e0b", zorder=5)
    ax.annotate(f"{checkpoint:,}: {row['elapsed_seconds']:.3f}s",
                (checkpoint, row["elapsed_seconds"]), xytext=(8, 8), textcoords="offset points", fontsize=9)
ax.set_title("Cumulative in-process time")
ax.set_xlabel("Seeds (two 20-turn runs per seed)")
ax.set_ylabel("Wall time (seconds)")
ax.grid(alpha=0.2)
ax.legend(fontsize=9)

ax = axes[0, 1]
ax.plot(seed_x, throughput_y, color="#a78bfa", linewidth=2.5)
ax.set_title("Amortized in-process throughput")
ax.set_xlabel("Seeds completed")
ax.set_ylabel("Repeated seed pairs / second")
ax.grid(alpha=0.2)
ax.annotate(f"10,000 checkpoint: {inprocess_summary['seeds_per_second']:.1f} pairs/s",
            (seed_x[-1], throughput_y[-1]), xytext=(-210, -30), textcoords="offset points",
            arrowprops={"arrowstyle": "->", "color": "#c4b5fd"}, fontsize=10)

ax = axes[1, 0]
ax.plot(full_seed_x, pair_time_y, marker="o", markersize=3.5, color="#34d399", linewidth=1.5)
mean_pair = float(full_summary["mean_pair_seconds"])
ax.axhline(mean_pair, color="#f59e0b", linestyle="--", linewidth=1.8,
           label=f"mean {mean_pair:.3f}s per seed pair")
ax.set_title("Full executable + SQLite latency")
ax.set_xlabel("Seed")
ax.set_ylabel("Two-process pair time (seconds)")
ax.grid(alpha=0.2)
ax.legend(fontsize=9)

ax = axes[1, 1]
full_ms = mean_pair * 1000
inprocess_ms = float(inprocess_summary["elapsed_seconds"]) / float(inprocess_summary["maximum_seed"]) * 1000
labels = ["Full process\n+ SQLite", "In-process\nstochastic probe"]
values = [full_ms, inprocess_ms]
bars = ax.bar(labels, values, color=["#f59e0b", "#38bdf8"])
ax.set_yscale("log")
ax.set_ylabel("Milliseconds per repeated seed pair (log scale)")
ax.set_title(f"Execution-layer cost (~{full_ms / inprocess_ms:.0f}× difference)")
ax.grid(axis="y", alpha=0.2)
for bar, value in zip(bars, values):
    ax.text(bar.get_x() + bar.get_width() / 2, value * 1.12, f"{value:.3f} ms",
            ha="center", va="bottom", fontsize=10)

fig.text(0.5, 0.025,
         "Stochastic sweep: 10,000 seeds, 20,000 engine runs, 400,000 turns, 0 mismatches, 10,000 distinct trajectories. "
         "Full process: 40 seeds, 80 processes, 0 stdout/database mismatches.",
         ha="center", fontsize=10, color="#cbd5e1")
fig.savefig(root / "seed_scaling_dashboard.png", dpi=180, facecolor=fig.get_facecolor())
plt.close(fig)

# A clean log-log checkpoint plot makes asymptotic comparisons easier as future
# experiments add 100k/1M checkpoints.
checkpoints = inprocess_summary["checkpoints"]
cx = np.array([item["seeds"] for item in checkpoints], dtype=float)
cy = np.array([item["elapsed_seconds"] for item in checkpoints], dtype=float)
fig, ax = plt.subplots(figsize=(10, 6), constrained_layout=True)
ax.loglog(cx, cy, marker="o", markersize=8, linewidth=2.5, color="#38bdf8", label="measured")
reference = cy[0] * (cx / cx[0])
ax.loglog(cx, reference, linestyle="--", color="#f59e0b", label="linear from 100-seed checkpoint")
for x, y in zip(cx, cy):
    ax.annotate(f"{int(x):,} seeds\n{y:.3f}s", (x, y), xytext=(8, 8), textcoords="offset points")
ax.set_title("Meridian stochastic seed sweep: checkpoint scaling", fontsize=16, fontweight="bold")
ax.set_xlabel("Seeds (log scale)")
ax.set_ylabel("Wall time, seconds (log scale)")
ax.grid(which="both", alpha=0.2)
ax.legend()
fig.savefig(root / "seed_scaling_loglog.png", dpi=180, facecolor=fig.get_facecolor())
plt.close(fig)

metrics = {
    "steady_fit_seconds_per_seed_pair": slope,
    "steady_fit_milliseconds_per_seed_pair": slope * 1000,
    "steady_fit_intercept_seconds": intercept,
    "steady_fit_r_squared": r_squared,
    "full_to_inprocess_pair_cost_ratio": full_ms / inprocess_ms,
}
(root / "timing_fit.json").write_text(json.dumps(metrics, indent=2), encoding="utf-8")
print(json.dumps(metrics, indent=2))
