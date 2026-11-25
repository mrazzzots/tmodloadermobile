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
        HostLog.Info($"TMLDroid {typeof(GameEntry).Assembly.GetName().Version}, {RuntimeInformation.FrameworkDescription}, " +
                     $"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");

        // 1. MonoMod on Android (see docs/RESEARCH.md): compat lib, helper drop path, OS = Linux.
        using (var so = typeof(GameEntry).Assembly.GetManifestResourceStream("libmm_gcc_s_shim.so")
                        ?? throw new InvalidOperationException("libmm_gcc_s_shim.so not embedded"))
            HostLog.Info(MonoModAndroidFix.InstallNativeCompat(so, Path.Combine(Paths.NativeDir, "libmm_gcc_s_shim.so")));
        HostLog.Info(MonoModAndroidFix.SetHelperDropPath(Paths.MonoModHelperDir));
        HostLog.Info(MonoModAndroidFix.SpoofOsAsLinux());

        // 2. Process environment tModLoader expects (it was written for a desktop CWD and $HOME).
        Directory.CreateDirectory(Paths.Saves);
        Directory.CreateDirectory(Paths.Cache);
        Directory.SetCurrentDirectory(Paths.TModLoader);
        SetEnv("HOME", Paths.Root);
        SetEnv("XDG_DATA_HOME", Path.Combine(Paths.Root, "xdg"));
        SetEnv("TMPDIR", Paths.InternalCache); // natives written to temp may be dlopen'ed

        // 3. Managed dependencies from Libraries/.
        LibraryResolver.Install(Paths.Libraries);

        // 4. Launch.
        var tml = AssemblyLoadContext.Default.LoadFromAssemblyPath(Paths.TModLoaderDll);
        HostLog.Info($"loaded {tml.FullName}");
        var main = tml.GetType("Terraria.MonoLaunch", throwOnError: true)!
            .GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException("Terraria.MonoLaunch", "Main");

        string[] args = ["-tmlsavedirectory", Paths.Saves, "-savedirectory", Paths.Saves];
        HostLog.Info("MonoLaunch.Main " + string.Join(' ', args));
        main.Invoke(null, [args]);
    }
}
