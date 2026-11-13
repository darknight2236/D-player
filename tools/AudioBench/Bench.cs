using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DPlayer.Models;
using DPlayer.Services;

namespace AudioBench;

internal sealed record Measurement(
    double P50Ms, double P95Ms, double P99Ms, double MaxMs,
    double CpuMsPerAudioSecond, long AllocatedBytesPerRun,
    long ProcessAllocatedBytesPerRun, int Reads);

internal sealed record ScenarioResult(
    string Name, int SampleRate, int Channels, int BufferFrames,
    int Seconds, int Runs, Measurement Measurement);

internal sealed class SyntheticSampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;

    public SyntheticSampleProvider(int sampleRate, int channels, double frequency = 440)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _samples = new float[sampleRate * channels];
        for (int i = 0; i < sampleRate; i++)
        {
            float sample = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
            for (int c = 0; c < channels; c++)
                _samples[i * channels + c] = sample;
        }
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        int copied = 0;
        while (copied < buffer.Length)
        {
            int count = Math.Min(buffer.Length - copied, _samples.Length - _position);
            _samples.AsSpan(_position, count).CopyTo(buffer.Slice(copied));
            _position = (_position + count) % _samples.Length;
            copied += count;
        }
        return buffer.Length;
    }
}

internal static class Bench
{
    private sealed class ChainProvider : ISampleProvider, IDisposable
    {
        private readonly ISampleProvider _source;
        private readonly object? _owner;

        public ChainProvider(ISampleProvider source, object? owner)
        {
            _source = source;
            _owner = owner;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;
        public int Read(Span<float> buffer) => _source.Read(buffer);
        public void Dispose() => (_owner as IDisposable)?.Dispose();
    }
    public static void SelfTest()
    {
        var sorted = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        AssertNear(Percentile(sorted, 0.50), 50.5, "p50");
        AssertNear(Percentile(sorted, 0.95), 95.05, "p95");
        AssertNear(Percentile(sorted, 0.99), 99.01, "p99");
        Console.WriteLine("AudioBench self-test passed.");
    }

    private static void AssertNear(double actual, double expected, string name)
    {
        if (Math.Abs(actual - expected) > 0.001)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        double rank = (sorted.Length - 1) * p;
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        if (low == high) return sorted[low];
        double weight = rank - low;
        return sorted[low] + (sorted[high] - sorted[low]) * weight;
    }

    public static IReadOnlyList<ScenarioResult> RunAll(
        int sampleRate, int channels, int bufferFrames, int seconds, int warmupSeconds, int runs)
    {
        var results = new List<ScenarioResult>();

        results.Add(Measure("eq-off", () =>
            new EqualizerSampleProvider(
                new SyntheticSampleProvider(sampleRate, channels),
                new EqualizerConfig { Enabled = false }),
            sampleRate, channels, bufferFrames, seconds, warmupSeconds, runs));

        results.Add(Measure("eq-flat", () =>
            new EqualizerSampleProvider(
                new SyntheticSampleProvider(sampleRate, channels),
                EqualizerConfig.Create(true, 0, null, EqualizerPresets.Flat)),
            sampleRate, channels, bufferFrames, seconds, warmupSeconds, runs));

        var bass = EqualizerPresets.All.First(p => p.Name == "Bass Boost").Gains;
        results.Add(Measure("eq-bass-boost", () =>
            new EqualizerSampleProvider(
                new SyntheticSampleProvider(sampleRate, channels),
                EqualizerConfig.Create(true, 0, bass, "Bass Boost")),
            sampleRate, channels, bufferFrames, seconds, warmupSeconds, runs));

        results.Add(Measure("aggregator", () =>
        {
            var aggregator = new SampleAggregator(
                new SyntheticSampleProvider(sampleRate, channels),
                new SpectrumConfig());
            aggregator.SpectrumDataReady += _ => { };
            return aggregator;
        }, sampleRate, channels, bufferFrames, seconds, warmupSeconds, runs));

        results.Add(Measure("full-chain", () =>
        {
            var eq = new EqualizerSampleProvider(
                new SyntheticSampleProvider(sampleRate, channels),
                EqualizerConfig.Create(true, 0, bass, "Bass Boost"));
            var aggregator = new SampleAggregator(eq, new SpectrumConfig());
            aggregator.SpectrumDataReady += _ => { };
            return new ChainProvider(new VolumeSampleProvider(aggregator) { Volume = 0.8f }, aggregator);
        }, sampleRate, channels, bufferFrames, seconds, warmupSeconds, runs));

        return results;
    }

    private static ScenarioResult Measure(
        string name,
        Func<ISampleProvider> create,
        int sampleRate,
        int channels,
        int bufferFrames,
        int seconds,
        int warmupSeconds,
        int runs)
    {
        var pooled = new List<double>(capacity: Math.Max(1, runs * (int)((long)sampleRate * seconds / bufferFrames)));
        long allocated = 0;
        long processAllocated = 0;
        double cpuMs = 0;

        for (int run = 0; run < runs; run++)
        {
            var provider = create();
            try
            {
                var buffer = new float[bufferFrames * channels];
                int warmupReads = Math.Max(1, sampleRate * warmupSeconds / bufferFrames);
                for (int i = 0; i < warmupReads; i++) provider.Read(buffer);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int reads = Math.Max(1, (int)((long)sampleRate * seconds / bufferFrames));
                var samples = new double[reads];
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                long processAllocBefore = GC.GetTotalAllocatedBytes(precise: true);

                var total = Stopwatch.StartNew();
                for (int i = 0; i < reads; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    provider.Read(buffer);
                    samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
                total.Stop();

                allocated += GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                processAllocated += GC.GetTotalAllocatedBytes(precise: true) - processAllocBefore;
                cpuMs += total.Elapsed.TotalMilliseconds;
                Array.Sort(samples);
                pooled.AddRange(samples);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }

        pooled.Sort();
        var measurement = new Measurement(
            P50Ms: Percentile(pooled.ToArray(), 0.50),
            P95Ms: Percentile(pooled.ToArray(), 0.95),
            P99Ms: Percentile(pooled.ToArray(), 0.99),
            MaxMs: pooled.Count == 0 ? 0 : pooled[^1],
            CpuMsPerAudioSecond: cpuMs / Math.Max(1, runs * seconds),
            AllocatedBytesPerRun: allocated / Math.Max(1, runs),
            ProcessAllocatedBytesPerRun: processAllocated / Math.Max(1, runs),
            Reads: pooled.Count);

        return new ScenarioResult(name, sampleRate, channels, bufferFrames, seconds, runs, measurement);
    }

    public static void WriteJson(string path, string commit, IReadOnlyList<ScenarioResult> results)
    {
        var payload = new
        {
            commit,
            os = Environment.OSVersion.ToString(),
            runtime = Environment.Version.ToString(),
            processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            timestampUtc = DateTime.UtcNow,
            results
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void WriteMarkdown(string path, string commit, IReadOnlyList<ScenarioResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 30 AudioBench");
        sb.AppendLine();
        sb.AppendLine($"- Commit: `{commit}`");
        sb.AppendLine($"- OS: {Environment.OSVersion}");
        sb.AppendLine($"- Runtime: {Environment.Version}");
        sb.AppendLine($"- Processor: {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}");
        sb.AppendLine();
        sb.AppendLine("| Scenario | sr/ch | buffer | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            sb.AppendLine($"| {r.Name} | {r.SampleRate}/{r.Channels} | {r.BufferFrames} | {r.Measurement.P50Ms:F4} | {r.Measurement.P95Ms:F4} | {r.Measurement.P99Ms:F4} | {r.Measurement.MaxMs:F4} | {r.Measurement.CpuMsPerAudioSecond:F3} | {r.Measurement.AllocatedBytesPerRun} | {r.Measurement.ProcessAllocatedBytesPerRun} |");
        }
        File.WriteAllText(path, sb.ToString());
    }
}
