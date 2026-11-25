using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using static TMLDroid.Probes.ProbeRunner;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;
using CecilMethodImplAttributes = Mono.Cecil.MethodImplAttributes;

namespace TMLDroid.Probes;

/// <summary>
/// Each probe mirrors something tModLoader or big mods (Calamity) rely on.
/// </summary>
public static class Probes
{
    static (string Name, Func<string> Body, bool Informational) P(string name, Func<string> body, bool informational = false)
        => (name, body, informational);

    public static IReadOnlyList<(string Name, Func<string> Body, bool Informational)> All { get; } = new[]
    {
        P("environment", Environment_, informational: true),
        P("platform_triple", PlatformTriple_),
        P("hook_static", HookStatic),
        P("hook_instance_virtual", HookInstanceVirtual),
        P("ilhook_patch_constant", ILHookConstant),
        P("hook_exception_caught_in_hook", HookExceptionCaught),
        P("hook_exception_propagates", HookExceptionPropagates),
        P("hook_struct_hfa_vector2", HookStructHfa),
        P("hook_struct_retbuf_32b", HookStructRetBuf),
        P("hook_generic_valuetype", HookGeneric),
        P("hook_chain_two_hooks", HookChain),
        P("modlike_assembly_load_and_hook", ModLikeAssembly),
        P("alc_collectible_unload", AlcUnload, informational: true),
        P("stress_300_hooks", Stress),
    };

    // ---------------------------------------------------------------- info

    static string Environment_()
    {
        static string Ver(Assembly a) =>
            a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? a.GetName().Version?.ToString() ?? "?";

        return string.Join("\n",
            $"framework: {RuntimeInformation.FrameworkDescription}",
            $"os: {RuntimeInformation.OSDescription}",
            $"process arch: {RuntimeInformation.ProcessArchitecture}",
            $"IsAndroid={OperatingSystem.IsAndroid()} IsLinux={OperatingSystem.IsLinux()}",
            $"dynamic code: supported={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported} compiled={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled}",
            $"MonoMod PlatformDetection: OS={PlatformDetection.OS} Arch={PlatformDetection.Architecture} Runtime={PlatformDetection.Runtime} {PlatformDetection.RuntimeVersion}",
            $"MonoMod.RuntimeDetour {Ver(typeof(Hook).Assembly)}",
            $"MonoMod.Core {Ver(typeof(PlatformTriple).Assembly)}",
            $"MonoMod.Utils {Ver(typeof(PlatformDetection).Assembly)}",
            $"Mono.Cecil {Ver(typeof(AssemblyDefinition).Assembly)}");
    }

    static string PlatformTriple_()
    {
        var t = PlatformTriple.Current;
        return $"HostTriple = {t.HostTriple}\nSystem = {t.System.GetType().FullName}\nArch = {t.Architecture.GetType().FullName}\nRuntime = {t.Runtime.GetType().FullName}";
    }

    // ---------------------------------------------------------------- basic hooks (On.* in tML)

    static MethodInfo M(string name) =>
        typeof(Targets).GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
        ?? throw new MissingMethodException(nameof(Targets), name);

    static string HookStatic()
    {
        AssertEqual(2, Targets.AddOne(1), "before hook");
        using (new Hook(M(nameof(Targets.AddOne)), new Func<Func<int, int>, int, int>((orig, x) => orig(x) * 10)))
        {
            AssertEqual(20, Targets.AddOne(1), "with hook");
        }
        AssertEqual(2, Targets.AddOne(1), "after dispose");
        return "detour applied and undone";
    }

    static string HookInstanceVirtual()
    {
        var obj = new Targets();
        using (new Hook(M(nameof(Targets.GetValue)), new Func<Func<Targets, int>, Targets, int>((orig, self) => orig(self) + 100)))
        {
            AssertEqual(103, obj.GetValue(), "virtual instance method");
        }
        AssertEqual(3, obj.GetValue(), "after dispose");
        return "ok";
    }

    // ---------------------------------------------------------------- IL hooks (IL.* in tML, heavy in Calamity)

    static string ILHookConstant()
    {
        AssertEqual(2, Targets.AddOneForIL(1), "before");
        using (new ILHook(M(nameof(Targets.AddOneForIL)), il =>
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(i => i.MatchLdcI4(out var v) && v == 1))
                throw new ProbeAssertException("ldc.i4.1 not found in:\n" + string.Join("\n", il.Instrs));
            c.Next!.OpCode = OpCodes.Ldc_I4;
            c.Next.Operand = 5;
        }))
        {
            AssertEqual(6, Targets.AddOneForIL(1), "with IL edit");
        }
        AssertEqual(2, Targets.AddOneForIL(1), "after dispose");
        return "IL rewritten and restored";
    }

    // ---------------------------------------------------------------- exceptions through hooks

    static string HookExceptionCaught()
    {
        using var h = new Hook(M(nameof(Targets.Thrower)), new Func<Func<int, int>, int, int>((orig, x) =>
        {
            try { return orig(x); }
            catch (InvalidOperationException) { return -1; }
        }));
        AssertEqual(-1, Targets.Thrower(5), "exception caught inside hook");
        return "ok";
    }

    static string HookExceptionPropagates()
    {
        using var h = new Hook(M(nameof(Targets.Thrower)), new Func<Func<int, int>, int, int>((orig, x) => orig(x) + 1));
        try
        {
            Targets.Thrower(5);
            throw new ProbeAssertException("no exception reached the caller");
        }
        catch (InvalidOperationException e)
        {
            Assert(e.Message.Contains("boom"), "unexpected message: " + e.Message);
        }
        return "exception crossed the detour to the caller";
    }

    // ---------------------------------------------------------------- arm64 ABI edge cases

    static string HookStructHfa()
    {
        using var h = new Hook(M(nameof(Targets.MakeVec)),
            new Func<Func<float, float, Targets.Vec2>, float, float, Targets.Vec2>((orig, x, y) =>
            {
                var v = orig(x, y);
                v.X += 1;
                return v;
            }));
        var r = Targets.MakeVec(1.5f, -2f);
        Assert(r.X == 2.5f && r.Y == -2f, $"got ({r.X}, {r.Y})");
        return "HFA struct return ok";
    }

    static string HookStructRetBuf()
    {
        using var h = new Hook(M(nameof(Targets.MakeBig)),
            new Func<Func<long, Targets.Big>, long, Targets.Big>((orig, a) =>
            {
                var b = orig(a);
                b.D *= 10;
                return b;
            }));
        var r = Targets.MakeBig(7);
        Assert(r.A == 7 && r.B == 8 && r.C == 9 && r.D == 100, $"got {r.A},{r.B},{r.C},{r.D}");
        return "return-buffer struct ok";
    }

    static string HookGeneric()
    {
        var gm = M(nameof(Targets.GenericName)).MakeGenericMethod(typeof(int));
        using var h = new Hook(gm, new Func<Func<int, string>, int, string>((orig, v) => orig(v) + "!"));
        AssertEqual("Int32!", Targets.GenericName(42), "generic<int>");
        return "ok";
    }

    static string HookChain()
    {
        var m = M(nameof(Targets.AddOne));
        using var a = new Hook(m, new Func<Func<int, int>, int, int>((orig, x) => orig(x) + 100));
        using var b = new Hook(m, new Func<Func<int, int>, int, int>((orig, x) => orig(x) * 2));
        var r = Targets.AddOne(1);
        // order depends on DetourConfig; both must be applied: (1+1+100)*2=204 or (1+1)*2+100=104
        Assert(r is 204 or 104, $"got {r}");
        return $"result {r}";
    }

    // ---------------------------------------------------------------- mod-like assemblies

    /// <summary>Builds a tiny assembly in memory with Cecil, like a .tmod's DLL.</summary>
    static byte[] BuildFakeModAssembly(string name, int methodCount, int answer)
    {
        var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name, ModuleKind.Dll);
        var mod = asm.MainModule;
        var type = new TypeDefinition(name, "FakeMod",
            CecilTypeAttributes.Public | CecilTypeAttributes.Abstract | CecilTypeAttributes.Sealed | CecilTypeAttributes.Class,
            mod.ImportReference(typeof(object)));
        mod.Types.Add(type);
        var int32 = mod.ImportReference(typeof(int));
        for (var i = 0; i < methodCount; i++)
        {
            var m = new MethodDefinition($"M{i}", CecilMethodAttributes.Public | CecilMethodAttributes.Static, int32);
            m.ImplAttributes |= CecilMethodImplAttributes.NoInlining;
            var il = m.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, answer + i);
            il.Emit(OpCodes.Ret);
            type.Methods.Add(m);
        }
        using var ms = new MemoryStream();
        asm.Write(ms);
        return ms.ToArray();
    }

    static string ModLikeAssembly()
    {
        var bytes = BuildFakeModAssembly("ProbeFakeMod", 1, 41);
        var alc = new AssemblyLoadContext("ProbeFakeMod", isCollectible: true);
        var asm = alc.LoadFromStream(new MemoryStream(bytes));
        var mi = asm.GetType("ProbeFakeMod.FakeMod")!.GetMethod("M0")!;
        var call = mi.CreateDelegate<Func<int>>();
        AssertEqual(41, call(), "loaded method");
        using (new Hook(mi, new Func<Func<int>, int>(orig => orig() + 1)))
        {
            AssertEqual(42, call(), "hooked method of loaded assembly");
        }
        return $"loaded {bytes.Length} bytes into collectible ALC and hooked it";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference LoadAndUnload()
    {
        var alc = new AssemblyLoadContext("ProbeUnload", isCollectible: true);
        var asm = alc.LoadFromStream(new MemoryStream(BuildFakeModAssembly("ProbeUnload", 1, 1)));
        _ = asm.GetType("ProbeUnload.FakeMod")!.GetMethod("M0")!.Invoke(null, null);
        alc.Unload();
        return new WeakReference(alc);
    }

    static string AlcUnload()
    {
        var wr = LoadAndUnload();
        for (var i = 0; i < 10 && wr.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return wr.IsAlive
            ? "ALC NOT collected: mod reload will need an app restart on this runtime"
            : "ALC collected: in-process mod unload is possible";
    }

    static string Stress()
    {
        const int n = 300;
        var bytes = BuildFakeModAssembly("ProbeStress", n, 1000);
        var alc = new AssemblyLoadContext("ProbeStress", isCollectible: false);
        var type = alc.LoadFromStream(new MemoryStream(bytes)).GetType("ProbeStress.FakeMod")!;
        var hooks = new List<Hook>(n);
        var sw = Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < n; i++)
                hooks.Add(new Hook(type.GetMethod($"M{i}")!, new Func<Func<int>, int>(orig => -orig())));
            var applyMs = sw.Elapsed.TotalMilliseconds;
            for (var i = 0; i < n; i += 37)
                AssertEqual(-(1000 + i), (int)type.GetMethod($"M{i}")!.Invoke(null, null)!, $"M{i}");
            return $"{n} hooks applied in {applyMs:F0} ms ({applyMs / n:F2} ms/hook)";
        }
        finally
        {
            foreach (var h in hooks) h.Dispose();
        }
    }
}
