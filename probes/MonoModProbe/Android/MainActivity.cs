using System.Text;
using Android.App;
using Android.OS;
using Android.Widget;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using MonoMod.Core.Platforms;
using TMLDroid.Probes;

namespace TMLDroid.ProbeApp;

[Activity(
    Label = "MonoMod Probe",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity
{
    // MonoMod's platform state is process-wide, so the suite can run only once per process.
    static bool ranInThisProcess;
    static readonly StringBuilder log = new();
    static readonly object logLock = new();

    TextView logView = null!;
    ScrollView scroll = null!;
    Button runFix = null!, runRaw = null!, share = null!;
    string logPath = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var dir = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath;
        logPath = Path.Combine(dir, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(24, 64, 24, 24);

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        runFix = new Button(this) { Text = "Run all (fix)" };
        runRaw = new Button(this) { Text = "Run as is" };
        share = new Button(this) { Text = "Share log" };
        buttons.AddView(runFix);
        buttons.AddView(runRaw);
        buttons.AddView(share);

        logView = new TextView(this) { TextSize = 11f, Typeface = Android.Graphics.Typeface.Monospace };
        logView.SetTextIsSelectable(true);
        scroll = new ScrollView(this);
        scroll.AddView(logView);

        root.AddView(buttons);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        SetContentView(root);

        runFix.Click += (_, _) => Start(withFix: true);
        runRaw.Click += (_, _) => Start(withFix: false);
        share.Click += (_, _) => Share();

        if (ranInThisProcess)
        {
            SetRunButtons(false);
            Refresh();
            Append("Suite already ran in this process. Swipe the app away from Recents and reopen to run again.");
        }
        else
        {
            Append(DeviceInfo());
            Append("Tap \"Run all (fix)\" first. \"Run as is\" shows stock MonoMod behaviour.");
        }
    }

    void Start(bool withFix)
    {
        if (ranInThisProcess) return;
        ranInThisProcess = true;
        SetRunButtons(false);
        Task.Run(() => RunSuite(withFix));
    }

    void RunSuite(bool withFix)
    {
        try
        {
            Append($"=== mode: {(withFix ? "WITH Android fix" : "AS IS (stock MonoMod)")} ===");
            if (withFix)
            {
                // Stock behaviour first. MonoMod does not cache a failed PlatformTriple init,
                // so we can still apply the fix afterwards in the same process.
                Append(ProbeRunner.Format(ProbeRunner.Run("stock_platform_triple (expected to FAIL on Android)",
                    () => PlatformTriple.Current.HostTriple.ToString())));

                Step("preload_gcc_s_shim", () =>
                {
                    Java.Lang.JavaSystem.LoadLibrary("mm_gcc_s_shim");
                    return "libmm_gcc_s_shim.so loaded (SONAME libgcc_s.so.1)";
                });
                Step("helper_drop_path", () => MonoModAndroidFix.SetHelperDropPath(Path.Combine(CacheDir!.AbsolutePath, "monomod")));
                Step("monomod_log_file", () => MonoModAndroidFix.EnableMonoModLogFile(Path.ChangeExtension(logPath, ".monomod.log")));
                Step("spoof_os_linux", MonoModAndroidFix.SpoofOsAsLinux);
            }

            var results = ProbeRunner.RunAll(r => Append(ProbeRunner.Format(r)));
            Append("");
            Append(ProbeRunner.Summary(results));
            Append("Done. Tap \"Share log\" and attach it to the GitHub issue.");
        }
        catch (Exception e)
        {
            Append("FATAL: " + e);
        }
    }

    void Step(string name, Func<string> body) =>
        Append(ProbeRunner.Format(ProbeRunner.Run(name, body)));

    string DeviceInfo()
    {
        var am = (ActivityManager)GetSystemService(ActivityService)!;
        var mem = new ActivityManager.MemoryInfo();
        am.GetMemoryInfo(mem);
        var soc = OperatingSystem.IsAndroidVersionAtLeast(31) ? $"{Build.SocManufacturer} {Build.SocModel}" : Build.Hardware;
        return string.Join("\n",
            $"device: {Build.Manufacturer} {Build.Model} (SoC: {soc})",
            $"android: {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}), build {Build.Display}",
            $"abis: {string.Join(", ", Build.SupportedAbis ?? [])}",
            $"ram: {mem.TotalMem / (1024 * 1024)} MB total, {mem.AvailMem / (1024 * 1024)} MB available",
            $"log file: {logPath}");
    }

    void Append(string line)
    {
        lock (logLock)
        {
            log.AppendLine(line);
            try { File.AppendAllText(logPath, line + "\n"); } catch { /* best effort: survives native crashes */ }
        }
        Console.WriteLine(line); // also visible in `adb logcat`
        RunOnUiThread(Refresh);
    }

    void Refresh()
    {
        lock (logLock) logView.Text = log.ToString();
        scroll.Post(() => scroll.FullScroll(FocusSearchDirection.Down));
    }

    void SetRunButtons(bool enabled)
    {
        RunOnUiThread(() =>
        {
            runFix.Enabled = enabled;
            runRaw.Enabled = enabled;
        });
    }

    void Share()
    {
        string text;
        lock (logLock) text = log.ToString();
        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraSubject, "TMLDroid MonoMod probe log");
        send.PutExtra(Intent.ExtraText, text);
        StartActivity(Intent.CreateChooser(send, "Share probe log"));
    }
}
