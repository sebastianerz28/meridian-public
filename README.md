# Meridian

Meridian is a small multi-agent geopolitics testbed: three countries (Alderia, Borvia,
Cascara) around a contested strait, a deterministic economic/military/political
simulation, and a set of actors — scripted, LLM-driven, or human — that decide one
action per turn. The engine is event-sourced, fully replayable, and resumable from any
saved snapshot.

The core question the project is built to test: **do LLM-driven agents, given only a
bounded, partially-uncertain view of the world and their own memories, produce a
coherent multi-step causal chain of decisions** — not because we scripted one, but
because the underlying economic/military feedback loops make it a locally rational
sequence of moves?

## Prerequisites

- .NET 8 SDK
- (LLM/play modes only) an `ANTHROPIC_API_KEY`, **or** a running `llama-server`
  (llama.cpp) — see [LLM providers](#llm-providers) below.

All commands below assume you run them from the **repository root**.

## Running the tests

```
dotnet test
```

Tests across `Game.Simulation.Tests`, `Game.Agents.Tests`, and `Meridian.Repro.Tests` — deterministic engine
mechanics, event round-trip/replay, save/resume equivalence, observation/estimate
bounds, LLM prompt/parser/telemetry behavior, and client request/error shapes. None of
them touch the network or require an API key.

## Running the headless engine

The headless runner is `src/Game.Headless`. It always creates/uses `run.db` (SQLite) in
the **current working directory** — run these commands from the repo root and `run.db`
lands at the repo root, next to `llm.config.json`.

### Scripted mode (no LLM, no key needed)

A small hand-authored story used to smoke-test the whole engine deterministically:

```
dotnet run --project src/Game.Headless -- --turns 20 --seed 42 --actors scripted
```

Deletes any existing `run.db` and starts fresh. Fully deterministic: the same
`--seed`/`--turns` always produces byte-identical stdout and an identical `run.db`.

### LLM mode

All three countries are driven by an `LlmActor` (Claude by default; see
[LLM providers](#llm-providers)). Requires `ANTHROPIC_API_KEY` (or a running
`llama-server` if you've switched the provider — see below).

```
export ANTHROPIC_API_KEY=sk-ant-...      # bash/zsh
$env:ANTHROPIC_API_KEY = "sk-ant-..."    # PowerShell

dotnet run --project src/Game.Headless -- --turns 20 --seed 7 --actors llm
```

If the key is missing (or `llm.config.json` can't be found/parsed), the program prints
a clear error and **exits with code 1 before deleting or touching `run.db`** — a failed
LLM-mode invocation never destroys a previous run.

Every LLM call (valid or not) is logged to the `llm_calls` table in `run.db` via
`SqliteAgentTelemetry`; a malformed reply gets exactly one corrective retry before the
turn degrades to `Pass` rather than crashing the run (see `LlmActor`). Agent state
(memories, political beliefs, current government, latest self-assessment) is persisted
every turn as JSON in the `snapshots` table (`agents_json`), keyed by country.

At the end of a run you get a final standings table, the full Alderia-view news
history for the run, and each LLM agent's accumulated memories (loaded back from
`run.db`, not just kept in memory).

### Play mode

Same as LLM mode, except Alderia is played by you: it prints your exact stats, the
same kind of bounded intelligence estimates on the other two countries an LLM agent's
prompt would show, and recent news, then reads one line per turn from stdin:

```
dotnet run --project src/Game.Headless -- --turns 20 --actors play
```

Input format: `action [target] [magnitude] [statement...]`, e.g.

```
> ExportControl Borvia 0.6 tightening rare-earth exports in response to their posture
> Reassure Cascara 0.4 we stand by our alliance
> pass
```

`pass` is a shortcut for taking no action. Invalid action/target names re-prompt rather
than crashing or silently defaulting. Borvia and Cascara remain LLM-driven, so play
mode requires the same API key (or llama.cpp setup) as LLM mode, checked up front the
same way.

### Resuming a run

`--resume <path>` opens an existing database instead of deleting it, restores the
latest saved snapshot (world state, engine RNG/observation state, and each agent's
memories/beliefs/government from `agents_json`), and continues turn numbering from
wherever the snapshot left off:

```
# first session — runs turns 1-5, saves a snapshot each turn
dotnet run --project src/Game.Headless -- --turns 5 --seed 42 --actors llm

# later — continues from turn 6, runs 5 more turns (through turn 10)
dotnet run --project src/Game.Headless -- --resume run.db --turns 5 --actors llm
```

`--actors` on the resume invocation must match what you want the countries driven by
going forward (normally the same mode you started with) — it's used to rebuild the
actor list that `TurnEngine.Resume` runs against. `--seed` is ignored on resume; the
RNG continues from the snapshot's saved state.

`--resume` on a nonexistent file fails clearly (exit 1) without creating anything.

## LLM providers

`llm.config.json` (repo root, no secrets) selects the provider:

```json
{
  "provider": "claude",
  "model": "claude-haiku-4-5-20251001",
  "maxTokens": 1024
}
```

**Claude** (default): reads `ANTHROPIC_API_KEY` from the environment. Nothing else to
configure.

**llama.cpp** (local, no API key): start a server exposing the OpenAI-compatible
completion endpoint, e.g.

```
llama-server -m /path/to/model.gguf --port 8080
```

then point `llm.config.json` at it:

```json
{
  "provider": "llamacpp",
  "baseUrl": "http://localhost:8080",
  "maxTokens": 1024
}
```

Program.cs resolves `llm.config.json` by looking in the current directory and then
walking up parent directories (up to 8 levels) until it finds one — so running
`dotnet run --project src/Game.Headless -- ...` from the repo root just works. If you
invoke it from somewhere else entirely, either `cd` to the repo root first or copy
`llm.config.json` alongside wherever you run it from.

## Where `run.db` lives

Wherever the current working directory was when you ran `dotnet run` — normally the
repo root. It's git-ignored (`*.db`). Tables: `events` (append-only, `(turn, ordinal)`
unique), `snapshots` (one row per turn: world/agents/engine JSON, latest wins on
`--resume`), `memories` (per-country, per-turn agent reflections), `llm_calls` (every
attempt, valid or not, with prompt/response/error/latency).

## Evaluation criterion

The experiment this project exists to run: play (or watch) a 20-turn LLM-driven run
and inspect `run.db`'s `events`/`memories`/`llm_calls` plus the printed news history.
**Success** means finding at least one:

1. **Coherent multi-step causal chain that we did not script** — e.g. a deployment
   provokes an export restriction, which drains a stockpile, which triggers a
   shortage, which feeds inflation, which erodes approval, which becomes election
   pressure — where each link is a plausible LLM decision responding to the state the
   simulation actually produced, not a fixed script (contrast with `--actors
   scripted`, whose beats are hand-authored for exactly this reason: it's the known-
   good baseline the LLM run is compared against).
2. **At least one agent statement or memory that references a past event** — evidence
   that the memory/belief mechanism (`AgentState.Memories`, fed back into
   `ContextBuilder.BuildDecisionPrompt`) is actually shaping later decisions, not just
   being written and ignored.

```
dotnet run --project src/Game.Headless -- --turns 20 --seed 7 --actors llm
```

Inspect locally generated databases to evaluate live runs. They contain prompts,
responses, and agent memories and are excluded from version control.

## Reproducibility benchmarks

The [benchmark harness](benchmarks/Meridian.Repro/README.md) checks deterministic
replays, stochastic trajectories, and full-process SQLite persistence. It runs
without an API key and includes recorded aggregate fingerprints for verification.
