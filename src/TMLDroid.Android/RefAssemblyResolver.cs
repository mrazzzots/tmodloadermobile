using Mono.Cecil;

namespace TMLDroid;

/// <summary>
/// Cecil resolver for patching on Android, where the BCL exists only inside the APK's assembly
/// store (no files). Falls back to the .NET 8 reference assemblies tModLoader itself ships for
/// compiling mods: Libraries/basic.reference.assemblies.net80/.../Basic.Reference.Assemblies.Net80.dll
/// embeds them as resources named "net80.&lt;AssemblyName&gt;".
/// </summary>
public sealed class RefAssemblyResolver : DefaultAssemblyResolver
{
    readonly string librariesDir;
    readonly Action<string> log;
    Dictionary<string, EmbeddedResource>? refs;
    AssemblyDefinition? refsAssembly;
    readonly Dictionary<string, AssemblyDefinition> cache = new(StringComparer.OrdinalIgnoreCase);

    public RefAssemblyResolver(string librariesDir, Action<string> log)
    {
        this.librariesDir = librariesDir;
        this.log = log;
        AddSearchDirectory(librariesDir);
        foreach (var dir in Directory.EnumerateDirectories(librariesDir, "*", SearchOption.AllDirectories))
            if (!dir.Replace('\\', '/').Contains("/Native", StringComparison.OrdinalIgnoreCase))
                AddSearchDirectory(dir);
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        try
        {
            return base.Resolve(name, parameters);
        }
        catch (AssemblyResolutionException)
        {
            if (cache.TryGetValue(name.Name, out var cached)) return cached;
            var resource = FindReference(name.Name) ?? throw new AssemblyResolutionException(name);
            var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(resource.GetResourceData()), new ReaderParameters
            {
                AssemblyResolver = this,
                ReadingMode = ReadingMode.Deferred,
            });
            cache[name.Name] = asm;
            log($"cecil: {name.Name} resolved from reference pack ({resource.Name})");
            return asm;
        }
    }

    EmbeddedResource? FindReference(string assemblyName)
    {
        if (refs == null)
        {
            refs = new(StringComparer.OrdinalIgnoreCase);
            var pack = Directory.EnumerateFiles(librariesDir, "Basic.Reference.Assemblies.Net80.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (pack == null)
            {
                log("cecil: Basic.Reference.Assemblies.Net80.dll not found in Libraries/");
                return null;
            }
            refsAssembly = AssemblyDefinition.ReadAssembly(pack, new ReaderParameters { ReadingMode = ReadingMode.Deferred });
            foreach (var r in refsAssembly.MainModule.Resources.OfType<EmbeddedResource>())
            {
                var key = r.Name.StartsWith("net80.", StringComparison.OrdinalIgnoreCase) ? r.Name["net80.".Length..] : r.Name;
                if (key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) key = key[..^4];
                refs[key] = r;
            }
            log($"cecil: reference pack {Path.GetFileName(pack)} with {refs.Count} assemblies");
        }
        return refs.GetValueOrDefault(assemblyName);
    }

    protected override void Dispose(bool disposing)
    {
        foreach (var a in cache.Values) a.Dispose();
        refsAssembly?.Dispose();
        base.Dispose(disposing);
    }
}
