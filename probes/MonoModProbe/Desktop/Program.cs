using TMLDroid.Probes;

// Desktop control run: proves the probe itself is correct on platforms MonoMod supports
// (x64 and arm64 Linux/Windows/macOS). On Android the same suite runs inside the APK.

Console.WriteLine("TMLDroid MonoMod probe (desktop)");
if (MonoModAndroidFix.LooksLikeAndroid)
{
    Console.WriteLine(MonoModAndroidFix.SetHelperDropPath(Path.Combine(Path.GetTempPath(), "tmldroid-monomod")));
    Console.WriteLine(MonoModAndroidFix.SpoofOsAsLinux());
}

var results = ProbeRunner.RunAll(r => Console.WriteLine(ProbeRunner.Format(r)));
Console.WriteLine();
Console.WriteLine(ProbeRunner.Summary(results));

var failures = results.Count(r => r.Status == ProbeStatus.Fail);
return failures == 0 ? 0 : 1;
