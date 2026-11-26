using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TMLDroid.Compat;

/// <summary>
/// Mods like Calamity are compiled against a "publicized" tModLoader and read private members
/// directly (AWorldListItem._data). On desktop .NET that works because the runtime is told to skip
/// access checks for them; the Mono runtime on Android honours only an explicit
/// [assembly: IgnoresAccessChecksTo("tModLoader")], which such mods may not carry. Before tML
/// loads a mod assembly we add that attribute for every non-BCL assembly it references, so Mono
/// lets it in. Rewritten assemblies are cached by content hash.
/// </summary>
public static class ModAssemblies
{
    const int Version = 1;
    const string AttrNs = "System.Runtime.CompilerServices", AttrName = "IgnoresAccessChecksToAttribute";

    public static Assembly LoadStream(AssemblyLoadContext context, Stream code, Stream? pdb)
    {
        try
        {
            var (dll, pdbFile) = Rewrite(code, pdb);
            using var c = File.OpenRead(dll);
            using var p = pdbFile != null ? File.OpenRead(pdbFile) : null;
            return context.LoadFromStream(c, p);
        }
        catch (Exception e)
        {
            HostLog.Info($"mod asm: rewrite failed ({e.GetType().Name}: {e.Message}), loading original");
            code.Position = 0;
            if (pdb != null) pdb.Position = 0;
            return context.LoadFromStream(code, pdb);
        }
    }

    static (string dll, string? pdb) Rewrite(Stream code, Stream? pdb)
    {
        var bytes = ReadAll(code);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..20].ToLowerInvariant();
        var dir = Path.Combine(Paths.InternalCache, "modasm");
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, $"{hash}-v{Version}.dll");
        var pdbOut = Path.Combine(dir, $"{hash}-v{Version}.pdb");
        if (File.Exists(dll))
            return (dll, File.Exists(pdbOut) ? pdbOut : null);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pdbBytes = pdb != null ? ReadAll(pdb) : null;
        AssemblyDefinition asm;
        bool symbols = pdbBytes != null;
        try { asm = Read(bytes, pdbBytes); }
        catch (Exception e) when (pdbBytes != null)
        {
            HostLog.Info($"mod asm: symbols unreadable ({e.Message}), dropping pdb");
            asm = Read(bytes, null);
            symbols = false;
        }

        using (asm)
        {
            var module = asm.MainModule;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MethodReference? ctor = null;
            foreach (var ca in asm.CustomAttributes)
            {
                if (ca.AttributeType.Name != AttrName || ca.AttributeType.Namespace != AttrNs) continue;
                ctor ??= ca.Constructor;
                if (ca.ConstructorArguments.Count == 1 && ca.ConstructorArguments[0].Value is string s)
                    existing.Add(s);
            }
            ctor ??= FindOrDefineCtor(module);

            var added = new List<string>();
            foreach (var r in module.AssemblyReferences)
            {
                var n = r.Name;
                if (IsBcl(n) || existing.Contains(n)) continue;
                var ca = new CustomAttribute(ctor);
                ca.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, n));
                asm.CustomAttributes.Add(ca);
                added.Add(n);
            }

            var tmpDll = dll + ".tmp";
            var tmpPdb = pdbOut + ".tmp";
            using (var o = File.Create(tmpDll))
            using (var po = symbols ? File.Create(tmpPdb) : null)
            {
                var wp = new WriterParameters();
                if (symbols)
                {
                    wp.WriteSymbols = true;
                    wp.SymbolStream = po;
                    wp.SymbolWriterProvider = new PortablePdbWriterProvider();
                }
                asm.Write(o, wp);
            }
            if (symbols) File.Move(tmpPdb, pdbOut, true);
            File.Move(tmpDll, dll, true);
            HostLog.Info($"mod asm: {asm.Name.Name} had IgnoresAccessChecksTo [{string.Join(", ", existing)}], added [{string.Join(", ", added)}] in {sw.ElapsedMilliseconds} ms");
        }
        return (dll, symbols ? pdbOut : null);
    }

    static AssemblyDefinition Read(byte[] bytes, byte[]? pdb)
    {
        var rp = new ReaderParameters { ReadingMode = ReadingMode.Deferred, InMemory = true, AssemblyResolver = new NullResolver() };
        if (pdb != null)
        {
            rp.ReadSymbols = true;
            rp.SymbolStream = new MemoryStream(pdb, false);
            rp.SymbolReaderProvider = new PortablePdbReaderProvider();
        }
        return AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false), rp);
    }

    static MethodReference FindOrDefineCtor(ModuleDefinition module)
    {
        // Mono matches the attribute by namespace + name only, so a type defined in the mod is enough.
        var def = module.Types.FirstOrDefault(t => t.Namespace == AttrNs && t.Name == AttrName);
        if (def == null)
        {
            var attrBase = new TypeReference("System", "Attribute", module, module.TypeSystem.CoreLibrary);
            def = new TypeDefinition(AttrNs, AttrName,
                Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.BeforeFieldInit, attrBase);
            var baseCtor = new MethodReference(".ctor", module.TypeSystem.Void, attrBase) { HasThis = true };
            var c = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            c.Parameters.Add(new ParameterDefinition("assemblyName", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
            var il = c.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, baseCtor);
            il.Emit(OpCodes.Ret);
            def.Methods.Add(c);
            module.Types.Add(def);
        }
        return def.Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 1);
    }

    static bool IsBcl(string n) =>
        n is "mscorlib" or "netstandard" or "System" || n.StartsWith("System.", StringComparison.Ordinal)
        || n.StartsWith("Microsoft.", StringComparison.Ordinal);

    static byte[] ReadAll(Stream s)
    {
        if (s.CanSeek) s.Position = 0;
        var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    sealed class NullResolver : IAssemblyResolver
    {
        public AssemblyDefinition? Resolve(AssemblyNameReference name) => null;
        public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters) => null;
        public void Dispose() { }
    }
}
