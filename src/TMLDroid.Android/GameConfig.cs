using System.Text.Json;
using System.Text.Json.Nodes;

namespace TMLDroid;

/// <summary>
/// Terraria's config.json (in the save directory): the PC defaults (800x720 window) make no sense on
/// a phone. Once per config version: fullscreen at the panel's landscape resolution. The first run
/// also had broken touch mapping (window briefly portrait), which picked a random language on the
/// language screen, so the language choice is reset once as well.
/// </summary>
public static class GameConfig
{
    const int Version = 1;

    public static void Apply(string savesDir)
    {
        var path = Path.Combine(savesDir, "config.json");
        var marker = Path.Combine(Paths.InternalRoot, $"config-v{Version}.done");
        if (File.Exists(marker)) return;

        JsonObject cfg;
        try { cfg = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
        catch (Exception e) { HostLog.Info("config.json unreadable, recreating: " + e.Message); cfg = new(); }

        var dm = Android.App.Application.Context.Resources!.DisplayMetrics!;
        int w = Math.Max(dm.WidthPixels, dm.HeightPixels), h = Math.Min(dm.WidthPixels, dm.HeightPixels);
        // Some panels report without the cut-out/nav bar; use the real size when available.
        var wm = Android.App.Application.Context.GetSystemService(Android.Content.Context.WindowService) as Android.Views.IWindowManager;
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && wm != null)
        {
            var b = wm.MaximumWindowMetrics.Bounds;
            w = Math.Max(b.Width(), b.Height()); h = Math.Min(b.Width(), b.Height());
        }

        cfg["Fullscreen"] = true;
        cfg["WindowMaximized"] = false;
        cfg["WindowBorderless"] = false;
        cfg["DisplayWidth"] = w;
        cfg["DisplayHeight"] = h;
        var oldLang = cfg["Language"]?.ToString();
        cfg.Remove("Language");

        File.WriteAllText(path, cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(marker, DateTime.Now.ToString("O"));
        HostLog.Info($"config.json: fullscreen {w}x{h}, language reset (was {oldLang ?? "unset"})");
    }
}
