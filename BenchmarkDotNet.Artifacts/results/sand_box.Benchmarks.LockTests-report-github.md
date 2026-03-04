```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 7 5700G with Radeon Graphics 3.80GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  Job-CNUJVU : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3

InvocationCount=1  UnrollFactor=1  

```
| Method           | Parallelism | Mean       | Error      | StdDev     | Median     | Ratio         | RatioSD | Completed Work Items | Lock Contentions | Allocated | Alloc Ratio |
|----------------- |------------ |-----------:|-----------:|-----------:|-----------:|--------------:|--------:|---------------------:|-----------------:|----------:|------------:|
| **LockSimple**       | **1**           |   **7.488 ms** |  **0.0378 ms** |  **0.0315 ms** |   **7.493 ms** |      **baseline** |        **** |                    **-** |                **-** |   **1.45 KB** |            **** |
| LockSimpleCopy1  | 1           |   7.510 ms |  0.0864 ms |  0.0808 ms |   7.503 ms |  1.00x slower |   0.01x |                    - |                - |   1.45 KB |  1.00x more |
| LockSimpleCopy2  | 1           |   4.422 ms |  0.0855 ms |  0.2236 ms |   4.334 ms |  1.70x faster |   0.08x |                    - |                - |   1.45 KB |  1.00x more |
| LockPrecondition | 1           |   4.281 ms |  0.0405 ms |  0.0780 ms |   4.252 ms |  1.75x faster |   0.03x |                    - |                - |   1.45 KB |  1.00x more |
| LockFreeValue    | 1           |   2.191 ms |  0.0082 ms |  0.0068 ms |   2.192 ms |  3.42x faster |   0.02x |                    - |                - |   1.45 KB |  1.00x more |
| LockFreeSection  | 1           |   3.789 ms |  0.0138 ms |  0.0116 ms |   3.790 ms |  1.98x faster |   0.01x |                    - |                - |   1.45 KB |  1.00x more |
| LockSpin         | 1           |  37.888 ms |  0.1227 ms |  0.0958 ms |  37.881 ms |  5.06x slower |   0.02x |                    - |                - |   1.45 KB |  1.00x more |
| LockSpinCustom   | 1           |   4.665 ms |  0.0910 ms |  0.1334 ms |   4.630 ms |  1.61x faster |   0.04x |                    - |                - |   1.45 KB |  1.00x more |
| LockSpinWait     | 1           |   3.764 ms |  0.0141 ms |  0.0132 ms |   3.759 ms |  1.99x faster |   0.01x |                    - |                - |   1.45 KB |  1.00x more |
|                  |             |            |            |            |            |               |         |                      |                  |           |             |
| **LockSimple**       | **2**           |  **37.898 ms** |  **1.0908 ms** |  **3.2163 ms** |  **38.752 ms** |      **baseline** |        **** |               **1.0000** |           **1.0000** |   **1.66 KB** |            **** |
| LockSimpleCopy1  | 2           |  38.258 ms |  1.8861 ms |  5.5612 ms |  40.143 ms |  1.02x slower |   0.18x |               1.0000 |           4.0000 |   1.66 KB |  1.00x more |
| LockSimpleCopy2  | 2           |  45.134 ms |  0.9167 ms |  2.6741 ms |  45.263 ms |  1.20x slower |   0.14x |               1.0000 |           2.0000 |   1.72 KB |  1.04x more |
| LockPrecondition | 2           |  21.071 ms |  0.5318 ms |  1.5681 ms |  21.583 ms |  1.81x faster |   0.21x |               1.0000 |           5.0000 |   1.66 KB |  1.00x more |
| LockFreeValue    | 2           |  25.158 ms |  0.6095 ms |  1.7971 ms |  26.120 ms |  1.51x faster |   0.17x |               1.0000 |                - |   1.66 KB |  1.00x more |
| LockFreeSection  | 2           |  52.768 ms |  1.0548 ms |  2.5875 ms |  53.906 ms |  1.40x slower |   0.15x |               1.0000 |                - |   1.66 KB |  1.00x more |
| LockSpin         | 2           |  40.154 ms |  0.5757 ms |  1.2994 ms |  39.885 ms |  1.07x slower |   0.11x |               1.0000 |                - |   1.66 KB |  1.00x more |
| LockSpinCustom   | 2           |  77.466 ms |  1.4614 ms |  1.6244 ms |  77.857 ms |  2.06x slower |   0.20x |               1.0000 |                - |   1.66 KB |  1.00x more |
| LockSpinWait     | 2           |  18.306 ms |  1.9659 ms |  5.7966 ms |  17.482 ms |  2.28x faster |   0.71x |               1.0000 |                - |   1.66 KB |  1.00x more |
|                  |             |            |            |            |            |               |         |                      |                  |           |             |
| **LockSimple**       | **4**           | **218.411 ms** |  **4.3355 ms** |  **6.0778 ms** | **221.147 ms** |      **baseline** |        **** |               **4.0000** |         **280.0000** |    **2.2 KB** |            **** |
| LockSimpleCopy1  | 4           | 214.395 ms |  2.2691 ms |  2.1226 ms | 214.793 ms |  1.02x faster |   0.03x |               4.0000 |         293.0000 |    2.2 KB |  1.00x more |
| LockSimpleCopy2  | 4           | 183.855 ms |  3.6212 ms |  7.1479 ms | 186.825 ms |  1.19x faster |   0.06x |               3.0000 |         124.0000 |    2.2 KB |  1.00x more |
| LockPrecondition | 4           |  64.413 ms |  1.2757 ms |  2.0235 ms |  65.071 ms |  3.39x faster |   0.14x |               3.0000 |         139.0000 |   2.08 KB |  1.06x less |
| LockFreeValue    | 4           |  60.570 ms |  1.2026 ms |  1.8000 ms |  60.858 ms |  3.61x faster |   0.14x |               3.0000 |                - |   2.08 KB |  1.06x less |
| LockFreeSection  | 4           | 120.597 ms |  2.3431 ms |  3.2073 ms | 120.490 ms |  1.81x faster |   0.07x |               3.0000 |                - |   2.08 KB |  1.06x less |
| LockSpin         | 4           | 184.437 ms |  3.6211 ms |  6.4366 ms | 185.381 ms |  1.19x faster |   0.05x |               4.0000 |                - |   2.14 KB |  1.03x less |
| LockSpinCustom   | 4           | 170.940 ms |  3.3073 ms |  3.3963 ms | 170.970 ms |  1.28x faster |   0.04x |               3.0000 |                - |   2.08 KB |  1.06x less |
| LockSpinWait     | 4           |  23.448 ms |  4.1452 ms | 11.6915 ms |  15.235 ms | 11.57x faster |   4.79x |               3.0000 |                - |   2.14 KB |  1.03x less |
|                  |             |            |            |            |            |               |         |                      |                  |           |             |
| **LockSimple**       | **8**           | **640.775 ms** |  **1.7770 ms** |  **1.6622 ms** | **641.173 ms** |      **baseline** |        **** |              **13.0000** |        **7646.0000** |   **3.36 KB** |            **** |
| LockSimpleCopy1  | 8           | 822.160 ms |  2.2170 ms |  2.0738 ms | 822.582 ms |  1.28x slower |   0.00x |              13.0000 |       10834.0000 |  15.41 KB |  4.59x more |
| LockSimpleCopy2  | 8           | 811.961 ms | 15.8029 ms | 24.6032 ms | 814.344 ms |  1.27x slower |   0.04x |              13.0000 |        9776.0000 |   3.36 KB |  1.00x more |
| LockPrecondition | 8           | 134.097 ms |  0.9856 ms |  0.9220 ms | 134.091 ms |  4.78x faster |   0.03x |               7.0000 |         796.0000 |   2.98 KB |  1.13x less |
| LockFreeValue    | 8           | 109.071 ms |  1.7997 ms |  1.5954 ms | 108.955 ms |  5.88x faster |   0.08x |               7.0000 |                - |   2.92 KB |  1.15x less |
| LockFreeSection  | 8           | 250.089 ms |  2.3726 ms |  2.2194 ms | 250.030 ms |  2.56x faster |   0.02x |               7.0000 |                - |   2.92 KB |  1.15x less |
| LockSpin         | 8           | 400.016 ms |  2.9850 ms |  2.7922 ms | 400.382 ms |  1.60x faster |   0.01x |              10.0000 |                - |   3.11 KB |  1.08x less |
| LockSpinCustom   | 8           | 389.378 ms |  2.9349 ms |  2.7453 ms | 389.634 ms |  1.65x faster |   0.01x |              10.0000 |                - |   3.11 KB |  1.08x less |
| LockSpinWait     | 8           |  34.341 ms |  6.1344 ms | 17.4022 ms |  29.534 ms | 25.00x faster |  14.19x |               7.0000 |                - |   3.05 KB |  1.10x less |
