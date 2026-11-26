using System.IO.Compression;
using System.Text.Json;

namespace TMLDroid;

/// <summary>
/// Imports TMLDroid.zip (see docs/FILE_IMPORT.md): extracts tModLoader/, Terraria/, Mods/,
/// installs mods into the save folder, checks Terraria.exe and patches tModLoader for Android.
/// </summary>
public static class Importer
{
    public sealed record Summary(DateTime ImportedAt, string TerrariaVersion, string TerrariaMd5,
        long Files, long Bytes, string[] Mods, int PatchVersion);

    static readonly string[] Tops = { "tModLoader/", "Terraria/", "Mods/" };
    // Libraries/Native holds desktop (glibc/Windows) natives; Android ones ship in the APK.
    static readonly string[] SkipPrefixes = { "tModLoader/dotnet/", "tModLoader/tModLoader-Logs/", "tModLoader/Libraries/Native/" };

    public static Summary? Load()
    {
        try { return File.Exists(Paths.ImportMarker) ? JsonSerializer.Deserialize<Summary>(File.ReadAllText(Paths.ImportMarker)) : null; }
        catch { return null; }
    }

    public static Summary Import(Stream zipStream, Action<string> progress)
    {
        HostLog.StartSession("import");
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Archives made with "Send to > Compressed folder" have a top folder (TMLDroid/); accept both.
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        var dllEntry = names.FirstOrDefault(n => n.EndsWith("tModLoader/tModLoader.dll", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Архив не содержит tModLoader/tModLoader.dll");
        var prefix = dllEntry[..^"tModLoader/tModLoader.dll".Length];
        HostLog.Info($"zip: {zip.Entries.Count} entries, root prefix '{prefix}'");

        if (Directory.Exists(Paths.Game))
        {
            progress("Удаляю предыдущий импорт…");
            Directory.Delete(Paths.Game, recursive: true);
        }
        Directory.CreateDirectory(Paths.Game);

        long files = 0, bytes = 0, total = zip.Entries.Sum(e => e.Length);
        var lastReport = DateTime.MinValue;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rel = name[prefix.Length..];
            if (!Tops.Any(t => rel.StartsWith(t, StringComparison.OrdinalIgnoreCase))) continue;
            if (SkipPrefixes.Any(p => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (rel.Contains("steam_api", StringComparison.OrdinalIgnoreCase)) continue;

            var dest = Path.GetFullPath(Path.Combine(Paths.Game, rel));
            if (!dest.StartsWith(Paths.Game + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Bad path in zip: " + name);
            if (rel.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using (var src = entry.Open())
            using (var dst = File.Create(dest))
                src.CopyTo(dst, 1 << 20);
            files++;
            bytes += entry.Length;
            if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(300))
            {
                lastReport = DateTime.UtcNow;
                progress($"Распаковка: {bytes * 100 / Math.Max(total, 1)}% ({files} файлов, {bytes >> 20} МБ)");
            }
        }
        HostLog.Info($"extracted {files} files, {bytes >> 20} MB");
        return Finish(files, bytes, progress);
    }

    /// <summary>True when files were extracted but the import did not finish (e.g. a patch step failed).</summary>
    public static bool CanFinish => Load() == null && File.Exists(Paths.TModLoaderDll);

    /// <summary>Post-extraction steps: checks, mods, patching, marker. Can be re-run without re-extracting.</summary>
    public static Summary Finish(long files, long bytes, Action<string> progress)
    {
        if (files == 0)
        {
            HostLog.StartSession("finish import");
            var all = Directory.EnumerateFiles(Paths.Game, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
            files = all.Count;
            bytes = all.Sum(f => f.Length);
        }

        progress("Проверяю файлы…");
        foreach (var required in new[] { Paths.TModLoaderDll, Paths.TerrariaExe, Path.Combine(Paths.TerrariaContent, "Images", "Projectile_651.xnb") })
            if (!File.Exists(required))
                throw new InvalidDataException("Не найден обязательный файл: " + Path.GetRelativePath(Paths.Game, required));

        var check = TerrariaCheck.Check(Paths.TerrariaExe);
        HostLog.Info("Terraria.exe: " + check.Message);
        if (!check.Ok) throw new InvalidDataException(check.Message);

        progress("Устанавливаю моды…");
        var mods = InstallMods();

        PatchTml(progress);

        var summary = new Summary(DateTime.Now, check.Version ?? "?", check.Md5, files, bytes, mods, TmlPatcher.PatchVersion);
        File.WriteAllText(Paths.ImportMarker, JsonSerializer.Serialize(summary));
        HostLog.Info("import done: " + JsonSerializer.Serialize(summary));
        return summary;
    }

    static void PatchTml(Action<string> progress)
    {
        progress("Адаптирую tModLoader для Android (может занять до минуты)…");
        TmlPatcher.Patch(Paths.TModLoaderDll, Paths.Libraries, patchInstallVerifier: true, HostLog.Info);
        var relogic = Directory.EnumerateFiles(Path.Combine(Paths.Libraries, "ReLogic"), "ReLogic.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (relogic != null)
            TmlPatcher.Patch(relogic, Paths.Libraries, patchInstallVerifier: false, HostLog.Info);
        // FNA: only the protected-member widening (publicized mods read FNA's protected members too)
        var fna = Directory.EnumerateFiles(Paths.Libraries, "FNA.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (fna != null)
            TmlPatcher.Patch(fna, Paths.Libraries, patchInstallVerifier: false, HostLog.Info, rewriteCalls: false);
    }

    /// <summary>Re-applies the current patch set (from the *.orig files) after an app update.</summary>
    public static void Repatch(Action<string> progress)
    {
        HostLog.StartSession("repatch");
        var summary = Load() ?? throw new InvalidOperationException("Нет импорта");
        PatchTml(progress);
        File.WriteAllText(Paths.ImportMarker, JsonSerializer.Serialize(summary with { PatchVersion = TmlPatcher.PatchVersion }));
    }

    /// <summary>
    /// Copies Mods/*.tmod into tML's save folder. They are NOT enabled automatically: the first
    /// launch should reach the main menu with no mods; enable them in tModLoader's Mods menu.
    /// </summary>
    static string[] InstallMods()
    {
        Directory.CreateDirectory(Paths.SavesMods);
        var src = Path.Combine(Paths.Game, "Mods");
        var names = new List<string>();
        if (Directory.Exists(src))
        {
            foreach (var tmod in Directory.EnumerateFiles(src, "*.tmod"))
            {
                File.Copy(tmod, Path.Combine(Paths.SavesMods, Path.GetFileName(tmod)), overwrite: true);
                names.Add(Path.GetFileNameWithoutExtension(tmod));
            }
            Directory.Delete(src, recursive: true); // keep one copy only
        }
        var enabledPath = Path.Combine(Paths.SavesMods, "enabled.json");
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        try { if (File.Exists(enabledPath)) enabled.UnionWith(JsonSerializer.Deserialize<string[]>(File.ReadAllText(enabledPath)) ?? []); } catch { }
        File.WriteAllText(enabledPath, JsonSerializer.Serialize(enabled.OrderBy(n => n).ToArray()));
        var installed = Directory.EnumerateFiles(Paths.SavesMods, "*.tmod").Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n).ToArray();
        HostLog.Info($"mods copied now: {string.Join(", ", names)}; installed: {string.Join(", ", installed)}; enabled.json: {string.Join(", ", enabled)}");
        return installed;
    }
}
