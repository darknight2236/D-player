# Phase 30 AudioBench

- Commit: `a231649`
- OS: Microsoft Windows NT 10.0.26300.0
- Runtime: 11.0.0
- Processor: AMD64 Family 25 Model 68 Stepping 1, AuthenticAMD

| Scenario | sr/ch | buffer | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |
|---|---|---|---|---|---|---|---|---|---|
| eq-off | 48000/2 | 480 | 0.0001 | 0.0002 | 0.0003 | 0.0025 | 0.019 | 40 | 40 |
| eq-flat | 48000/2 | 480 | 0.0001 | 0.0002 | 0.0003 | 0.0053 | 0.203 | 40 | 40 |
| eq-bass-boost | 48000/2 | 480 | 0.0380 | 0.0386 | 0.0461 | 0.5196 | 3.041 | 40 | 40 |
| aggregator | 48000/2 | 480 | 0.0048 | 0.0070 | 0.0076 | 0.1110 | 0.506 | 40 | 3596 |
| full-chain | 48000/2 | 480 | 0.0193 | 0.0290 | 0.0330 | 0.1274 | 2.028 | 40 | 17459 |
