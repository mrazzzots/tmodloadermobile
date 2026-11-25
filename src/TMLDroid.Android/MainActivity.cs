using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace TMLDroid;

/// <summary>Launcher: import TMLDroid.zip, start the game, share logs.</summary>
[Activity(
    Label = "TMLDroid",
    Name = "io.github.tmldroid.MainActivity",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity
{
    const int PickZip = 1;

    TextView status = null!;
    Button importButton = null!, finishButton = null!, playButton = null!, shareButton = null!;
    bool busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(48, 48, 48, 48);

        var title = new TextView(this) { Text = "TMLDroid", TextSize = 26f };
        var subtitle = new TextView(this)
        {
            Text = "tModLoader на Android. Нужны файлы из вашей купленной копии Terraria (docs/FILE_IMPORT.md).",
            TextSize = 13f,
        };
        status = new TextView(this) { TextSize = 14f };
        status.SetPadding(0, 32, 0, 32);

        importButton = new Button(this) { Text = "Импортировать TMLDroid.zip" };
        finishButton = new Button(this) { Text = "Завершить импорт (без повторного копирования)" };
        finishButton.Click += (_, _) => RunBusy("Завершение импорта", () =>
        {
            var summary = Importer.Finish(0, 0, Status);
            return $"Готово: {summary.Files} файлов, Terraria {summary.TerrariaVersion}.";
        });
        playButton = new Button(this) { Text = "Играть" };
        shareButton = new Button(this) { Text = "Поделиться логами" };
        importButton.Click += (_, _) => PickArchive();
        playButton.Click += (_, _) => Play();
        shareButton.Click += (_, _) => ShareLogs();

        root.AddView(title);
        root.AddView(subtitle);
        root.AddView(status);
        root.AddView(importButton);
        root.AddView(finishButton);
        root.AddView(playButton);
        root.AddView(shareButton);
        var scroll = new ScrollView(this);
        scroll.AddView(root);
        scroll.SetOnApplyWindowInsetsListener(new InsetsPadding(0)); // Android 15+ edge-to-edge
        SetContentView(scroll);

        Refresh();
    }

    void Refresh()
    {
        var summary = Importer.Load();
        if (summary == null)
        {
            status.Text = "Файлы игры не импортированы.\n\nСоберите TMLDroid.zip на ПК (tModLoader/, Terraria/Terraria.exe + Content/, Mods/) и нажмите «Импортировать».";
            playButton.Enabled = false;
        }
        else
        {
            status.Text = $"Импортировано {summary.ImportedAt:dd.MM.yyyy HH:mm}\n" +
                          $"Terraria: {summary.TerrariaVersion}\n" +
                          $"Файлов: {summary.Files}, {summary.Bytes >> 20} МБ\n" +
                          $"Моды: {(summary.Mods.Length == 0 ? "нет" : string.Join(", ", summary.Mods))}\n" +
                          (summary.PatchVersion != TmlPatcher.PatchVersion ? "Патчи tModLoader устарели — обновятся при запуске.\n" : "") +
                          "\nПервый запуск без включённых модов; Calamity включается в меню «Моды».";
            playButton.Enabled = !busy;
        }
        importButton.Enabled = !busy;
        finishButton.Visibility = Importer.CanFinish ? ViewStates.Visible : ViewStates.Gone;
        finishButton.Enabled = !busy;
        if (summary == null && Importer.CanFinish)
            status.Text = "Файлы распакованы, но импорт не завершён. Нажмите «Завершить импорт».";
    }

    void PickArchive()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" });
        StartActivityForResult(intent, PickZip);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickZip || resultCode != Result.Ok || data?.Data is not { } uri) return;
        RunBusy("Импорт", () =>
        {
            // ZipArchive needs a seekable stream: copy the content URI to a temp file first.
            var temp = Path.Combine(Paths.Cache, "import.zip");
            Directory.CreateDirectory(Paths.Cache);
            try
            {
                using (var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Не удалось открыть файл"))
                using (var output = File.Create(temp))
                {
                    var buffer = new byte[1 << 20];
                    long copied = 0;
                    var last = DateTime.MinValue;
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, n);
                        copied += n;
                        if (DateTime.UtcNow - last > TimeSpan.FromMilliseconds(300))
                        {
                            last = DateTime.UtcNow;
                            Status($"Копирую архив: {copied >> 20} МБ…");
                        }
                    }
                }
                using var zip = File.OpenRead(temp);
                var summary = Importer.Import(zip, Status);
                return $"Готово: {summary.Files} файлов, Terraria {summary.TerrariaVersion}.";
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        });
    }

    void Play()
    {
        var summary = Importer.Load();
        if (summary == null) return;
        if (summary.PatchVersion != TmlPatcher.PatchVersion)
        {
            RunBusy("Обновление патчей", () =>
            {
                Importer.Repatch(Status);
                return "Патчи обновлены.";
            }, then: StartGame);
            return;
        }
        StartGame();
    }

    void StartGame()
    {
        // One-shot token: Android recreates GameActivity after the process dies; only a launch
        // from this button may start tModLoader.
        File.WriteAllText(Paths.LaunchToken, DateTime.UtcNow.ToString("O"));
        var intent = new Intent(this, Java.Lang.Class.ForName("io.github.tmldroid.GameActivity"));
        StartActivity(intent);
    }

    void ShareLogs()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TMLDroid {PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName} on {Build.Manufacturer} {Build.Model}, Android {Build.VERSION.Release}");
        AppendTail(sb, "host.log", Paths.HostLog, 400);
        AppendTail(sb, "tModLoader client.log", Path.Combine(Paths.TmlLogs, "client.log"), 400);
        AppendTail(sb, "tModLoader Natives.log", Path.Combine(Paths.TmlLogs, "Natives.log"), 50);
        AppendExitReasons(sb);
        AppendLogcat(sb);
        var text = sb.ToString();
        if (text.Length > 400_000) text = text[^400_000..];

        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraSubject, "TMLDroid logs");
        send.PutExtra(Intent.ExtraText, text);
        StartActivity(Intent.CreateChooser(send, "Поделиться логами"));
    }

    /// <summary>Why previous processes of this app died (native crash, ANR, low memory...). API 30+.</summary>
    void AppendExitReasons(StringBuilder sb)
    {
        sb.AppendLine().AppendLine("===== exit reasons =====");
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) { sb.AppendLine("(нужен Android 11+)"); return; }
        try
        {
            var am = (ActivityManager)GetSystemService(ActivityService)!;
            var infos = am.GetHistoricalProcessExitReasons(PackageName, 0, 5);
            foreach (var info in infos)
            {
                sb.AppendLine($"{DateTimeOffset.FromUnixTimeMilliseconds(info.Timestamp).ToLocalTime():dd.MM HH:mm:ss} " +
                              $"pid={info.Pid} reason={info.Reason} status={info.Status} importance={info.Importance} " +
                              $"pss={info.Pss / 1024}MB rss={info.Rss / 1024}MB: {info.Description}");
                // Native crashes carry the tombstone (protobuf); its strings are still readable enough.
                var reason = Convert.ToInt32(info.Reason); // REASON_CRASH = 4, REASON_CRASH_NATIVE = 5
                if (reason is 4 or 5)
                {
                    using var trace = info.TraceInputStream;
                    if (trace != null)
                    {
                        using var ms = new MemoryStream();
                        trace.CopyTo(ms);
                        var readable = new string(Encoding.UTF8.GetString(ms.ToArray())
                            .Select(c => char.IsControl(c) && c != '\n' ? ' ' : c).ToArray());
                        sb.AppendLine("  trace: " + (readable.Length > 20000 ? readable[..20000] : readable));
                    }
                }
            }
            if (infos.Count == 0) sb.AppendLine("(нет записей)");
        }
        catch (Exception e) { sb.AppendLine("(ошибка: " + e.Message + ")"); }
    }

    /// <summary>Recent logcat lines of this app (apps may read their own UID's log).</summary>
    static void AppendLogcat(StringBuilder sb)
    {
        sb.AppendLine().AppendLine("===== logcat (this app) =====");
        try
        {
            var process = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "logcat", "-d", "-t", "1500", "-v", "time" });
            using var reader = new StreamReader(process!.InputStream!);
            var lines = reader.ReadToEnd().Split('\n')
                .Where(l => !l.Contains("Access denied finding property"))
                .TakeLast(800);
            foreach (var l in lines) sb.AppendLine(l);
        }
        catch (Exception e) { sb.AppendLine("(ошибка: " + e.Message + ")"); }
    }

    static void AppendTail(StringBuilder sb, string title, string path, int lines)
    {
        sb.AppendLine().AppendLine($"===== {title} =====");
        try
        {
            if (!File.Exists(path)) { sb.AppendLine("(нет файла)"); return; }
            var all = File.ReadAllLines(path);
            foreach (var line in all.Skip(Math.Max(0, all.Length - lines))) sb.AppendLine(line);
        }
        catch (Exception e) { sb.AppendLine("(ошибка чтения: " + e.Message + ")"); }
    }

    void RunBusy(string what, Func<string> work, Action? then = null)
    {
        busy = true;
        Refresh();
        Status(what + "…");
        Task.Run(() =>
        {
            string message;
            var ok = false;
            try
            {
                message = work();
                ok = true;
            }
            catch (Exception e)
            {
                HostLog.Error(what + " failed", e);
                message = $"Ошибка: {e.Message}\n\nНажмите «Поделиться логами» и пришлите их.";
            }
            RunOnUiThread(() =>
            {
                busy = false;
                Refresh();
                status.Text = message + "\n\n" + status.Text;
                if (ok) then?.Invoke();
            });
        });
    }

    void Status(string text) => RunOnUiThread(() => status.Text = text);
}

sealed class InsetsPadding(int extra) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var b = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            v.SetPadding(b.Left + extra, b.Top + extra, b.Right + extra, b.Bottom + extra);
        }
        return insets;
    }
}
