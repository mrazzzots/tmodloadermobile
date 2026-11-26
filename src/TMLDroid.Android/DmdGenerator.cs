using System.Reflection;
using MonoMod;
using MonoMod.Utils;

namespace TMLDroid;

/// <summary>
/// How MonoMod turns a patched method (hook trampoline, IL hook) into runnable code on Android.
///
/// MonoMod decides "this Mono has no usable DynamicMethod" (it probes classic-Mono field names)
/// and falls back to the Cecil generator, which writes a separate assembly. Classic Mono lets such
/// assemblies skip access checks (corlib_internal flag); the dotnet/runtime Mono in .NET for
/// Android does not, so a patched Terraria method calling a protected base method
/// (Main.DoDraw -> Game.Draw) dies with MethodAccessException.
///
/// DynamicMethods are created with skipVisibility and are exempt from those checks. Prefer them;
/// if MonoMod cannot emit one for some method, fall back to Cecil as before.
/// </summary>
public sealed class DmdGenerator : DMDGenerator<DmdGenerator>
{
    static int fallbacks;

    protected override MethodInfo GenerateCore(DynamicMethodDefinition dmd, object? context)
    {
        try
        {
            return DMDEmitDynamicMethodGenerator.Generate(dmd, context);
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref fallbacks) <= 20)
                HostLog.Info($"DMD: DynamicMethod failed for {dmd.OriginalMethod?.DeclaringType?.Name}.{dmd.OriginalMethod?.Name} ({e.GetType().Name}: {e.Message}), using Cecil");
            return DMDCecilGenerator.Generate(dmd, context);
        }
    }

    public static string Install()
    {
        var name = typeof(DmdGenerator).AssemblyQualifiedName!;
        Switches.SetSwitchValue(Switches.DMDType, name);
        return "MonoMod DMDType = DynamicMethod (Cecil fallback)";
    }
}
