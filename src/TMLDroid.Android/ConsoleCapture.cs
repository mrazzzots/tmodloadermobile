using System.Text;

namespace TMLDroid;

/// <summary>
/// Mirrors Console.Out / Console.Error into host.log. tModLoader prints fatal errors that happen
/// before its own logging is set up (ErrorReporting.FatalExit) to Console.Error only.
/// </summary>
public static class ConsoleCapture
{
    static bool installed;

    /// <summary>The real console (logcat), so HostLog does not write its lines to the file twice.</summary>
    public static TextWriter? OriginalOut { get; private set; }

    public static void Install()
    {
        if (installed) return;
        installed = true;
        OriginalOut = Console.Out;
        Console.SetOut(new Tee(Console.Out, "OUT"));
        Console.SetError(new Tee(Console.Error, "ERR"));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HostLog.Error("UNHANDLED: " + e.ExceptionObject);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            HostLog.Info($"process exiting, exit code {Environment.ExitCode}\n{Environment.StackTrace}");
    }

    sealed class Tee(TextWriter inner, string tag) : TextWriter
    {
        readonly StringBuilder line = new();
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            inner.Write(value);
            lock (line)
            {
                if (value == '\n') Flush();
                else if (value != '\r') line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            foreach (var c in value) Write(c);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        public override void Flush()
        {
            lock (line)
            {
                if (line.Length == 0) return;
                HostLog.Raw($"[{tag}] {line}");
                line.Clear();
            }
        }
    }
}
