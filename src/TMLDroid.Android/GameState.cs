using System.Reflection;
using Android.Runtime;

namespace TMLDroid;

/// <summary>
/// Tells the Java overlay whether a world is loaded (Terraria.Main.gameMenu == false), so the
/// joystick and buttons are shown in game only and the menus stay clean.
/// </summary>
public static class GameState
{
    public static void StartWatching(Assembly tml)
    {
        // Reading Main.gameMenu would run Main's (huge, order-sensitive) static constructor on this
        // thread if the game has not touched Main yet. Program.LoadedEverything (Program's cctor
        // already ran) turns true only after Main.LoadContent, so wait for it first.
        var loaded = tml.GetType("Terraria.Program")?.GetField("LoadedEverything", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var gameMenu = tml.GetType("Terraria.Main")?.GetField("gameMenu", BindingFlags.Public | BindingFlags.Static);
        if (gameMenu == null || loaded == null)
        {
            HostLog.Info("GameState: Program.LoadedEverything/Main.gameMenu not found, controls always shown");
            new Thread(() => { Thread.Sleep(3000); Notify(true); }) { IsBackground = true }.Start();
            return;
        }

        new Thread(() =>
        {
            bool? last = null;
            while (true)
            {
                Thread.Sleep(250);
                try
                {
                    if (!(bool)loaded.GetValue(null)!) continue;
                    var inGame = !(bool)gameMenu.GetValue(null)!;
                    if (inGame == last) continue;
                    Notify(inGame);
                    last = inGame;
                    HostLog.Info("in world: " + inGame);
                }
                catch (Exception e)
                {
                    HostLog.Info("GameState: " + e.Message);
                    Thread.Sleep(5000);
                }
            }
        }) { IsBackground = true, Name = "TMLDroid state" }.Start();
    }

    static IntPtr cls, method;

    static void Notify(bool inGame)
    {
        if (cls == IntPtr.Zero)
        {
            // .NET for Android's JNIEnv.FindClass already returns a *global* reference
            // (deleting it as a local one aborts the process: "Attempt to delete global reference").
            cls = JNIEnv.FindClass("io/github/tmldroid/GameActivity");
            method = JNIEnv.GetStaticMethodID(cls, "setInGame", "(Z)V");
        }
        JNIEnv.CallStaticVoidMethod(cls, method, new JValue(inGame));
    }
}
