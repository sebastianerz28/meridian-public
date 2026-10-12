# Meridian.Repro

A committed, re-runnable reproducibility benchmark for the Meridian simulation. It turns a
one-off seed-scaling experiment (see `evidence/2026-08-17-seed-scaling/`) into something a
third party can independently reproduce: clone the repo, run one command, and compare **one
aggregate hash** instead of inspecting thousands of individual values.

## Purpose

Meridian's `TurnEngine` is meant to be deterministic: given the same scenario, actor set, and
seed, it must produce byte-identical event streams and final world state every time, whether
run twice in the same process or as two completely separate processes on two different
operating systems. `Meridian.Repro` is the proof harness for that claim. It runs three
scenarios, each proving a different slice of the determinism story, folds every run's output
down to a single SHA-256 "trajectory fingerprint," and folds every seed's fingerprint down to
one further "aggregate fingerprint" per (scenario, seed count) combination. Comparing two
aggregate fingerprints is comparing two entire benchmark runs.

## Quickstart

```bash
# Fast sanity check (defaults: 2 repeats, 20 turns each)
dotnet run -c Release --project benchmarks/Meridian.Repro -- --scenario narrative --seeds 40

# Stochastic reproducibility with 10,000 seeds:
dotnet run -c Release --project benchmarks/Meridian.Repro -- \
  --scenario stochastic-spy-probe --seeds 10000 --repeats 2 --turns 20 --output artifacts/repro

# The expensive, most-honest mode (spawns the real headless binary; build it first)
dotnet build -c Release
dotnet run -c Release --project benchmarks/Meridian.Repro -- --scenario full-process --seeds 40

# Reproduce this bundle's recorded ground truth and compare aggregates
dotnet run -c Release --project benchmarks/Meridian.Repro -- --verify
```

### CLI reference

```
dotnet run -c Release --project benchmarks/Meridian.Repro -- \
  --scenario narrative|stochastic-spy-probe|full-process \
  --seeds N          (default: narrative 40, stochastic-spy-probe 10000, full-process 40)
  --repeats N         (default 2)
  --turns N           (default 20)
  --output DIR        (default artifacts/repro; created if missing)
  --verify            (mode: ignores --scenario/--seeds/--repeats/--turns; re-runs exactly what's
                        recorded in expected/expected_hashes.json and compares aggregates)
```

Seeds are `1..N` (`ulong`). Exit codes: `0` = success / all aggregates matched and all sanity
gates passed; `1` = a sanity gate failed or an aggregate mismatched (`--verify` re-applies the
same gates to each re-run, on top of comparing aggregates — see "expected_hashes.json /
--verify workflow" below); `2` = a setup problem (bad arguments, `expected_hashes.json`
missing/unparsable, or `full-process` run without a prior `dotnet build -c Release`).

## The three scenarios

All three scenarios use the same fixed initial state, `Scenario.CreateMeridianStrait()`, and
all actor registrations are `SchedulingPolicy.EveryTurn` (scripted beats always land on the
exact turn they're written for).

### 1. `narrative`

Reuses `Game.Headless.ScriptedStoryActors` **verbatim** via a project reference to
`Game.Headless` (see `Meridian.Repro.csproj`) — Borvia `ExportControl(0.8)→Alderia` at T2 and
`DeployToRegion` at T6; Alderia `Mobilize` at T3 and `DeployToRegion` at T5; Cascara
`Reassure→Alderia` at T4; Pass otherwise. **This proves the shipped story scenario never
touches the rng**: none of its scripted actions ever draw from `DeterministicRng`, and the one
passive system that does (`StraitSystem`'s incident roll) never crosses its trigger threshold
in this story. `rng_consumed_seeds` must be `0` for every seed, and every seed must produce the
exact same trajectory (`distinct_trajectories == 1`) — the story's outcome is seed-independent
by construction.

### 2. `stochastic-spy-probe`

A benchmark-owned actor set (not shared with the headless binary) engineered so **every seed
consumes the rng**: Borvia spies on Alderia every turn from T2 onward; Alderia adds `Mobilize`
at T3 and `DeployToRegion` at T5 before spying on Borvia from T7 onward; Cascara reassures
Alderia at T4 before spying on Borvia from T10 onward (`ActionType.Spy`'s resolver always
draws exactly two rng values, win or lose). This proves reproducibility holds under real rng
load, not just in a rng-free story: `mismatches == 0`, `rng_consumed_seeds == seed_count`, and
`distinct_trajectories == seed_count` (10,000 independent seeds, 10,000 independent
trajectories, zero collisions). It also records **cumulative timing checkpoints** at
seeds = 100 / 1,000 / 10,000 (whichever are ≤ `--seeds`) in a single run, rather than three
separate runs — see `timing_checkpoints` in the manifest schema below.

### 3. `full-process`

The expensive, most-honest mode: for every (seed, repeat), spawns
`dotnet src/Game.Headless/bin/Release/net8.0/Game.Headless.dll --turns T --seed S --actors
scripted` as a real child process in a **fresh temporary working directory** (the headless
binary writes `run.db` relative to its CWD). This requires a prior `dotnet build -c Release` —
full-process deliberately does not build for you, so its timing reflects only process/JIT/SQLite
cost, not compilation. It proves determinism survives process boundaries, cold starts, and a
real SQLite round-trip.

**stdout and the persisted database are each fingerprinted twice, and gated independently.**
Every run produces a CANONICAL (portable) fingerprint that feeds the aggregate proof and is
meaningful across machines/OSes, and a RAW (same-environment diagnostic) hash that is exact
bytes with no normalization — useful for narrowing down where two same-machine runs diverged,
but never compared across environments. See "Fingerprint spec" below for exactly how each is
built. Repeat mismatches are tracked and gated **independently** for stdout vs. database — a
same-seed database divergence must fail even when stdout is byte-identical across repeats
(and vice versa), so neither side can hide behind the other agreeing.

Expected: `stdout_mismatches == 0` and `database_mismatches == 0` (every repeat, canonically,
agrees on both sides), `distinct_stdout_trajectories == 1` (the scripted story is
seed-independent, exactly like `narrative`, so all 40 seeds print identical — after line-ending
normalization — news/standings), and `distinct_database_trajectories == seed_count` (each
`run.db`'s persisted `EngineSnapshot` embeds the game seed, so its logical content differs per
seed even though the story itself doesn't). `distinct_database_files` (the RAW, same-environment
diagnostic) is also expected to equal `seed_count` on any one machine, but is never asserted
across OSes — see the fingerprint spec's canonical/raw split for why.

## Fingerprint spec

This is the exact, reimplementable spec — a third party with no access to this repo's code
should be able to reproduce these hashes from this section alone.

### Trajectory fingerprint (in-process scenarios: `narrative`, `stochastic-spy-probe`)

For one (scenario, seed, repeat):

1. Construct a fresh `Scenario.CreateMeridianStrait()` world and a fresh
   `TurnEngine(world, actors, seed)`.
2. Advance `--turns` turns via `AdvanceTurnAsync()`, collecting every `TurnResult.Events` in
   call order (do not reorder or deduplicate).
3. For each event, in stream order, append two lines to a UTF-8 buffer:
   `"{event.GetType().Name}\n{EventSerializer.Serialize(event)}\n"`
   (`EventSerializer.Serialize` uses `Game.Persistence`'s own `new JsonSerializerOptions()` —
   the type-discriminated, closed-set event serializer already shipped with the simulation).
4. After the last event, append: `"FINAL\n" + JsonSerializer.Serialize(WorldStateDto.FromWorld(engine.World), options)`
   where `options` is **one shared, default, unindented** `JsonSerializerOptions` instance
   (`new JsonSerializerOptions()` — no camelCase, no indentation; declaration-order property
   names, which is what makes this portable across the environments this bundle actually
   certifies: `proof_schema_version` `"1"` on the .NET 8 runtime, across the `windows-latest` /
   `ubuntu-latest` matrix CI exercises — see "CI" below. Portability isn't claimed as a
   universal guarantee across arbitrary past/future .NET versions; a `proof_schema_version` bump
   is how a future change to this assumption would be signaled).
5. SHA-256 the resulting UTF-8 buffer. Lowercase hex = the **trajectory fingerprint**.

`rng_consumed` for that run = `engine.Snapshot().RngState != new DeterministicRng(seed).State`
— i.e. compare the post-run rng state against a **freshly constructed** rng with the same seed
(never call `.NextDouble()` on the comparison instance; it exists purely to read the
seed-normalized initial `.State`).

**Repeat mismatch**: same seed, different trajectory fingerprint across repeats.

The **canonical fingerprint** used for aggregation/CSV is repeat 0's — if repeats disagree,
the seed is already flagged as a mismatch, and any repeat is equally "wrong" evidence.

### Full-process fingerprints: portable (canonical) vs. same-environment (raw)

For one (seed, repeat), spawn the headless binary as specified above in a fresh temp working
directory. **Raw SQLite-file/stdout byte equality is a same-environment result only** — it can
differ for reasons that have nothing to do with simulation determinism (line-ending
conventions, SQLite page layout/freelist churn, filesystem metadata), so it is never comparable
across operating systems and never feeds the aggregate proof. **The semantic (canonical)
fingerprints below are the cross-platform acceptance criterion** — they are what the aggregate
proof is built from, and they are what CI's cross-OS comparison actually asserts on.

**Canonical (portable) stdout fingerprint:** decode the raw stdout bytes as UTF-8, normalize
line endings (`"\r\n"` → `"\n"`, then any remaining `"\r"` → `"\n"`), re-encode as UTF-8, and
SHA-256 the result. Lowercase hex = that run's canonical stdout fingerprint.

**Raw stdout hash (diagnostic only):** SHA-256 of the exact bytes read from the child process's
stdout pipe (`StreamReader.BaseStream`, never through any other decode/re-encode path), no
normalization. Recorded for same-machine debugging; never used in the aggregate.

**Canonical (logical) database fingerprint:** open `run.db` and append every persisted
event in `(turn, ordinal)` order as `"{TypeName}\n{EventSerializer.Serialize(event)}\n"`,
using `GameStore.LoadAllEvents()`. Then append **every snapshot row**, ordered by turn
ascending, as `"SNAPSHOT\n{turn}\n{world_json}\n{agents_json}\n{engine_json}\n"`.
The turn uses invariant formatting; JSON columns are read verbatim with
`SELECT turn, world_json, agents_json, engine_json FROM snapshots ORDER BY turn ASC`.
SHA-256 the UTF-8 buffer. Covering all snapshots checks the complete persisted resume
history. The raw JSON comes from `GameStore`'s fixed serializer options; it is not
re-parsed or reordered.

**Raw database hash (diagnostic only):** SHA-256 of the `run.db` file's exact bytes as written
to disk. Recorded for same-machine debugging; never used in the aggregate.

`rng_consumed` is computed the same way as the in-process scenarios: the persisted
`EngineSnapshot.RngState` (read back via `GameStore.LoadLatestSnapshot()`) is compared against
`new DeterministicRng(seed).State`.

**Combined per-seed fingerprint (what feeds the aggregate):**
`SHA-256("STDOUT\n{canonical stdout}\nDATABASE\n{canonical database}\n")` using repeat 0's
canonical stdout and canonical database fingerprints — never raw bytes.

**Stdout/database mismatch, tracked independently:** for a given seed, a *stdout* mismatch is
"canonical stdout fingerprint disagreed across repeats"; a *database* mismatch is "canonical
database fingerprint disagreed across repeats." Neither can mask the other — a seed with
identical stdout but divergent database fingerprints across repeats is still a mismatch (and
vice versa).

### Aggregate proof hash (all scenarios)

Given a scenario's per-seed canonical fingerprint mapping (for `full-process`, the **combined**
canonical stdout+database fingerprint above — never raw platform-dependent bytes):

1. Sort by seed ascending.
2. Build a UTF-8 buffer of `"{seed}:{fingerprint}\n"` lines, one per seed, in that sorted
   order.
3. SHA-256 the buffer. Lowercase hex = the **aggregate fingerprint** — the one number that
   stands in for an entire (scenario, seed count) run.

### Harness-internal determinism rules

- Invariant culture everywhere (`InvariantGlobalization` is set in `Meridian.Repro.csproj`;
  every ad hoc numeric-to-string conversion also passes `CultureInfo.InvariantCulture`
  explicitly, belt-and-suspenders).
- Exactly one shared `JsonSerializerOptions` for fingerprinting (`Fingerprint.JsonOptions`) and
  a separate, single shared one for manifest I/O (`ManifestIo.JsonOptions`) — never constructed
  per-call.
- Seeds run concurrently (`Parallel.ForEachAsync`, degree = `Environment.ProcessorCount` for
  in-process scenarios, capped at 8 real process spawns for `full-process`) **only** because
  each seed's state is fully isolated (its own `WorldState`/`TurnEngine`/rng, or its own temp
  directory + child process) and results are written into a seed-indexed array. Every
  aggregate is computed afterward from that sorted array, so it is independent of which thread
  finished which seed first.

## Manifest schema

Every run writes `<output>/<scenario>-manifest.json` (snake_case field names on the wire):

| Field | Meaning |
|---|---|
| `proof_schema_version` | Always `"1"`. |
| `git_commit` | `git rev-parse HEAD` at run time, or `"unknown"` on failure. |
| `git_dirty` | `git status --porcelain` non-empty at run time. |
| `scenario` | `narrative` \| `stochastic-spy-probe` \| `full-process`. |
| `ruleset_version` | Always `"meridian-core-v1"`. |
| `scenario_version` | Always `"meridian-strait-v1"`. |
| `seed_start` | Always `1`. |
| `seed_count` | Number of seeds run. |
| `runs_per_seed` | The `--repeats` value. |
| `turns_per_run` | The `--turns` value. |
| `dotnet_runtime` / `os` / `architecture` | `RuntimeInformation.FrameworkDescription` / `.OSDescription` / `.OSArchitecture`, read live. |
| `event_serializer_version` | Always `"1"`. |
| `mismatches` | Count of seeds whose repeats disagreed (full-process: either the stdout OR the database side — see `stdout_mismatches`/`database_mismatches` for the independently-tracked breakdown). |
| `distinct_trajectories` | Count of distinct canonical fingerprints across all seeds (full-process: distinct COMBINED canonical stdout+database fingerprints). |
| `rng_consumed_seeds` | Count of seeds whose rng state moved off its seed-normalized initial value. |
| `aggregate_fingerprint` | The aggregate proof hash over every seed in the run (full-process: built from each seed's combined CANONICAL fingerprint — never raw bytes). |
| `elapsed_seconds` | Total wall-clock seconds for the whole run. |
| `timing_checkpoints` | Array of `{seeds, elapsed_seconds, mismatches, distinct_trajectories, aggregate_fingerprint, seeds_per_second}`, cumulative from the start of the run; populated only for `stochastic-spy-probe` (at 100/1,000/10,000 seeds), empty otherwise. |
| `distinct_stdout_trajectories` | `full-process` only, PORTABLE: distinct CANONICAL (line-ending-normalized) stdout fingerprints across seeds. |
| `distinct_database_files` | `full-process` only, DIAGNOSTIC (same-environment, raw bytes): distinct `run.db` file-content hashes across all (seed, repeat) pairs. Never compared across OSes. |
| `distinct_database_trajectories` | `full-process` only, PORTABLE: distinct CANONICAL (logical) database fingerprints across seeds. |
| `stdout_mismatches` | `full-process` only: count of seeds whose CANONICAL stdout fingerprint disagreed across repeats. |
| `database_mismatches` | `full-process` only: count of seeds whose CANONICAL database fingerprint disagreed across repeats. |
| `mean_pair_seconds` | `full-process` only: mean wall-clock seconds to complete one seed's full repeat set. |

In-process scenarios also write `<output>/<scenario>-fingerprints.csv`: one `seed,fingerprint`
line per seed, sorted by seed ascending (not written for `full-process`).

## `expected_hashes.json` / `--verify` workflow

`expected/expected_hashes.json` records ground truth generated directly by this harness
for the simulation included in this snapshot:

```json
{
  "simulation_commit": "",
  "generated_utc": "<ISO-8601>",
  "entries": [
    {"scenario": "...", "seeds": N, "repeats": 2, "turns": 20, "aggregate_fingerprint": "..."}
  ]
}
```

`stochastic-spy-probe` contributes three entries (seeds = 100, 1,000, 10,000) — `--verify`
reproduces all three from a **single** 10,000-seed run, matching each entry against the
checkpoint at its own seed count. One 10,000-seed run yields all three checkpoints.

**`--verify` re-applies the sanity gates, not just the aggregate comparison.** Each aggregate
fingerprint is built from every seed's *repeat-0* fingerprint only (see the fingerprint spec
above) — so comparing aggregates alone cannot detect a verifier's machine producing REAL repeat
mismatches (live non-determinism) if repeat 0 happens to still match. To close that gap,
`--verify` runs the same `Gates.Check` used during ordinary generation against every re-run's
own manifest (whose `mismatches` field IS computed from every repeat) and folds any gate
failure into the exit code, printing each entry's `mismatches` / `distinct_trajectories` /
`rng_consumed_seeds` (and, for full-process, `stdout_mismatches` / `database_mismatches` /
`distinct_stdout_trajectories` / `distinct_database_trajectories` / `distinct_database_files`)
so a verifier can see the underlying numbers, not just a pass/fail bit. A same-seed **database**
mismatch fails `--verify` exactly like a stdout mismatch does — neither side can hide behind the
other agreeing. A "VERIFY OK" therefore certifies two things at once: the run matches the
recorded ground truth, *and* it was internally reproducible on the verifier's own machine.

**Baseline provenance:** `simulation_commit` is blank for this initial snapshot.
The aggregate fingerprints identify the expected simulation outputs without requiring
an earlier Git history. If a later baseline records a commit SHA, `--verify` warns
when HEAD differs and continues; aggregate mismatches and sanity-gate failures cause
verification to fail.

Run `--verify` any time to confirm your checkout reproduces the recorded numbers:

```bash
dotnet run -c Release --project benchmarks/Meridian.Repro -- --verify
```

### CI

`.github/workflows/repro.yml`'s `repro` job runs on both `windows-latest` and `ubuntu-latest`.
Besides the explicit narrative/stochastic-spy-probe/full-process runs it always ran (full-process
now included at CI scale — 10 seeds × 2 repeats, small enough to run on every push/PR), it also
runs `--verify` directly: now that the full-process aggregate is built from CANONICAL (portable)
fingerprints rather than raw bytes, reproducing `expected_hashes.json`'s full recorded scale
(including full-process at its recorded 40 seeds) is meaningful in CI, not just locally, and a
`--verify` failure fails the job on either OS. The `compare` job additionally asserts that the
explicit runs' `aggregate_fingerprint` — including full-process's — is IDENTICAL between the two
OSes; full-process's raw diagnostics (`distinct_database_files`, the same-environment raw run.db
hash count) are logged but never compared across OSes, since raw bytes are expected to differ
there.

## Independent reproduction

Clone this repository, build in Release mode, and run `--verify` to compare all five
recorded aggregates and check repeat consistency. A successful local run verifies
that checkout on that machine. The CI workflow also compares canonical aggregates
between Windows and Ubuntu; raw SQLite and stdout byte hashes are diagnostic only.
Timing results depend on the machine and are never acceptance gates.

## Evidence

`evidence/2026-08-17-seed-scaling/` preserves the original one-off experiment (`SeedSweep`,
not part of this solution) that motivated turning this into a durable, committed harness. See
`evidence/2026-08-17-seed-scaling/PROVENANCE.md` for its measurement context and headline
numbers.
