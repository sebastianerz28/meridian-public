# Meridian seed-scaling benchmark

## Results

- Full process seeds: 40, two fresh executable/SQLite runs each
- Full process mismatches: 0
- Full process distinct stdout trajectories: 1
- Full process distinct databases: 40 (seed metadata is persisted)
- Stochastic probe seeds: 10,000, two 20-turn engine runs each
- Stochastic probe total turns: 400,000
- Stochastic probe mismatches: 0
- Stochastic probe distinct behavioral trajectories: 10,000
- Narrative seeds consuming RNG: 0 / 40
- Narrative distinct behavioral trajectories: 1

## Timing plots

- `full_process_pair_latency.svg`
- `inprocess_cumulative_scaling.svg`
- `inprocess_loglog_scaling.svg`
- `mode_cost_comparison.svg`
