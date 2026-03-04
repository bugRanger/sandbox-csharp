```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 7 5700G with Radeon Graphics 3.80GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3


```
| Method           | Iterations | Mean             | Error         | StdDev        | Gen0     | Completed Work Items | Lock Contentions | Allocated |
|----------------- |----------- |-----------------:|--------------:|--------------:|---------:|---------------------:|-----------------:|----------:|
| **UseTryCatch**      | **100**        |    **145,625.27 ns** |    **773.439 ns** |    **723.475 ns** |   **3.6621** |                    **-** |                **-** |   **32000 B** |
| UseResultPattern | 100        |         27.60 ns |      0.073 ns |      0.065 ns |        - |                    - |                - |         - |
| **UseTryCatch**      | **1000**       |  **1,444,404.30 ns** | **13,955.043 ns** | **10,895.180 ns** |  **37.1094** |                    **-** |                **-** |  **320000 B** |
| UseResultPattern | 1000       |        226.70 ns |      0.529 ns |      0.495 ns |        - |                    - |                - |         - |
| **UseTryCatch**      | **10000**      | **14,628,351.20 ns** | **55,529.699 ns** | **46,369.814 ns** | **375.0000** |                    **-** |                **-** | **3200000 B** |
| UseResultPattern | 10000      |      2,222.09 ns |      6.921 ns |      5.779 ns |        - |                    - |                - |         - |
