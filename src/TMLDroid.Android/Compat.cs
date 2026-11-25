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
