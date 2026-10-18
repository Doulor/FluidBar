# FluidBar performance baseline — headless platform host (20261018-112228)

**Mode:** headless (guarded \Soak_IdlePlatformGraph\) — published App not sampled.
**Scenario duration:** 180 s

| Metric | Value |
|---|---|
| Startup (graph start) | 333 ms |
| Idle CPU (avg) | 0.022 % |
| Working set start → end | 116.5 MB → 116.5 MB |
| Handles start → end | 685 → 685 |
| Threads start → end | 32 → 32 |
| Gen2 GCs during run | 0 |

Machine: AMD Ryzen 9 9955HX 16-Core Processor            (16C/32T), 31.2 GB RAM, Microsoft Windows 10.0.26200, dotnet 10.0.301
Source evidence: `soak-idle-platform-20261018-112529.json`
