# Phase 30 AudioBench

- Commit: `e46579e`
- OS: Microsoft Windows NT 10.0.26300.0
- Runtime: 11.0.0
- Processor: AMD64 Family 25 Model 68 Stepping 1, AuthenticAMD

| Scenario | sr/ch | buffer | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |
|---|---|---|---|---|---|---|---|---|---|
| eq-off | 48000/2 | 480 | 0.0001 | 0.0002 | 0.0003 | 0.0024 | 0.016 | 40 | 40 |
| eq-flat | 48000/2 | 480 | 0.0463 | 0.1061 | 0.1169 | 0.7657 | 6.251 | 40 | 40 |
| eq-bass-boost | 48000/2 | 480 | 0.0463 | 0.0516 | 0.0696 | 0.2738 | 4.740 | 40 | 40 |
| aggregator | 48000/2 | 480 | 0.0044 | 0.2415 | 0.2531 | 0.3848 | 3.111 | 17824 | 17824 |
| full-chain | 48000/2 | 480 | 0.0523 | 0.2801 | 0.3371 | 3.9939 | 7.885 | 17824 | 17824 |
