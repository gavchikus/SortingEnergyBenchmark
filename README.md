# SortingEnergyBenchmark

**Empirical Evaluation of CPU Energy Consumption and Execution Efficiency of Sorting Algorithms Under Synthetic Background Loads**

* **Authors:** Andrii Chupryna (75564), Danylo Holovatiuk (75600)
* **Institution:** Akademia Finansów i Biznesu Vistula, Warsaw, Poland
* **Course:** Architecture of Computers
* **Repository:** https://github.com/gavchikus/SortingEnergyBenchmark

---

## Overview

This repository provides the C# (.NET 8) benchmarking harness, synthetic CPU load generator, raw timing CSVs, hardware power telemetry logs, and merged energy datasets used to evaluate four sorting algorithms (`InsertionSort`, `QuickSort`, `MergeSort`, and `ParallelMergeSort`) under `0%`, `50%`, and `90%` synthetic background CPU utilization across two hardware architectures:

* **Windows 11 (x86-64):** Intel Core i5-13420H (4 P-cores + 4 E-cores, 12 logical threads, 45 W TDP), power sampled via HWiNFO64 (`CPU Package Power [W]` / Intel RAPL).
* **macOS (ARM64):** Apple M4 (4 P-cores + 6 E-cores, 10 hardware threads), power sampled via `sudo powermetrics` (`CPU Power` in mW).

---

## Repository Structure

```text
SortingEnergyBenchmark/
├── SortingEnergyBenchmark.sln           # Visual Studio / .NET 8 solution file
├── README.md                            # Project documentation and replication guide
├── merged_benchmark_energy.csv          # Cleaned 216-run dataset aligned with power telemetry
├── summary_metrics_by_config.csv        # Aggregated mean/SD metrics and EDP degradation (%)
├── results_macOS_0pct.csv               # Raw macOS timing results (0% background load)
├── results_macOS_50pct.csv              # Raw macOS timing results (50% background load)
├── results_macOS_90pct.csv              # Raw macOS timing results (90% background load)
├── mac_power_0pct.txt                   # Raw macOS powermetrics log (0% background load)
├── mac_power_50pct.txt                  # Raw macOS powermetrics log (50% background load)
├── mac_power_90pct.txt                  # Raw macOS powermetrics log (90% background load)
├── win_power_0pct.CSV                   # Raw Windows HWiNFO64 power log (0% background load)
├── win_power_50pct.CSV                  # Raw Windows HWiNFO64 power log (50% background load)
├── win_power_90pct.CSV                  # Raw Windows HWiNFO64 power log (90% background load)
└── SortingEnergyBenchmark/              # C# project directory
    ├── SortingEnergyBenchmark.csproj    # .NET 8 project configuration
    ├── Program.cs                       # Benchmark runner, sorting algorithms, and stress generator
    ├── results_Windows_0pct.csv         # Raw Windows timing results (0% background load)
    ├── results_Windows_50pct.csv        # Raw Windows timing results (50% background load)
    └── results_Windows_90pct.csv        # Raw Windows timing results (90% background load)