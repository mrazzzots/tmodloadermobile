using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TMLDroid;

/// <summary>
/// Rewrites the user's own tModLoader.dll / ReLogic.dll on the device (nothing is redistributed):
///  - RuntimeInformation.IsOSPlatform / OperatingSystem.IsOSPlatform / OperatingSystem.IsLinux
///    -> TMLDroid.Compat.OS (Android counts as Linux)
///  - InstallVerifier.Startup -> TMLDroid.Compat.Install.Startup (GOG-style check of Terraria.exe)
/// The original file is kept as *.orig and is always the patch input, so patching is idempotent.
/// </summary>
public static class TmlPatcher
{
    public const int PatchVersion = 4;

    public sealed record Report(string File, int OsCalls, bool InstallVerifier);

    // Calls rewritten below: OS checks (Android counts as Linux) and Assembly.GetEntryAssembly().

    public static Report Patch(string path, string librariesDir, bool patchInstallVerifier, Action<string> log)
    {
        var orig = path + ".orig";
        if (!File.Exists(orig))
            File.Copy(path, orig);

        using var resolver = new RefAssemblyResolver(librariesDir, log);
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);

        int osCalls = 0;
        bool installPatched = false;
        using (var asm = AssemblyDefinition.ReadAssembly(orig, new ReaderParameters
               {
                   AssemblyResolver = resolver,
                   // Deferred: never resolve what we do not touch. Immediate mode decodes every
                   // custom attribute and needs BCL reference assemblies (System.Runtime) as files,
                   // which do not exist on Android. Untouched attribute blobs are written back as-is.
                   ReadingMode = ReadingMode.Deferred,
                   InMemory = true,
                   ReadSymbols = false,
               }))
        {
            var module = asm.MainModule;
            var isOSPlatform = module.ImportReference(typeof(Compat.OS).GetMethod(nameof(Compat.OS.IsOSPlatform))!);
            var isOSPlatformName = module.ImportReference(typeof(Compat.OS).GetMethod(nameof(Compat.OS.IsOSPlatformName))!);
            var isLinux = module.ImportReference(typeof(Compat.OS).GetMethod(nameof(Compat.OS.IsLinux))!);
            var getEntryAssembly = module.ImportReference(typeof(Compat.Runtime).GetMethod(nameof(Compat.Runtime.GetEntryAssembly))!);

            foreach (var type in module.GetTypes())
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.OpCode.Code is not (Code.Call or Code.Callvirt) || ins.Operand is not MethodReference callee) continue;
                    var declaring = callee.DeclaringType.FullName;
                    MethodReference? replacement = (declaring, callee.Name, callee.Parameters.Count) switch
                    {
                        ("System.Runtime.InteropServices.RuntimeInformation", "IsOSPlatform", 1) => isOSPlatform,
                        ("System.OperatingSystem", "IsOSPlatform", 1) => isOSPlatformName,
                        ("System.OperatingSystem", "IsLinux", 0) => isLinux,
                        ("System.Reflection.Assembly", "GetEntryAssembly", 0) => getEntryAssembly,
                        _ => null,
                    };
                    if (replacement == null) continue;
                    ins.OpCode = OpCodes.Call;
                    ins.Operand = replacement;
                    osCalls++;
                }
            }

            if (patchInstallVerifier)
            {
                var verifier = module.GetType("Terraria.ModLoader.Engine.InstallVerifier")
                    ?? throw new InvalidOperationException("InstallVerifier not found in " + path);
                var startup = verifier.Methods.FirstOrDefault(m => m.Name == "Startup" && m.Parameters.Count == 0)
                    ?? throw new InvalidOperationException("InstallVerifier.Startup() not found");
                var body = startup.Body;
                body.Instructions.Clear();
                body.ExceptionHandlers.Clear();
                body.Variables.Clear();
                var il = body.GetILProcessor();
                il.Emit(OpCodes.Call, module.ImportReference(typeof(Compat.Install).GetMethod(nameof(Compat.Install.Startup))!));
                il.Emit(OpCodes.Ret);
                installPatched = true;
            }

            asm.Write(path);
        }

        log($"patched {Path.GetFileName(path)}: {osCalls} calls rewritten (OS checks, GetEntryAssembly){(installPatched ? ", InstallVerifier.Startup replaced" : "")}");
        return new(path, osCalls, installPatched);
    }
}
