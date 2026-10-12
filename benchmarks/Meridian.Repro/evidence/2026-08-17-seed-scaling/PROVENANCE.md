# Provenance

These historical measurements were recorded on **2026-08-17**, on a
**Windows 11 Pro** machine running the **.NET 10 runtime via `RollForward: LatestMajor`**
against `net8.0`-targeted projects (see `Directory.Build.props`).

They predate the committed `Meridian.Repro` harness and were produced by a one-off
`SeedSweep` console application. They are retained as historical measurements, rather
than a claim about every machine or a baseline for the current verifier. To verify
this snapshot, use `../../expected/expected_hashes.json` and the current harness's
`--verify` command. The original sweep application and scratch databases are not
part of this evidence bundle.

**Raw vs. semantic equality, and how this differs from the current harness.** `SeedSweep` was a
single-machine, single-OS tool: its "distinct database files" and stdout-equality numbers below
were computed from raw bytes (the exact SQLite file / exact stdout bytes), which is a fine,
honest measurement on ONE machine but is not something that can be meaningfully asserted across
operating systems (raw bytes can differ for reasons — line-ending conventions, SQLite page
layout — that have nothing to do with simulation determinism). `Meridian.Repro` (this bundle's
harness, in `../..`) makes that distinction explicit: it computes a CANONICAL (portable,
semantic) fingerprint for full-process stdout/database — the actual cross-platform acceptance
criterion, and what its own `aggregate_fingerprint` and CI's cross-OS comparison are built
from — alongside the RAW same-environment diagnostic hashes `SeedSweep` effectively produced.
The headline numbers below are `SeedSweep`'s original (raw-byte, same-machine) measurements;
they are historical evidence for this specific machine/date, not a cross-platform claim, and are
superseded as ground truth by `../../expected/expected_hashes.json`.

## Headline numbers

| Metric | Value |
|---|---|
| Stochastic probe: mismatches @ 10,000 seeds × 2 repeats | 0 |
| Stochastic probe: distinct trajectories @ 10,000 seeds | 10,000 / 10,000 |
| Narrative: seeds that consumed RNG | 0 / 40 |
| Narrative: distinct trajectories | 1 |
| Full-process: mismatches @ 40 seeds × 2 repeats | 0 |
| Full-process: distinct stdout trajectories | 1 |
| Full-process: distinct database files | 40 / 40 (snapshots embed the seed) |
| Full-process: mean time per seed-pair | ≈ 0.60 s/pair |
| In-process (steady-state fit): time per seed-pair | ≈ 1.30 ms/pair |
| Full-process ÷ in-process cost ratio | ≈ 419× |

Source files for each row: `full_process_summary.json`, `inprocess_summary.json`,
`narrative_seed_summary.json`, and `timing_fit.json` (steady-state linear fit over the
in-process cumulative-timing samples).
