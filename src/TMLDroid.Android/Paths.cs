namespace TMLDroid;

/// <summary>On-device layout. Everything lives in the app's external files dir (no permissions needed).</summary>
public static class Paths
{
    public static string Root { get; private set; } = "";

    public static string Game => Path.Combine(Root, "game");
    public static string TModLoader => Path.Combine(Game, "tModLoader");
    public static string TModLoaderDll => Path.Combine(TModLoader, "tModLoader.dll");
    public static string Libraries => Path.Combine(TModLoader, "Libraries");
    public static string Terraria => Path.Combine(Game, "Terraria");
    public static string TerrariaExe => Path.Combine(Terraria, "Terraria.exe");
    public static string TerrariaContent => Path.Combine(Terraria, "Content");
    public static string Saves => Path.Combine(Root, "saves");                 // -tmlsavedirectory
    public static string SavesMods => Path.Combine(Saves, "Mods");
    public static string Logs => Path.Combine(Root, "logs");
    public static string HostLog => Path.Combine(Logs, "host.log");
    public static string TmlLogs => Path.Combine(TModLoader, "tModLoader-Logs");
    public static string ImportMarker => Path.Combine(Game, "import.json");
    // Native code (our compat .so, MonoMod's helper) must live in internal storage: the Android
    // linker refuses to dlopen anything from /storage/emulated (external app dir).
    public static string InternalRoot { get; private set; } = "";
    public static string InternalCache { get; private set; } = "";
    public static string NativeDir => Path.Combine(InternalRoot, "native");
    public static string MonoModHelperDir => Path.Combine(InternalCache, "monomod");
    public static string Cache => Path.Combine(Root, "cache");

    public static void Init(string root, string internalRoot, string internalCache)
    {
        Root = root;
        InternalRoot = internalRoot;
        InternalCache = internalCache;
        Directory.CreateDirectory(Logs);
    }
}
