using System.Reflection;
using System.Text;
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

    static IntPtr compatHandle;

    /// <summary>
    /// Writes the native compat library (libmm_gcc_s_shim.so, SONAME libgcc_s.so.1) to
    /// <paramref name="path"/>, loads it by absolute path and routes the "libc" P/Invokes of
    /// MonoMod assemblies to it. It exports glibc's __errno_location (missing in bionic); every
    /// other libc symbol resolves through its DT_NEEDED libc.so. Must run before MonoMod.Core's
    /// Interop.Unix is touched: a failed type initializer is cached for the process lifetime.
    /// </summary>
    public static string InstallNativeCompat(Stream library, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = File.Create(path))
            library.CopyTo(file);

        compatHandle = NativeLibrary.Load(path);
        var sb = new StringBuilder();
        sb.Append($"loaded {path} (0x{compatHandle:x})");

        unsafe
        {
            if (NativeLibrary.TryGetExport(compatHandle, "mm_gcc_s_shim_version", out var ver))
                sb.Append('\n').Append(Marshal.PtrToStringAnsi(((delegate* unmanaged<IntPtr>)ver)()));
        }
        foreach (var sym in new[] { "__errno_location", "_Unwind_RaiseException", "mmap", "mprotect", "mkstemp", "sysconf" })
            sb.Append($"\n  {sym}: {(NativeLibrary.TryGetExport(compatHandle, sym, out _) ? "ok" : "MISSING")}");

        foreach (var asm in new[] { typeof(MonoMod.Core.Platforms.PlatformTriple).Assembly, typeof(PlatformDetection).Assembly })
        {
            NativeLibrary.SetDllImportResolver(asm, ResolveLibc);
            sb.Append($"\n  libc resolver -> {asm.GetName().Name}");
        }
        return sb.ToString();
    }

    static IntPtr ResolveLibc(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name is "libc" or "c" or "libc.so" or "libc.so.6" ? compatHandle : IntPtr.Zero;

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
