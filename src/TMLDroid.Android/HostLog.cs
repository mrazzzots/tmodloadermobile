namespace TMLDroid;

/// <summary>Host-side log: file (survives native crashes) + logcat (DOTNET tag).</summary>
public static class HostLog
{
    static readonly object gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception e) => Write("ERROR", $"{message}\n{e}");

    static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        Console.WriteLine("TMLDroid " + line);
        lock (gate)
        {
            try { File.AppendAllText(Paths.HostLog, line + "\n"); } catch { /* best effort */ }
        }
    }

    public static void StartSession(string what)
    {
        try
        {
            if (File.Exists(Paths.HostLog) && new FileInfo(Paths.HostLog).Length > 2_000_000)
                File.Move(Paths.HostLog, Paths.HostLog + ".old", overwrite: true);
        }
        catch { }
        Info($"===== {what} =====");
    }
}
