using System.Runtime.InteropServices;

namespace TMLDroid.FnaProbe;

public static unsafe class Entry
{
    [DllImport("libc", CharSet = CharSet.Ansi)]
    static extern int setenv(string name, string value, int overwrite);

    public static void Publish()
    {
        delegate* unmanaged<int, IntPtr, int> entry = &RunGame;
        var hex = ((nint)entry).ToString("x");
        var rc = setenv("TMLDROID_MANAGED_MAIN", hex, 1);
        Console.WriteLine($"FNAPROBE published managed entry 0x{hex} (setenv rc={rc})");
    }

    /// <summary>Called by SDL_main on SDL's thread.</summary>
    [UnmanagedCallersOnly]
    static int RunGame(int argc, IntPtr argv)
    {
        try
        {
            var args = new string[argc];
            for (var i = 0; i < argc; i++)
                args[i] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            Console.WriteLine($"FNAPROBE managed main, args: [{string.Join(", ", args)}]");

            SdlDiagnostics.Log();
            using var game = new ProbeGame(autorun: args.Contains("--autorun"));
            game.Run();
            Console.WriteLine("FNAPROBE game exited normally");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FNAPROBE FATAL " + e);
            return 1;
        }
    }
}

/// <summary>Checks that .NET and SDL's Java side share one libSDL2.so instance.</summary>
static class SdlDiagnostics
{
    [DllImport("SDL2")] static extern IntPtr SDL_AndroidGetJNIEnv();
    [DllImport("SDL2")] static extern IntPtr SDL_GetPlatform();
    [DllImport("SDL2")] static extern void SDL_GetVersion(out SdlVersion v);

    [StructLayout(LayoutKind.Sequential)]
    struct SdlVersion { public byte Major, Minor, Patch; }

    public static void Log()
    {
        try
        {
            SDL_GetVersion(out var v);
            var env = SDL_AndroidGetJNIEnv();
            Console.WriteLine($"FNAPROBE SDL {v.Major}.{v.Minor}.{v.Patch} platform={Marshal.PtrToStringUTF8(SDL_GetPlatform())} " +
                              $"JNIEnv=0x{env:x} -> {(env != IntPtr.Zero ? "shared SDL instance OK" : "SECOND SDL COPY (JNI not set up)")}");
        }
        catch (Exception e)
        {
            Console.WriteLine("FNAPROBE SDL diagnostics failed: " + e);
        }
    }
}
