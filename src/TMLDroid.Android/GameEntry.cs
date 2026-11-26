using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using TMLDroid.Probes;

namespace TMLDroid;

/// <summary>
/// SDL_main (native/sdl_main_bridge) -> RunGame on SDL's thread -> tModLoader's MonoLaunch.Main.
/// </summary>
public static unsafe class GameEntry
{
    [DllImport("libc", CharSet = CharSet.Ansi)]
    static extern int setenv(string name, string value, int overwrite);

    public static void Publish()
    {
        delegate* unmanaged<int, IntPtr, int> entry = &RunGame;
        setenv("TMLDROID_MANAGED_MAIN", ((nint)entry).ToString("x"), 1);
    }

    static void SetEnv(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value); // managed view
        setenv(name, value, 1);                          // native view (SDL, FNA3D)
    }

    [UnmanagedCallersOnly]
    static int RunGame(int argc, IntPtr argv)
    {
        if (!File.Exists(Paths.LaunchToken))
        {
            HostLog.Info("GameActivity recreated by Android after the process died: not starting tModLoader");
            return 0; // SDL finishes the activity, the launcher shows up again
        }
        File.Delete(Paths.LaunchToken);
        try
        {
            Run();
            HostLog.Info("tModLoader returned normally");
            return 0;
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            HostLog.Error("tModLoader crashed", e.InnerException);
            return 1;
        }
        catch (Exception e)
        {
            HostLog.Error("host failed to start tModLoader", e);
            return 1;
        }
    }

    static void Run()
    {
        HostLog.StartSession("game");
        ConsoleCapture.Install();
        HostLog.Info($"TMLDroid {typeof(GameEntry).Assembly.GetName().Version}, {RuntimeInformation.FrameworkDescription}, " +
                     $"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");

        // 1. MonoMod on Android (see docs/RESEARCH.md): compat lib, helper drop path, OS = Linux.
        using (var so = typeof(GameEntry).Assembly.GetManifestResourceStream("libmm_gcc_s_shim.so")
                        ?? throw new InvalidOperationException("libmm_gcc_s_shim.so not embedded"))
            HostLog.Info(MonoModAndroidFix.InstallNativeCompat(so, Path.Combine(Paths.NativeDir, "libmm_gcc_s_shim.so")));
        HostLog.Info(MonoModAndroidFix.SetHelperDropPath(Paths.MonoModHelperDir));
        HostLog.Info(MonoModAndroidFix.SpoofOsAsLinux());
        HostLog.Info(DmdGenerator.Install());

        // 2. Process environment tModLoader expects (it was written for a desktop CWD and $HOME).
        Directory.CreateDirectory(Paths.Saves);
        Directory.CreateDirectory(Paths.Cache);
        Directory.SetCurrentDirectory(Paths.TModLoader);
        SetEnv("HOME", Paths.Root);
        SetEnv("XDG_DATA_HOME", Path.Combine(Paths.Root, "xdg"));
        // FNA opens two output devices (SoundEffect master voice + XACT AudioEngine). SDL's default
        // Android backend, OpenSL ES, allows only one ("Audio device already open"); AAudio allows many.
        SetEnv("SDL_AUDIODRIVER", "aaudio,openslES");
        SetEnv("TMPDIR", Paths.InternalCache); // natives written to temp may be dlopen'ed
        // SDL picks the activity orientation from the window size; Terraria briefly sets 720x800,
        // which turned the screen to portrait (and broke touch coordinates). Landscape only.
        SetEnv("SDL_IOS_ORIENTATIONS", "LandscapeLeft LandscapeRight");
        try { GameConfig.Apply(Paths.Saves); } catch (Exception e) { HostLog.Info("config.json: " + e.Message); }

        // 3. Managed dependencies from Libraries/.
        LibraryResolver.Install(Paths.Libraries);

        // 4. Launch.
        var tml = AssemblyLoadContext.Default.LoadFromAssemblyPath(Paths.TModLoaderDll);
        HostLog.Info($"loaded {tml.FullName}");
        var main = tml.GetType("Terraria.MonoLaunch", throwOnError: true)!
            .GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException("Terraria.MonoLaunch", "Main");

        // Mono may run beforefieldinit static constructors when a method that touches the type is
        // JIT-compiled (CoreCLR defers them). ModCompile's cctor needs Program.SavePath(Shared), which
        // tML sets only later from -tmlsavedirectory; set the very same values up front.
        var program = tml.GetType("Terraria.Program", throwOnError: true)!;
        foreach (var name in new[] { "SavePath", "SavePathShared" })
            program.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetSetMethod(nonPublic: true)?.Invoke(null, [Paths.Saves]);
        HostLog.Info("Program.SavePath/SavePathShared preset to " + Paths.Saves);
        HostLog.Info("bundled " + typeof(Steamworks.SteamAPI).Assembly.FullName);

        string[] args = ["-tmlsavedirectory", Paths.Saves, "-savedirectory", Paths.Saves];
        HostLog.Info("MonoLaunch.Main " + string.Join(' ', args));

        // SDL's Java thread has a ~1 MB stack. Mono's JIT gives Terraria's giant methods
        // (Main.Initialize, SetDefaults switches with thousands of locals) frames of 100+ KB,
        // which overflowed it right after content setup (SIGSEGV, "stack pointer is not in a rw map").
        // Run tModLoader on our own thread with a big stack; SDL attaches it to the JVM on demand.
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var game = new Thread(() =>
        {
            try { main.Invoke(null, [args]); }
            catch (Exception e) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e); }
        }, GameThreadStackSize) { Name = "tML game", IsBackground = false };
        HostLog.Info($"game thread stack: {GameThreadStackSize / (1024 * 1024)} MB");
        game.Start();
        GameState.StartWatching(tml);
        game.Join();
        failure?.Throw();
    }

    const int GameThreadStackSize = 256 * 1024 * 1024;
}
