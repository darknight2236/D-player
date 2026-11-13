# Phase 30 AudioBench

- Commit: `a231649`
- OS: Microsoft Windows NT 10.0.26300.0
- Runtime: 11.0.0
- Processor: AMD64 Family 25 Model 68 Stepping 1, AuthenticAMD

| Scenario | sr/ch | buffer | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |
|---|---|---|---|---|---|---|---|---|---|
| eq-off | 48000/2 | 480 | 0.0001 | 0.0002 | 0.0003 | 0.0057 | 0.018 | 40 | 40 |
| eq-flat | 48000/2 | 480 | 0.0001 | 0.0002 | 0.0002 | 0.0081 | 0.180 | 40 | 40 |
| eq-bass-boost | 48000/2 | 480 | 0.0376 | 0.0451 | 0.0572 | 0.5063 | 3.164 | 40 | 40 |
| aggregator | 48000/2 | 480 | 0.0049 | 0.0080 | 0.0091 | 0.0956 | 0.533 | 40 | 4113 |
| full-chain | 48000/2 | 480 | 0.0195 | 0.0282 | 0.0312 | 0.1639 | 2.031 | 40 | 16942 |
