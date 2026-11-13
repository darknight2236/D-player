using AudioBench;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    Bench.SelfTest();
    return;
}

static int GetInt(string[] args, string name, int fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int value)
        ? value
        : fallback;
}

static string GetString(string[] args, string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

int sampleRate = GetInt(args, "--sample-rate", 48000);
int channels = GetInt(args, "--channels", 2);
int bufferFrames = GetInt(args, "--buffer-frames", 480);
int seconds = GetInt(args, "--seconds", 10);
int warmupSeconds = GetInt(args, "--warmup", 2);
int runs = GetInt(args, "--runs", 5);
string commit = GetString(args, "--commit", "unknown");
string jsonPath = GetString(args, "--json", "docs/PHASE30-AUDIO-BASELINE.json");
string markdownPath = GetString(args, "--out", "docs/PHASE30-AUDIO-BASELINE.md");

var results = Bench.RunAll(sampleRate, channels, bufferFrames, seconds, warmupSeconds, runs);
Bench.WriteJson(jsonPath, commit, results);
Bench.WriteMarkdown(markdownPath, commit, results);
Console.WriteLine($"AudioBench wrote {jsonPath} and {markdownPath}.");
