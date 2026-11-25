using Mono.Cecil;

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

    if (patched == 0)
    {
        Console.Error.WriteLine($"error: nothing to patch in {input} (MonoMod changed? update the rename table)");
        return 1;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    asm.Write(output);
}

Console.WriteLine($"MonoModAndroidPatcher: {patched} entry point(s) patched -> {output}");
return 0;
