using System.Reflection;
using System.Runtime.InteropServices;

namespace TMLDroid.Compat;

/// <summary>
/// Targets of the IL rewrites TmlPatcher applies to tModLoader.dll / ReLogic.dll.
/// .NET for Android reports neither Windows, Linux nor macOS, and tModLoader (MonoLaunch,
/// InstallVerifier, ReLogic Platform) refuses to start on an unknown OS. Android is a Linux
/// kernel with a POSIX userland, so tML's Linux code paths are the right ones.
/// </summary>
public static class OS
{
    public static bool IsOSPlatform(OSPlatform platform) =>
        platform == OSPlatform.Linux || RuntimeInformation.IsOSPlatform(platform);

    public static bool IsOSPlatformName(string platform) =>
        string.Equals(platform, "linux", StringComparison.OrdinalIgnoreCase) || OperatingSystem.IsOSPlatform(platform);

    public static bool IsLinux() => true;
}

/// <summary>
/// .NET for Android has no managed entry assembly (Assembly.GetEntryAssembly() == null);
/// on desktop tModLoader is the entry assembly, so report that.
/// </summary>
public static class Runtime
{
    static Assembly? tml;

    public static Assembly? GetEntryAssembly() =>
        Assembly.GetEntryAssembly()
        ?? (tml ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "tModLoader"));
}

/// <summary>
/// tModLoader's own (non-mod) hooks on .NET internals: StackTrace prettifying, assembly-name
/// redirects, web request logging. Several target CoreCLR-only internals that Mono's CoreLib does
/// not have (GetMethod returns null). They are not needed to play, so a missing target is skipped.
/// </summary>
public static class Hooks
{
    public static MonoMod.RuntimeDetour.Hook? NewHook(MethodBase? source, Delegate target)
    {
        if (source == null)
        {
            HostLog.Info($"tML hook skipped: target method not present on this runtime (hook {target.Method.DeclaringType?.Name}.{target.Method.Name})");
            return null;
        }
        try
        {
            return new MonoMod.RuntimeDetour.Hook(source, target);
        }
        catch (Exception e)
        {
            HostLog.Info($"tML hook skipped: {source.DeclaringType?.FullName}.{source.Name}: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }
}

/// <summary>Console colours are not supported on Android (PlatformNotSupportedException).</summary>
public static class ConsoleCompat
{
    public static void SetForegroundColor(ConsoleColor color) { try { Console.ForegroundColor = color; } catch (PlatformNotSupportedException) { } }
    public static void SetBackgroundColor(ConsoleColor color) { try { Console.BackgroundColor = color; } catch (PlatformNotSupportedException) { } }
    public static void ResetColor() { try { Console.ResetColor(); } catch (PlatformNotSupportedException) { } }
}

/// <summary>
/// Process.Start(file, args) / Process.Start(file) in tModLoader: there are no xdg-open/explorer
/// binaries on Android. URLs go to an ACTION_VIEW intent; anything else is logged and skipped.
/// </summary>
public static class Shell
{
    public static System.Diagnostics.Process? Start(string fileName) => Start(fileName, "");

    public static System.Diagnostics.Process? Start(string fileName, string arguments)
    {
        var target = (arguments ?? "").Trim().Trim('"');
        if (target.Length == 0) target = fileName.Trim('"');
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(target));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
                HostLog.Info("opened URL: " + target);
            }
            catch (Exception e) { HostLog.Info($"cannot open URL {target}: {e.Message}"); }
            return null;
        }
        HostLog.Info($"Process.Start skipped on Android: {fileName} {arguments}");
        return null;
    }
}

/// <summary>UIMemoryBar.GetTotalMemory(): Mono's GC.GetGCMemoryInfo() reports heap size, not RAM.</summary>
public static class Memory
{
    public static long TotalInstalled()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
                if (line.StartsWith("MemTotal:"))
                    return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;
        }
        catch { }
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }
}

/// <summary>Replaces Terraria.ModLoader.Engine.InstallVerifier.Startup().</summary>
public static class Install
{
    public static void Startup()
    {
        var result = TerrariaCheck.Check(Paths.TerrariaExe);
        HostLog.Info($"Install check: {result.Message} (md5 {result.Md5})");
        if (!result.Ok)
            throw new InvalidOperationException("TMLDroid install check failed: " + result.Message);

        var verifier = Type.GetType("Terraria.ModLoader.Engine.InstallVerifier, tModLoader", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // GOG mode: no Steam, vanilla Content next to Terraria.exe (Main.TML.cs)
        var platformField = verifier.GetField("DistributionPlatform", flags)!;
        platformField.SetValue(null, Enum.Parse(platformField.FieldType, "GoG"));
        verifier.GetField("vanillaExePath", flags)!.SetValue(null, Paths.TerrariaExe);
        HostLog.Info($"InstallVerifier: DistributionPlatform=GoG, vanillaExePath={Paths.TerrariaExe}");
    }
}
