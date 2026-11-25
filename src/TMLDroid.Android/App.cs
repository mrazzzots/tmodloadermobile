using Android.App;
using Android.Runtime;

namespace TMLDroid;

[Application(Label = "TMLDroid", LargeHeap = true)]
public sealed class App : Application
{
    public App(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();
        Paths.Init(GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath);
        GameEntry.Publish();
    }
}
