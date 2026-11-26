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
    public const int PatchVersion = 7;

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
        int hooks = 0;
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
            var newHook = module.ImportReference(typeof(Compat.Hooks).GetMethod(nameof(Compat.Hooks.NewHook))!);
            var setFg = module.ImportReference(typeof(Compat.ConsoleCompat).GetMethod(nameof(Compat.ConsoleCompat.SetForegroundColor))!);
            var setBg = module.ImportReference(typeof(Compat.ConsoleCompat).GetMethod(nameof(Compat.ConsoleCompat.SetBackgroundColor))!);
            var shellStart1 = module.ImportReference(typeof(Compat.Shell).GetMethod(nameof(Compat.Shell.Start), [typeof(string)])!);
            var shellStart2 = module.ImportReference(typeof(Compat.Shell).GetMethod(nameof(Compat.Shell.Start), [typeof(string), typeof(string)])!);
            var resetColor = module.ImportReference(typeof(Compat.ConsoleCompat).GetMethod(nameof(Compat.ConsoleCompat.ResetColor))!);

            foreach (var type in module.GetTypes())
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    // new Hook(MethodBase, Delegate) in tML's own code -> Compat.Hooks.NewHook (mods' hooks stay strict)
                    if (ins.OpCode.Code == Code.Newobj && ins.Operand is MethodReference ctor
                        && ctor.DeclaringType.FullName == "MonoMod.RuntimeDetour.Hook" && ctor.Parameters.Count == 2
                        && ctor.Parameters[0].ParameterType.FullName == "System.Reflection.MethodBase"
                        && ctor.Parameters[1].ParameterType.FullName == "System.Delegate"
                        && type.FullName != "Terraria.ModLoader.MonoModHooks")
                    {
                        ins.OpCode = OpCodes.Call;
                        ins.Operand = newHook;
                        hooks++;
                        continue;
                    }
                    if (ins.OpCode.Code is not (Code.Call or Code.Callvirt) || ins.Operand is not MethodReference callee) continue;
                    var declaring = callee.DeclaringType.FullName;
                    MethodReference? replacement = (declaring, callee.Name, callee.Parameters.Count) switch
                    {
                        ("System.Runtime.InteropServices.RuntimeInformation", "IsOSPlatform", 1) => isOSPlatform,
                        ("System.OperatingSystem", "IsOSPlatform", 1) => isOSPlatformName,
                        ("System.OperatingSystem", "IsLinux", 0) => isLinux,
                        ("System.Reflection.Assembly", "GetEntryAssembly", 0) => getEntryAssembly,
                        ("System.Console", "set_ForegroundColor", 1) => setFg,
                        ("System.Console", "set_BackgroundColor", 1) => setBg,
                        ("System.Console", "ResetColor", 0) => resetColor,
                        ("System.Diagnostics.Process", "Start", 1) when callee.Parameters[0].ParameterType.FullName == "System.String" => shellStart1,
                        ("System.Diagnostics.Process", "Start", 2) when callee.Parameters[1].ParameterType.FullName == "System.String" => shellStart2,
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

                // Android shared storage (/storage/emulated, FUSE) is case-insensitive: File.Exists finds
                // "NPC_517.xnb" while a case-sensitive EnumerateFileSystemInfos(name) returns nothing, so
                // the casing fix-up crashes on First(). Lookups already work regardless of case: skip it.
                var content = module.GetType("Terraria.ModLoader.Engine.TMLContentManager");
                var fix = content?.Methods.FirstOrDefault(m => m.Name == "TryFixFileCasings");
                if (fix != null)
                {
                    fix.Body.Instructions.Clear();
                    fix.Body.ExceptionHandlers.Clear();
                    fix.Body.Variables.Clear();
                    fix.Body.GetILProcessor().Emit(OpCodes.Ret);
                    log("TMLContentManager.TryFixFileCasings disabled (case-insensitive storage)");
                }
            }

            asm.Write(path);
        }

        log($"patched {Path.GetFileName(path)}: {osCalls} calls rewritten (OS checks, GetEntryAssembly, console colours), {hooks} tML hooks made optional{(installPatched ? ", InstallVerifier.Startup replaced" : "")}");
        return new(path, osCalls, installPatched);
    }
}
