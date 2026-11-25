using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TMLDroid;

/// <summary>
/// Replaces tModLoader's GOG-style install check on Android: the user must provide Terraria.exe
/// from their own copy. Known hashes (from tModLoader's InstallVerifier, Windows builds) are
/// accepted directly; newer Steam builds (e.g. 1.4.6.x) are accepted when the file is a Terraria
/// assembly at least as new as the version tModLoader stable targets.
/// </summary>
public static class TerrariaCheck
{
    // tModLoader v2026.9 InstallVerifier: Windows GOG and Steam builds of Terraria 1.4.5.8
    static readonly string[] KnownMd5 = { "56d1bee030a5cc5a365574eaa0a79030", "890f5654041f09607d74596d46249000" };
    static readonly Version MinimumVersion = new(1, 4, 5, 8);

    public sealed record Result(bool Ok, string Md5, string? Version, string Message);

    public static Result Check(string exePath)
    {
        if (!File.Exists(exePath))
            return new(false, "", null, $"Terraria.exe not found at {exePath}");

        var bytes = File.ReadAllBytes(exePath);
        var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        if (KnownMd5.Contains(md5))
            return new(true, md5, "1.4.5.8", "known Terraria build (tModLoader hash list)");

        // Terraria keeps its display version ("v1.4.6.1") as a UTF-16 user string.
        var text = Encoding.Unicode.GetString(bytes);
        var versions = Regex.Matches(text, @"v(1\.4\.\d+\.\d+)")
            .Select(m => Version.TryParse(m.Groups[1].Value, out var v) ? v : null)
            .Where(v => v != null).Select(v => v!).ToList();
        var looksLikeTerraria = text.Contains("Terraria.Main") || Encoding.ASCII.GetString(bytes).Contains("Terraria");
        if (!looksLikeTerraria || versions.Count == 0)
            return new(false, md5, null, "Terraria.exe is not a recognised Terraria build");

        var version = versions.Max()!;
        return version >= MinimumVersion
            ? new(true, md5, version.ToString(), $"Terraria v{version} (newer than {MinimumVersion}, accepted)")
            : new(false, md5, version.ToString(), $"Terraria v{version} is older than {MinimumVersion}, which tModLoader stable needs");
    }
}
