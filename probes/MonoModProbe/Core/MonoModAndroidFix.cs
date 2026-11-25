using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod;
using MonoMod.Utils;

namespace TMLDroid.Probes;

/// <summary>
/// Workarounds that let MonoMod.Core run on Android.
/// MonoMod 25.x throws NotImplementedException for OSKind.Android, but Android is a Linux
/// kernel with a libc that has everything LinuxSystem needs. See docs/RESEARCH.md.
/// Must be applied BEFORE anything touches MonoMod.Core (PlatformTriple.Current).
/// </summary>
public static class MonoModAndroidFix
{
    public static bool LooksLikeAndroid =>
        OperatingSystem.IsAndroid() || (Directory.Exists("/data") && File.Exists("/system/build.prop"));

    /// <summary>LinuxSystem drops a native helper into /tmp by default; Android has no /tmp.</summary>
    public static string SetHelperDropPath(string directory)
    {
        Directory.CreateDirectory(directory);
        Switches.SetSwitchValue(Switches.HelperDropPath, directory);
        return $"HelperDropPath = {directory}";
    }

    /// <summary>Optional MonoMod debug log to a file (useful when something crashes natively).</summary>
    public static string EnableMonoModLogFile(string path)
    {
        Switches.SetSwitchValue(Switches.LogToFile, path);
        return $"MonoMod LogToFile = {path}";
    }

    /// <summary>
    /// Makes MonoMod's PlatformDetection report Linux instead of Android, so PlatformTriple
    /// picks LinuxSystem. Uses reflection on private fields (os, platInitState) because there is
    /// no public override. Returns a human-readable description of what happened.
    /// </summary>
    public static string SpoofOsAsLinux()
    {
        var detected = PlatformDetection.OS; // forces detection of os + arch
        if (!detected.Is(OSKind.Android))
            return $"no-op: MonoMod already detects {detected}";

        var t = typeof(PlatformDetection);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var osField = t.GetField("os", flags)
            ?? throw new MissingFieldException(t.FullName, "os (MonoMod internals changed, update MonoModAndroidFix)");
        var stateField = t.GetField("platInitState", flags)
            ?? throw new MissingFieldException(t.FullName, "platInitState");

        osField.SetValue(null, OSKind.Linux);
        stateField.SetValue(null, 1);

        var now = PlatformDetection.OS;
        if (now != OSKind.Linux)
            throw new InvalidOperationException($"Spoof failed, PlatformDetection.OS is still {now}");
        return $"PlatformDetection.OS: {detected} -> {now}";
    }

    /// <summary>
    /// Loads a native library by name or path (used to preload the libgcc_s replacement).
    /// On Android the host prefers Java's System.loadLibrary; this is the managed fallback.
    /// </summary>
    public static string TryPreloadNative(string nameOrPath)
    {
        return NativeLibrary.TryLoad(nameOrPath, out var handle)
            ? $"loaded {nameOrPath} (0x{handle:x})"
            : $"NOT loaded {nameOrPath}";
    }
}
