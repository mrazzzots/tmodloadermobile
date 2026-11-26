using System.Reflection;
using Android.Runtime;

namespace TMLDroid;

/// <summary>
/// Bridge between the Java overlay (TouchControls) and Terraria:
///  - tells the overlay whether a world is loaded (Main.gameMenu == false), so the joystick and
///    buttons are shown in game only;
///  - executes overlay commands (in-game menu, zoom) on Terraria's main thread.
/// </summary>
public static class GameState
{
    const int CmdMenu = 1, CmdZoomIn = 2, CmdZoomOut = 3;

    public static void StartWatching(Assembly tml)
    {
        // Reading Main.* would run Main's (huge, order-sensitive) static constructor on this thread
        // if the game has not touched Main yet. Program.LoadedEverything (Program's cctor already ran)
        // turns true only after Main.LoadContent, so nothing of Main is read before that.
        var loaded = tml.GetType("Terraria.Program")?.GetField("LoadedEverything", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var main = tml.GetType("Terraria.Main");
        var gameMenu = main?.GetField("gameMenu", BindingFlags.Public | BindingFlags.Static);
        var zoom = main?.GetField("GameZoomTarget", BindingFlags.Public | BindingFlags.Static);
        var queue = main?.GetMethod("QueueMainThreadAction", BindingFlags.Public | BindingFlags.Static, [typeof(Action)]);
        var openOptions = tml.GetType("Terraria.IngameOptions")?.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        HostLog.Info($"GameState: loaded={loaded != null} gameMenu={gameMenu != null} zoom={zoom != null} queue={queue != null} options={openOptions != null}");

        new Thread(() =>
        {
            bool? last = null;
            int tick = 0;
            while (true)
            {
                Thread.Sleep(50);
                try
                {
                    if (loaded != null && !(bool)loaded.GetValue(null)!) continue;

                    for (int cmd = Poll(); cmd != 0; cmd = Poll())
                    {
                        if (cmd is CmdZoomIn or CmdZoomOut)
                        {
                            // a float store: safe from this thread, no need to wait for the game loop
                            if (zoom == null) { HostLog.Info("zoom: Main.GameZoomTarget not found"); continue; }
                            var before = (float)zoom.GetValue(null)!;
                            var z = Math.Clamp(before + (cmd == CmdZoomIn ? 0.1f : -0.1f), 1f, 2f);
                            zoom.SetValue(null, z);
                            HostLog.Info($"zoom: {before:0.00} -> {z:0.00}");
                        }
                        else if (cmd == CmdMenu)
                        {
                            if (gameMenu != null && (bool)gameMenu.GetValue(null)!) continue; // already in the main menu
                            Action open = () => openOptions?.Invoke(null, null);
                            if (queue != null) queue.Invoke(null, [open]); else open();
                            HostLog.Info("menu: IngameOptions.Open" + (queue != null ? " (queued)" : " (direct)"));
                        }
                    }

                    if (++tick % 5 != 0 || gameMenu == null) continue;
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

    static IntPtr cls, notifyMethod, pollMethod;

    static void EnsureClass()
    {
        if (cls != IntPtr.Zero) return;
        // .NET for Android's JNIEnv.FindClass already returns a *global* reference
        // (deleting it as a local one aborts the process: "Attempt to delete global reference").
        cls = JNIEnv.FindClass("io/github/tmldroid/GameActivity");
        notifyMethod = JNIEnv.GetStaticMethodID(cls, "setInGame", "(Z)V");
        pollMethod = JNIEnv.GetStaticMethodID(cls, "pollCommand", "()I");
    }

    static void Notify(bool inGame)
    {
        EnsureClass();
        JNIEnv.CallStaticVoidMethod(cls, notifyMethod, new JValue(inGame));
    }

    static int Poll()
    {
        EnsureClass();
        return JNIEnv.CallStaticIntMethod(cls, pollMethod);
    }
}
