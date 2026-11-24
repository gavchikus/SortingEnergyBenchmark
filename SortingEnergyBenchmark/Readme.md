# Cross-Architecture Empirical Energy & Performance Benchmarking of Sorting Algorithms Under Variable CPU Contention

**Authors:** Andrii Chupryna, Danylo Holovatyuk  
**Course:** Computer Architecture — Student Research Challenge (Fall 2026)  

---

## 1. Overview
This repository contains the source code, synthetic CPU contention harness, raw hardware power telemetry, and execution logs for our empirical study evaluating how background CPU load ($0\%$, $50\%$, and $90\%$) impacts the execution time and energy efficiency of classic and parallel sorting algorithms across two distinct hybrid-core architectures:
* **x86-64 Platform:** Windows 11 on Intel Core i5-13420H (Performance + Efficiency cores)
* **ARM64 Platform:** macOS on Apple M4 (Performance + Efficiency cores)

Because hardware power telemetry interfaces (`HWiNFO64` / Intel RAPL on Windows vs. `powermetrics` on macOS) sample distinct power domains, this study uses a **within-platform delta-energy methodology** ($E_{\text{net}} = E_{\text{combined}} - E_{\text{baseline}}$) and compares relative efficiency degradation and Energy-Delay Product ($\text{EDP} = E_{\text{net}} \times T$) across contention regimes.

---

## 2. Repository Structure

```text
SortingEnergyBenchmark/
├── SortingEnergyBenchmark.sln
├── README.md
├── SortingEnergyBenchmark/
│   ├── Program.cs                        # Benchmark harness, sorting algorithms, & CPU stressor
│   └── SortingEnergyBenchmark.csproj     # .NET 9 Release configuration (TieredCompilation disabled)
└── data/                                 # Raw experimental outputs (12 files total)
    ├── results_Windows_0pct.csv          # Windows timing & Unix timestamp windows (0% load)
    ├── results_Windows_50pct.csv         # Windows timing & Unix timestamp windows (50% load)
    ├── results_Windows_90pct.csv         # Windows timing & Unix timestamp windows (90% load)
    ├── win_power_0pct.csv                # HWiNFO64 CPU Package Power telemetry (0% load)
    ├── win_power_50pct.csv               # HWiNFO64 CPU Package Power telemetry (50% load)
    ├── win_power_90pct.csv               # HWiNFO64 CPU Package Power telemetry (90% load)
    ├── results_macOS_0pct.csv            # macOS timing & Unix timestamp windows (0% load)
    ├── results_macOS_50pct.csv           # macOS timing & Unix timestamp windows (50% load)
    ├── results_macOS_90pct.csv           # macOS timing & Unix timestamp windows (90% load)
    ├── mac_power_0pct.txt                # macOS powermetrics CPU power telemetry (0% load)
    ├── mac_power_50pct.txt               # macOS powermetrics CPU power telemetry (50% load)
    └── mac_power_90pct.txt               # macOS powermetrics CPU power telemetry (90% load)