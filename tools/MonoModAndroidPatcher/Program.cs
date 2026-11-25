using Mono.Cecil;
using Mono.Cecil.Cil;

// Rewrites glibc-only P/Invoke entry points in MonoMod assemblies to their bionic names.
// The same patch will later be applied at runtime to the MonoMod.Core.dll shipped with tModLoader.
//
// usage: MonoModAndroidPatcher <input MonoMod.Core.dll> <output path>

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: MonoModAndroidPatcher <input.dll> <output.dll>");
    return 2;
}

var (input, output) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));

// glibc name -> bionic name
var renames = new Dictionary<string, string>
{
    ["__errno_location"] = "__errno",
};

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(input)!);

int patched = 0;
using (var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
       {
           AssemblyResolver = resolver,
           ReadingMode = ReadingMode.Immediate,
           InMemory = true,
       }))
{
    foreach (var type in asm.MainModule.GetTypes())
    foreach (var method in type.Methods)
    {
        if (!method.IsPInvokeImpl || method.PInvokeInfo is not { } info) continue;
        var entry = string.IsNullOrEmpty(info.EntryPoint) ? method.Name : info.EntryPoint;
        if (!renames.TryGetValue(entry, out var bionic)) continue;
        info.EntryPoint = bionic;
        patched++;
        Console.WriteLine($"  {type.FullName}::{method.Name}  [{info.Module.Name}] {entry} -> {bionic}");
    }

    // sysconf() names differ: MonoMod passes glibc numbers (enum Unix.SysconfName).
    // glibc _SC_PAGESIZE = 30, but in bionic 30 is _SC_XOPEN_ENH_I18N; bionic _SC_PAGESIZE = 0x27.
    var sysconfPatched = 0;
    var sysconfGlibcToBionic = new Dictionary<int, int> { [30] = 0x27 };
    foreach (var type in asm.MainModule.GetTypes())
    foreach (var method in type.Methods)
    {
        if (!method.HasBody) continue;
        var instrs = method.Body.Instructions;
        for (var i = 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode.Code != Code.Call || instrs[i].Operand is not MethodReference callee) continue;
            if (callee.Name != "Sysconf" || callee.DeclaringType.Name != "Unix") continue;
            var arg = instrs[i - 1];
            if (!TryGetInt(arg, out var glibc) || !sysconfGlibcToBionic.TryGetValue(glibc, out var bionic)) continue;
            arg.OpCode = OpCodes.Ldc_I4;
            arg.Operand = bionic;
            patched++; sysconfPatched++;
            Console.WriteLine($"  {type.FullName}::{method.Name}  sysconf({glibc}) -> sysconf(0x{bionic:x})");
        }
    }

    if (patched == sysconfPatched || sysconfPatched == 0)
    {
        Console.Error.WriteLine($"error: expected both the errno and the sysconf patch in {input} " +
                                $"(total {patched}, sysconf {sysconfPatched}). MonoMod changed? Update the patcher.");
        return 1;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    asm.Write(output);
}

Console.WriteLine($"MonoModAndroidPatcher: {patched} entry point(s) patched -> {output}");
return 0;

static bool TryGetInt(Instruction ins, out int value)
{
    value = ins.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1, Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2,
        Code.Ldc_I4_3 => 3, Code.Ldc_I4_4 => 4, Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6,
        Code.Ldc_I4_7 => 7, Code.Ldc_I4_8 => 8,
        Code.Ldc_I4_S => (sbyte)ins.Operand,
        Code.Ldc_I4 => (int)ins.Operand,
        _ => int.MinValue,
    };
    return value != int.MinValue;
}
