using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SortingEnergyBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            string mode = GetArg(args, "--mode", "benchmark").ToLowerInvariant();

            if (mode == "stress")
            {
                int targetLoad = int.Parse(GetArg(args, "--load", "50"));
                int durationSec = int.Parse(GetArg(args, "--duration", "600"));
                RunCpuStressGenerator(targetLoad, durationSec);
                return;
            }

            if (mode == "benchmark")
            {
                string tag = GetArg(args, "--tag", "0pct");
                int arraySize = int.Parse(GetArg(args, "--size", "50000"));
                int windowSeconds = int.Parse(GetArg(args, "--window", "15"));
                int repeats = int.Parse(GetArg(args, "--repeats", "3"));
                int cooldownSec = int.Parse(GetArg(args, "--cooldown", "20"));

                RunBenchmarkSuite(tag, arraySize, windowSeconds, repeats, cooldownSec);
                return;
            }

            Console.WriteLine("Usage:");
            Console.WriteLine("  Stress mode:    dotnet run -c Release -- --mode stress --load 50 --duration 600");
            Console.WriteLine("  Benchmark mode: dotnet run -c Release -- --mode benchmark --tag 0pct --size 50000 --window 15 --repeats 3 --cooldown 20");
        }

        // =========================================================================
        // 1. SYNTHETIC CPU LOAD GENERATOR (Duty-Cycle Spin-Wait across all cores)
        // =========================================================================
        static void RunCpuStressGenerator(int targetLoadPercent, int durationSeconds)
        {
            targetLoadPercent = Math.Clamp(targetLoadPercent, 1, 99);
            int coreCount = Environment.ProcessorCount;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"[Stress] Starting {targetLoadPercent}% synthetic CPU load across {coreCount} logical cores for {durationSeconds}s...");
            Console.WriteLine("[Stress] Press Ctrl+C to stop early.");

            // 100 ms PWM duty cycle per core (e.g., 50 ms spin, 50 ms sleep for 50% load)
            double cycleMs = 100.0;
            double busyMs = cycleMs * (targetLoadPercent / 100.0);
            double sleepMs = cycleMs - busyMs;

            Task[] workers = new Task[coreCount];
            for (int c = 0; c < coreCount; c++)
            {
                workers[c] = Task.Run(() =>
                {
                    var sw = new Stopwatch();
                    double acc = 1.0001;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        sw.Restart();
                        while (sw.Elapsed.TotalMilliseconds < busyMs)
                        {
                            // Pure ALU floating-point work (minimal cache/memory footprint)
                            acc = Math.Sqrt(acc * 1.000001 + 0.5);
                            if (acc > 1e9) acc = 1.0001;
                        }

                        if (sleepMs >= 1.0)
                        {
                            Thread.Sleep((int)Math.Round(sleepMs));
                        }
                    }
                }, cts.Token);
            }

            try
            {
                Task.WaitAll(workers);
            }
            catch (AggregateException) { }

            Console.WriteLine("[Stress] Synthetic load finished.");
        }

        // =========================================================================
        // 2. SUSTAINED BENCHMARK HARNESS (Aligned for Energy Telemetry)
        // =========================================================================
        record BenchmarkCase(string Algorithm, string Distribution, int Threads);

        static void RunBenchmarkSuite(string loadTag, int arraySize, int windowSeconds, int repeats, int cooldownSec)
        {
            string osName = OperatingSystem.IsMacOS() ? "macOS" : (OperatingSystem.IsWindows() ? "Windows" : "Linux");
            string fileName = $"results_{osName}_{loadTag}.csv";

            // Algorithms & thread configurations to test
            string[] distributions = { "Random", "ReverseSorted" };
            var baseCases = new List<BenchmarkCase>();

            foreach (var dist in distributions)
            {
                baseCases.Add(new BenchmarkCase("InsertionSort", dist, 1));
                baseCases.Add(new BenchmarkCase("QuickSort", dist, 1));
                baseCases.Add(new BenchmarkCase("MergeSort", dist, 1));
                baseCases.Add(new BenchmarkCase("ParallelMergeSort", dist, 2));
                baseCases.Add(new BenchmarkCase("ParallelMergeSort", dist, 4));
                baseCases.Add(new BenchmarkCase("ParallelMergeSort", dist, Environment.ProcessorCount));
            }

            // Pre-generate reference arrays outside measurement loops to prevent GC spikes
            var referenceArrays = new Dictionary<string, int[]>();
            foreach (var dist in distributions)
            {
                referenceArrays[dist] = GenerateArray(arraySize, dist, seed: 42);
            }

            // Force full JIT compilation of every sorting path before timing or energy capture
            Console.WriteLine("[Init] Running JIT warm-up and correctness validation...");
            WarmUpAndValidate(referenceArrays);

            // Ensure clean GC state before starting
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var writer = new StreamWriter(fileName);
            writer.WriteLine("OS,LoadCondition,Repeat,OrderMode,Algorithm,Threads,Distribution,ArraySize,StartUnixMs,EndUnixMs,TotalSortsCompleted,ElapsedSeconds,AvgTimePerSortMs,ThroughputSortsPerSec");

            Console.WriteLine($"=== Starting Benchmark | OS: {osName} | LoadTag: {loadTag} | N={arraySize} | Window={windowSeconds}s ===");
            Console.WriteLine($"{"Rep",-4} | {"Algorithm",-18} | {"Thr",-3} | {"Dist",-13} | {"Sorts",-7} | {"Avg (ms)",-10}");
            Console.WriteLine(new string('-', 72));

            Random shuffleRng = new Random(12345);

            for (int rep = 1; rep <= repeats; rep++)
            {
                // Counterbalance execution order across repeats to neutralize thermal drift bias
                List<BenchmarkCase> orderedCases;
                string orderMode;
                if (rep % 3 == 1)
                {
                    orderedCases = new List<BenchmarkCase>(baseCases);
                    orderMode = "Forward";
                }
                else if (rep % 3 == 2)
                {
                    orderedCases = baseCases.AsEnumerable().Reverse().ToList();
                    orderMode = "Reverse";
                }
                else
                {
                    orderedCases = baseCases.OrderBy(_ => shuffleRng.Next()).ToList();
                    orderMode = "Shuffled";
                }

                foreach (var test in orderedCases)
                {
                    int[] source = referenceArrays[test.Distribution];
                    int[] workBuffer = new int[arraySize];

                    // Brief 2-second stabilization micro-warmup for CPU frequency ramp-up
                    RunSingleSort(test, source, workBuffer);

                    long startUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    Stopwatch sw = Stopwatch.StartNew();
                    long sortsCompleted = 0;

                    while (sw.Elapsed.TotalSeconds < windowSeconds)
                    {
                        // Fast unmanaged block copy resets array distribution without heap allocation
                        Buffer.BlockCopy(source, 0, workBuffer, 0, arraySize * sizeof(int));
                        ExecuteAlgorithm(test.Algorithm, workBuffer, test.Threads);
                        sortsCompleted++;
                    }

                    sw.Stop();
                    long endUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    double elapsedSec = sw.Elapsed.TotalSeconds;
                    double avgMsPerSort = (elapsedSec * 1000.0) / sortsCompleted;
                    double throughput = sortsCompleted / elapsedSec;

                    writer.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11:F4},{12:F4},{13:F2}",
                        osName, loadTag, rep, orderMode, test.Algorithm, test.Threads,
                        test.Distribution, arraySize, startUnixMs, endUnixMs,
                        sortsCompleted, elapsedSec, avgMsPerSort, throughput));
                    writer.Flush();

                    Console.WriteLine($"{rep,-4} | {test.Algorithm,-18} | {test.Threads,-3} | {test.Distribution,-13} | {sortsCompleted,-7} | {avgMsPerSort,-10:F3}");

                    // Cooldown window to let die temperature stabilize between sustained bursts
                    if (cooldownSec > 0)
                    {
                        Thread.Sleep(cooldownSec * 1000);
                    }
                }
            }

            Console.WriteLine($"\n[Done] Benchmark results saved to '{fileName}'.");
        }

        // =========================================================================
        // 3. SORTING ALGORITHMS (Insertion, Median-of-3 QuickSort, Seq/Par MergeSort)
        // =========================================================================
        static void ExecuteAlgorithm(string algorithm, int[] array, int threads)
        {
            switch (algorithm)
            {
                case "InsertionSort":
                    InsertionSort(array);
                    break;
                case "QuickSort":
                    QuickSort(array, 0, array.Length - 1);
                    break;
                case "MergeSort":
                    int[] tempSeq = new int[array.Length];
                    MergeSortSequential(array, tempSeq, 0, array.Length - 1);
                    break;
                case "ParallelMergeSort":
                    int[] tempPar = new int[array.Length];
                    int maxDepth = (int)Math.Ceiling(Math.Log2(Math.Max(1, threads)));
                    MergeSortParallel(array, tempPar, 0, array.Length - 1, 0, maxDepth);
                    break;
                default:
                    throw new ArgumentException($"Unknown algorithm: {algorithm}");
            }
        }

        static void InsertionSort(int[] array)
        {
            for (int i = 1; i < array.Length; i++)
            {
                int key = array[i];
                int j = i - 1;
                while (j >= 0 && array[j] > key)
                {
                    array[j + 1] = array[j];
                    j--;
                }
                array[j + 1] = key;
            }
        }

        // Median-of-Three QuickSort: prevents O(N) recursion stack overflow on Sorted/ReverseSorted arrays
        static void QuickSort(int[] array, int low, int high)
        {
            while (low < high)
            {
                int pivotIndex = PartitionMedianOfThree(array, low, high);
                if (pivotIndex - low < high - pivotIndex)
                {
                    QuickSort(array, low, pivotIndex - 1);
                    low = pivotIndex + 1;
                }
                else
                {
                    QuickSort(array, pivotIndex + 1, high);
                    high = pivotIndex - 1;
                }
            }
        }

        static int PartitionMedianOfThree(int[] array, int low, int high)
        {
            int mid = low + ((high - low) >> 1);
            if (array[mid] < array[low]) Swap(array, low, mid);
            if (array[high] < array[low]) Swap(array, low, high);
            if (array[mid] < array[high]) Swap(array, mid, high);

            int pivot = array[high];
            int i = low - 1;
            for (int j = low; j < high; j++)
            {
                if (array[j] <= pivot)
                {
                    i++;
                    Swap(array, i, j);
                }
            }
            Swap(array, i + 1, high);
            return i + 1;
        }

        static void MergeSortSequential(int[] array, int[] temp, int left, int right)
        {
            if (left >= right) return;
            int mid = left + ((right - left) >> 1);
            MergeSortSequential(array, temp, left, mid);
            MergeSortSequential(array, temp, mid + 1, right);
            Merge(array, temp, left, mid, right);
        }

        static void MergeSortParallel(int[] array, int[] temp, int left, int right, int depth, int maxDepth)
        {
            if (left >= right) return;

            // Threshold avoids thread-spawning overhead on tiny sub-arrays
            if (depth >= maxDepth || (right - left) < 2048)
            {
                MergeSortSequential(array, temp, left, right);
                return;
            }

            int mid = left + ((right - left) >> 1);
            Parallel.Invoke(
                () => MergeSortParallel(array, temp, left, mid, depth + 1, maxDepth),
                () => MergeSortParallel(array, temp, mid + 1, right, depth + 1, maxDepth)
            );
            Merge(array, temp, left, mid, right);
        }

        static void Merge(int[] array, int[] temp, int left, int mid, int right)
        {
            Array.Copy(array, left, temp, left, right - left + 1);
            int i = left;
            int j = mid + 1;
            int k = left;

            while (i <= mid && j <= right)
            {
                if (temp[i] <= temp[j])
                    array[k++] = temp[i++];
                else
                    array[k++] = temp[j++];
            }

            while (i <= mid)
                array[k++] = temp[i++];
        }

        static void Swap(int[] array, int a, int b)
        {
            int tmp = array[a];
            array[a] = array[b];
            array[b] = tmp;
        }

        // =========================================================================
        // 4. HELPERS (Data Generation, Warm-Up, Validation, CLI Parsing)
        // =========================================================================
        static int[] GenerateArray(int size, string distribution, int seed)
        {
            Random rand = new Random(seed);
            int[] array = Enumerable.Range(0, size).Select(_ => rand.Next()).ToArray();

            if (distribution == "ReverseSorted")
            {
                Array.Sort(array);
                Array.Reverse(array);
            }
            return array;
        }

        static void RunSingleSort(BenchmarkCase test, int[] source, int[] buffer)
        {
            Buffer.BlockCopy(source, 0, buffer, 0, source.Length * sizeof(int));
            ExecuteAlgorithm(test.Algorithm, buffer, test.Threads);
        }

        static void WarmUpAndValidate(Dictionary<string, int[]> referenceArrays)
        {
            string[] algos = { "InsertionSort", "QuickSort", "MergeSort", "ParallelMergeSort" };
            foreach (var kvp in referenceArrays)
            {
                foreach (var algo in algos)
                {
                    int[] testBuf = (int[])kvp.Value.Clone();
                    ExecuteAlgorithm(algo, testBuf, 4);
                    if (!IsSorted(testBuf))
                        throw new Exception($"Validation failed for {algo} on {kvp.Key}!");
                }
            }
        }

        static bool IsSorted(int[] array)
        {
            for (int i = 0; i < array.Length - 1; i++)
            {
                if (array[i] > array[i + 1]) return false;
            }
            return true;
        }

        static string GetArg(string[] args, string key, string defaultValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return defaultValue;
        }
    }
}