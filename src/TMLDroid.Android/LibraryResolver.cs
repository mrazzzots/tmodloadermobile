using System.Reflection;
using System.Runtime.Loader;

namespace TMLDroid;

/// <summary>
/// Resolves tModLoader's managed dependencies from its Libraries/ folder (what deps.json does on PC).
/// Assemblies that ship inside the APK (MonoMod, Mono.Cecil, BCL) win, because the default
/// context finds them first: that is how tML ends up on the bionic-patched MonoMod.Core.
/// </summary>
public static class LibraryResolver
{
    static Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);
    static bool installed;

    public static void Install(string librariesDir)
    {
        index = BuildIndex(librariesDir);
        HostLog.Info($"LibraryResolver: {index.Count} assemblies indexed in {librariesDir}");
        if (installed) return;
        installed = true;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == null || !index.TryGetValue(name.Name, out var path)) return null;
            HostLog.Info($"resolve {name.Name} {name.Version} -> {Path.GetRelativePath(librariesDir, path)}");
            return context.LoadFromAssemblyPath(path);
        };
    }

    /// <summary>name -> path; for duplicates the highest version folder wins (Libraries/name/version/...).</summary>
    static Dictionary<string, string> BuildIndex(string dir)
    {
        var map = new Dictionary<string, (Version v, string path)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (rel.StartsWith("Native/", StringComparison.OrdinalIgnoreCase)) continue;
            if (rel.Contains("/ref/", StringComparison.OrdinalIgnoreCase)) continue;       // reference assemblies
            if (rel.StartsWith("basic.reference.assemblies", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(file);
            var version = rel.Split('/').Select(p => Version.TryParse(p, out var v) ? v : null).FirstOrDefault(v => v != null) ?? new Version(0, 0);
            if (!map.TryGetValue(name, out var existing) || version > existing.v)
                map[name] = (version, file);
        }
        return map.ToDictionary(kv => kv.Key, kv => kv.Value.path, StringComparer.OrdinalIgnoreCase);
    }
}
