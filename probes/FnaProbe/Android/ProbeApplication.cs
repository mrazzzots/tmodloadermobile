using Android.App;
using Android.Runtime;

namespace TMLDroid.FnaProbe;

/// <summary>
/// Runs in the app process before any activity: publishes the managed entry point for
/// libmain.so's SDL_main (see native/sdl_main_bridge/main.c).
/// </summary>
[Application]
public sealed class ProbeApplication : Application
{
    public ProbeApplication(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();
        Entry.Publish();
    }
}
