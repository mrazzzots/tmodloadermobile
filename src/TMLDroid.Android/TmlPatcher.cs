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
    public const int PatchVersion = 11;

    public sealed record Report(string File, int OsCalls, bool InstallVerifier);

    // Calls rewritten below: OS checks (Android counts as Linux) and Assembly.GetEntryAssembly().

    public static Report Patch(string path, string librariesDir, bool patchInstallVerifier, Action<string> log, bool rewriteCalls = true)
    {
        var orig = path + ".orig";
        if (!File.Exists(orig))
            File.Copy(path, orig);

        using var resolver = new RefAssemblyResolver(librariesDir, log);
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);

        int osCalls = 0;
        int hooks = 0;
        int metadataCalls = 0;
        int modLoads = 0;
        int widened = 0;
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
            var metadataLoad = module.ImportReference(typeof(Compat.Metadata).GetMethod(nameof(Compat.Metadata.LoadPath))!);
            var modLoad = module.ImportReference(typeof(Compat.ModAssemblies).GetMethod(nameof(Compat.ModAssemblies.LoadStream))!);
            var resetColor = module.ImportReference(typeof(Compat.ConsoleCompat).GetMethod(nameof(Compat.ConsoleCompat.ResetColor))!);

            widened = WidenProtected(module);

            foreach (var type in module.GetTypes())
            foreach (var method in type.Methods)
            {
                if (!rewriteCalls || !method.HasBody) continue;
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
                    if (callee.Name == "LoadFromAssemblyPath" && callee.DeclaringType.FullName == "System.Reflection.MetadataLoadContext"
                        && type.FullName.StartsWith("Terraria.ModLoader.Core.AssemblyManager", StringComparison.Ordinal))
                    {
                        ins.OpCode = OpCodes.Call;
                        ins.Operand = metadataLoad;
                        metadataCalls++;
                        continue;
                    }
                    if (callee.Name == "LoadFromStream" && callee.Parameters.Count == 2 && callee.HasThis
                        && callee.DeclaringType.FullName == "System.Runtime.Loader.AssemblyLoadContext"
                        && type.FullName.StartsWith("Terraria.ModLoader.Core.AssemblyManager", StringComparison.Ordinal))
                    {
                        ins.OpCode = OpCodes.Call;
                        ins.Operand = modLoad;
                        modLoads++;
                        continue;
                    }
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
                var memBar = module.GetType("Terraria.ModLoader.UI.UIMemoryBar");
                var getTotal = memBar?.Methods.FirstOrDefault(m => m.Name == "GetTotalMemory" && m.Parameters.Count == 0);
                if (getTotal != null)
                {
                    getTotal.Body.Instructions.Clear();
                    getTotal.Body.ExceptionHandlers.Clear();
                    getTotal.Body.Variables.Clear();
                    var gil = getTotal.Body.GetILProcessor();
                    gil.Emit(OpCodes.Call, module.ImportReference(typeof(Compat.Memory).GetMethod(nameof(Compat.Memory.TotalInstalled))!));
                    gil.Emit(OpCodes.Ret);
                    log("UIMemoryBar.GetTotalMemory -> /proc/meminfo");
                }

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

        log($"patched {Path.GetFileName(path)}: {osCalls} calls rewritten (OS checks, GetEntryAssembly, console colours), {hooks} tML hooks made optional, {metadataCalls} metadata loads redirected, {modLoads} mod loads routed{(installPatched ? ", InstallVerifier.Startup replaced" : "")}, {widened} protected members widened");
        return new(path, osCalls, installPatched);
    }

    /// <summary>
    /// Mods compiled against a publicized tML/FNA/ReLogic (Calamity) read protected members from
    /// unrelated classes and rely on [IgnoresAccessChecksTo]. Mono honours that attribute for
    /// private/internal members but not for protected ("family") ones, so e.g.
    /// AWorldListItem._data throws FieldAccessException. protected -> protected internal (and
    /// private protected -> protected internal) routes the check through the internal-access path,
    /// which does honour the attribute. Virtual methods are left alone (override visibility).
    /// Reflection still sees these members as non-public.
    /// </summary>
    static int WidenProtected(ModuleDefinition module)
    {
        int n = 0;
        foreach (var type in module.GetTypes())
        {
            if (type.IsNested)
            {
                var vis = type.Attributes & TypeAttributes.VisibilityMask;
                if (vis is TypeAttributes.NestedFamily or TypeAttributes.NestedFamANDAssem)
                {
                    type.Attributes = (type.Attributes & ~TypeAttributes.VisibilityMask) | TypeAttributes.NestedFamORAssem;
                    n++;
                }
            }
            foreach (var f in type.Fields)
            {
                var a = f.Attributes & FieldAttributes.FieldAccessMask;
                if (a is FieldAttributes.Family or FieldAttributes.FamANDAssem)
                {
                    f.Attributes = (f.Attributes & ~FieldAttributes.FieldAccessMask) | FieldAttributes.FamORAssem;
                    n++;
                }
            }
            foreach (var m in type.Methods)
            {
                if (m.IsVirtual) continue;
                var a = m.Attributes & MethodAttributes.MemberAccessMask;
                if (a is MethodAttributes.Family or MethodAttributes.FamANDAssem)
                {
                    m.Attributes = (m.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.FamORAssem;
                    n++;
                }
            }
        }
        return n;
    }
}
