using System.Diagnostics;
using System.Text;

namespace TMLDroid.Probes;

public enum ProbeStatus { Pass, Fail, Skip, Info }

public sealed record ProbeResult(string Name, ProbeStatus Status, string Detail, double Ms);

public sealed class ProbeSkipException(string reason) : Exception(reason);

public sealed class ProbeAssertException(string message) : Exception(message);

public static class ProbeRunner
{
    public static void Assert(bool condition, string message)
    {
        if (!condition) throw new ProbeAssertException(message);
    }

    public static void AssertEqual<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new ProbeAssertException($"{what}: expected {expected}, got {actual}");
    }

    /// <summary>Runs one probe, never throws.</summary>
    public static ProbeResult Run(string name, Func<string> body, bool informational = false)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = body();
            return new(name, informational ? ProbeStatus.Info : ProbeStatus.Pass, detail, sw.Elapsed.TotalMilliseconds);
        }
        catch (ProbeSkipException e)
        {
            return new(name, ProbeStatus.Skip, e.Message, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception e)
        {
            var text = e.ToString();
            if (text.Length > 6000) text = text[..6000] + "\n...(truncated)";
            return new(name, ProbeStatus.Fail, text, sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>Full suite, in a fixed order. Call MonoModAndroidFix first if you want the fix.</summary>
    public static List<ProbeResult> RunAll(Action<ProbeResult>? onResult = null)
    {
        var results = new List<ProbeResult>();
        foreach (var (name, body, info) in Probes.All)
        {
            var r = Run(name, body, info);
            results.Add(r);
            onResult?.Invoke(r);
        }
        return results;
    }

    public static string Format(ProbeResult r)
    {
        var tag = r.Status switch
        {
            ProbeStatus.Pass => "PASS",
            ProbeStatus.Fail => "FAIL",
            ProbeStatus.Skip => "SKIP",
            _ => "INFO",
        };
        return $"[{tag}] {r.Name} ({r.Ms:F1} ms)\n    {r.Detail.Replace("\n", "\n    ")}";
    }

    public static string Summary(IEnumerable<ProbeResult> results)
    {
        var list = results.ToList();
        var sb = new StringBuilder();
        sb.Append($"passed {list.Count(r => r.Status == ProbeStatus.Pass)}, ");
        sb.Append($"failed {list.Count(r => r.Status == ProbeStatus.Fail)}, ");
        sb.Append($"skipped {list.Count(r => r.Status == ProbeStatus.Skip)}");
        var failed = list.Where(r => r.Status == ProbeStatus.Fail).Select(r => r.Name).ToList();
        if (failed.Count > 0) sb.Append($"\nfailed: {string.Join(", ", failed)}");
        return sb.ToString();
    }
}
