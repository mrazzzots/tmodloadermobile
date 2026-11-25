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
    public static string NativeDir => Path.Combine(Root, "native");
    public static string Cache => Path.Combine(Root, "cache");

    public static void Init(string root)
    {
        Root = root;
        Directory.CreateDirectory(Logs);
    }
}
