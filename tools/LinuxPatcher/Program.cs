using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("Subnautica Below Zero Multiplayer Linux Patcher");
        Console.WriteLine("==================================================");

        string repoRoot = args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();

        if (args.Contains("--dump-method"))
        {
            int idx = args.ToList().IndexOf("--dump-method");
            DumpMethod(args[idx + 1], args[idx + 2], args[idx + 3]);
            return;
        }

        if (args.Contains("--check-types"))
        {
            int idx = args.ToList().IndexOf("--check-types");
            using var asm = AssemblyDefinition.ReadAssembly(args[idx + 1]);
            foreach (var tr in asm.MainModule.GetTypeReferences())
            {
                if (tr.Scope != null && tr.Scope.Name.Contains("CoreLib"))
                {
                    Console.WriteLine($"TypeRef: {tr.FullName} -> {tr.Scope}");
                }
            }
            foreach (var mr in asm.MainModule.GetMemberReferences())
            {
                if (mr.DeclaringType != null && mr.DeclaringType.Scope != null && mr.DeclaringType.Scope.Name.Contains("CoreLib"))
                {
                    Console.WriteLine($"MemberRef: {mr.FullName} -> {mr.DeclaringType.Scope}");
                }
            }
            return;
        }

        if (args.Contains("--inspect"))
        {
            InspectLauncher(args.Length > 1 ? args[1] : Path.Combine(repoRoot, "Subnautica.Multiplayer.exe.original"));
            return;
        }
        if (!File.Exists(Path.Combine(repoRoot, "Subnautica.Multiplayer.exe")) && File.Exists(Path.Combine(repoRoot, "..", "Subnautica.Multiplayer.exe")))
        {
            repoRoot = Path.GetFullPath(Path.Combine(repoRoot, ".."));
        }
        if (!File.Exists(Path.Combine(repoRoot, "Subnautica.Multiplayer.exe")) && File.Exists(Path.Combine(repoRoot, "..", "..", "Subnautica.Multiplayer.exe")))
        {
            repoRoot = Path.GetFullPath(Path.Combine(repoRoot, "..", ".."));
        }

        string exePath = Path.Combine(repoRoot, "Subnautica.Multiplayer.exe");
        string backupExe = Path.Combine(repoRoot, "Subnautica.Multiplayer.exe.original");

        if (!File.Exists(exePath) && !File.Exists(backupExe))
        {
            Console.WriteLine($"Error: {exePath} not found!");
            return;
        }

        string sourceExe = File.Exists(backupExe) ? backupExe : exePath;
        if (!File.Exists(backupExe))
        {
            Console.WriteLine($"Backing up original executable to {backupExe}...");
            File.Copy(exePath, backupExe, false);
        }

        string workDir = Path.Combine(Path.GetTempPath(), "subnautica_patcher_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            // 1. Extract costura.subnautica.api.dll.compressed from the launcher
            string rawApiDll = Path.Combine(workDir, "Subnautica.API.raw.dll");
            string patchedApiDll = Path.Combine(workDir, "Subnautica.API.dll");
            Console.WriteLine("Extracting embedded Subnautica.API.dll from launcher...");

            using (var launcherForExtract = AssemblyDefinition.ReadAssembly(sourceExe))
            {
                var res = launcherForExtract.MainModule.Resources.OfType<EmbeddedResource>()
                    .FirstOrDefault(r => r.Name == "costura.subnautica.api.dll.compressed");
                if (res == null)
                {
                    throw new Exception("Could not find costura.subnautica.api.dll.compressed in launcher resources!");
                }

                using var resStream = res.GetResourceStream();
                using var deflate = new DeflateStream(resStream, CompressionMode.Decompress);
                using var outFs = File.Create(rawApiDll);
                deflate.CopyTo(outFs);
                Console.WriteLine($"Extracted raw Subnautica.API.dll ({outFs.Length} bytes).");
            }

            // 2. Patch Subnautica.API.dll
            Console.WriteLine("Patching Subnautica.API.dll for Linux / LAN / Tailscale hosting...");
            PatchSubnauticaApi(rawApiDll, patchedApiDll, workDir);
            Console.WriteLine("Subnautica.API.dll patched successfully.");

            // Compress the patched Subnautica.API.dll for re-embedding
            byte[] patchedApiBytes = File.ReadAllBytes(patchedApiDll);
            byte[] compressedPatchedApi;
            using (var ms = new MemoryStream())
            {
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true))
                {
                    deflate.Write(patchedApiBytes, 0, patchedApiBytes.Length);
                }
                compressedPatchedApi = ms.ToArray();
            }
            Console.WriteLine($"Compressed patched Subnautica.API.dll to {compressedPatchedApi.Length} bytes.");

            string apiJsonPath = File.Exists(Path.Combine(repoRoot, "Api.json")) 
                ? Path.Combine(repoRoot, "Api.json") 
                : Path.Combine(repoRoot, "Api.json.b64");
            string creditsPath = Path.Combine(repoRoot, "credits.json");
            byte[] apiJsonBytes = File.Exists(apiJsonPath) ? File.ReadAllBytes(apiJsonPath) : Array.Empty<byte>();
            byte[] creditsJsonBytes = File.Exists(creditsPath) ? File.ReadAllBytes(creditsPath) : Array.Empty<byte>();

            // 3. Patch Subnautica.Multiplayer.exe
            Console.WriteLine("Patching Subnautica.Multiplayer.exe for Steam / Proton...");
            string patchedExePath = Path.Combine(workDir, "Subnautica.Multiplayer.exe");
            PatchLauncherExe(sourceExe, patchedExePath, compressedPatchedApi, workDir, apiJsonBytes, creditsJsonBytes);

            // 4. Overwrite Subnautica.Multiplayer.exe in repo root
            File.Copy(patchedExePath, exePath, true);
            Console.WriteLine($"Updated {exePath} with patched Linux-ready launcher!");

            // Also save a standalone copy of patched Subnautica.API.dll in repo root
            string repoApiDll = Path.Combine(repoRoot, "Subnautica.API.Patched.dll");
            File.Copy(patchedApiDll, repoApiDll, true);
            Console.WriteLine($"Saved standalone patched API to {repoApiDll}");

            Console.WriteLine("==================================================");
            Console.WriteLine("ALL PATCHES APPLIED SUCCESSFULLY!");
            Console.WriteLine("==================================================");
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            }
            catch { }
        }
    }

    static TypeReference CoreType(ModuleDefinition module, string ns, string name) =>
        new TypeReference(ns, name, module, module.TypeSystem.CoreLibrary);

    static AssemblyNameReference GetSystemAssemblyRef(ModuleDefinition module)
    {
        var sys = module.AssemblyReferences.FirstOrDefault(a => a.Name == "System");
        if (sys == null)
        {
            sys = new AssemblyNameReference("System", new Version(4, 0, 0, 0))
            {
                PublicKeyToken = new byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 }
            };
            module.AssemblyReferences.Add(sys);
        }
        return sys;
    }

    static TypeReference SystemType(ModuleDefinition module, string ns, string name) =>
        new TypeReference(ns, name, module, GetSystemAssemblyRef(module));

    static void PatchSubnauticaApi(string inputDll, string outputDll, string workDir)
    {
        // Generate a stub Assembly-CSharp.dll so Cecil can resolve DamageType enum and ConstructorInput
        string stubAssemblyCSharp = Path.Combine(workDir, "Assembly-CSharp.dll");
        if (!File.Exists(stubAssemblyCSharp))
        {
            var dummyAsm = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("Assembly-CSharp", new Version(0, 0, 0, 0)),
                "Assembly-CSharp",
                ModuleKind.Dll);

            var damageType = new TypeDefinition(
                "",
                "DamageType",
                TypeAttributes.Public | TypeAttributes.Sealed,
                CoreType(dummyAsm.MainModule, "System", "Enum"));

            damageType.Fields.Add(new FieldDefinition(
                "value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                dummyAsm.MainModule.TypeSystem.Int32));

            dummyAsm.MainModule.Types.Add(damageType);

            var constructorInput = new TypeDefinition(
                "",
                "ConstructorInput",
                TypeAttributes.Public,
                dummyAsm.MainModule.TypeSystem.Object);

            dummyAsm.MainModule.Types.Add(constructorInput);

            dummyAsm.Write(stubAssemblyCSharp);
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(workDir);

        var readerParams = new ReaderParameters {
            AssemblyResolver = resolver,
            ReadWrite = false
        };
        using var asm = AssemblyDefinition.ReadAssembly(inputDll, readerParams);
        var module = asm.MainModule;

        // A. NetBirdApi stubs
        var netbirdApi = module.GetType("Subnautica.API.Features.NetBirdApi");
        if (netbirdApi != null)
        {
            StubMethodVoid(netbirdApi, "Refresh");
            StubMethodVoid(netbirdApi, "Disconnect");
            StubMethodBool(netbirdApi, "Connect", true);
            StubMethodBool(netbirdApi, "IsCommandRunning", false);
            StubMethodBool(netbirdApi, "IsConnectingToNetwork", false);
            StubMethodBool(netbirdApi, "IsExists", true);
            StubMethodBool(netbirdApi, "IsWaitingInstallation", false);
            StubMethodBool(netbirdApi, "IsWaitingLogin", false);
            StubMethodBool(netbirdApi, "IsAnyError", false);
            StubMethodBool(netbirdApi, "IsReady", true);
            StubMethodBool(netbirdApi, "IsHostConnected", true);
            StubMethodBool(netbirdApi, "IsHostConnectionActive", true);
            StubMethodString(netbirdApi, "GetPeerId", "LOCAL");
            StubMethodString(netbirdApi, "GetPeerIp", "127.0.0.1");
            StubMethodString(netbirdApi, "ExecuteCommand", "");
            Console.WriteLine("  [+] NetBirdApi stubbed out (no netbird.exe required).");
        }

        // B. FirewallApi stubs
        var firewallApi = module.GetType("Subnautica.API.Features.FirewallApi");
        if (firewallApi != null)
        {
            StubMethodBool(firewallApi, "IsSubnauticaFirewallOk", true);
            StubMethodBool(firewallApi, "SetupSubnauticaFirewall", true);
            StubMethodString(firewallApi, "SetupFirewallWithAdminPerms", "");
            Console.WriteLine("  [+] FirewallApi stubbed out (no Windows Firewall COM calls).");
        }

        // C. Patch version & credits check, and TLS
        var toolsType = module.GetType("Subnautica.API.Features.Tools");
        if (toolsType != null)
        {
            var checkCreditsMethod = toolsType.Methods.FirstOrDefault(m => m.Name == "CheckCreditsPage");
            if (checkCreditsMethod != null)
            {
                foreach (var inst in checkCreditsMethod.Body.Instructions)
                {
                    if (inst.OpCode == OpCodes.Ldc_I4_1 || inst.OpCode == OpCodes.Ldc_I4_3)
                    {
                        inst.OpCode = OpCodes.Ldc_I4_0; // None (success)
                    }
                }
                Console.WriteLine("  [+] Tools.CheckCreditsPage patched (never blocks with error).");
            }

            var checkVersionMethod = toolsType.Methods.FirstOrDefault(m => m.Name == "CheckLauncherVersion");
            if (checkVersionMethod != null)
            {
                foreach (var inst in checkVersionMethod.Body.Instructions)
                {
                    if (inst.OpCode == OpCodes.Ldc_I4_1 || inst.OpCode == OpCodes.Ldc_I4_2 || inst.OpCode == OpCodes.Ldc_I4_3 || inst.OpCode == OpCodes.Ldc_I4_4)
                    {
                        inst.OpCode = OpCodes.Ldc_I4_5; // NewVersionNotFound
                    }
                }
                Console.WriteLine("  [+] Tools.CheckLauncherVersion patched (never blocks with update warning or connection error).");
            }

            PatchToolsTls(toolsType, module);
            Console.WriteLine("  [+] Tools TLS 1.2/1.3 and certificate validation bypass enabled.");

            PatchGZipUncompress(toolsType, module);
            Console.WriteLine("  [+] Tools.GZipUncompress patched for ZLIB & offline fallback.");

            PatchToolsGetLauncherVersion(toolsType, module);
            Console.WriteLine("  [+] Tools.GetLauncherVersion patched to report 3.0.1 for server/client handshake.");

            PatchToolsUserAndId(toolsType, module);
            Console.WriteLine("  [+] Tools.GetLoggedId & GetLoggedInName patched with robust fallbacks.");
        }

        var errorMessagesType = module.GetType("Subnautica.API.Features.ErrorMessages");
        if (errorMessagesType != null)
        {
            PatchErrorMessagesShowConnectionError(errorMessagesType, module);
            Console.WriteLine("  [+] ErrorMessages.ShowConnectionError patched with port 666 diagnosis modal.");
        }

        var settingsType = module.GetType("Subnautica.API.Features.Settings");
        if (settingsType != null)
        {
            PatchSettingsApiSelfHealing(settingsType, module);
            Console.WriteLine("  [+] Settings.Api & CreditsApi self-healing enabled.");

            StubMethodString(settingsType, "get_LauncherVersion", "3.0.1.0");
            Console.WriteLine("  [+] Settings.LauncherVersion stubbed to 3.0.1.0.");
        }

        asm.Name.Version = new Version(3, 0, 1, 0);
        Console.WriteLine("  [+] Subnautica.API assembly version set to 3.0.1.0.");

        // D. Filter out Windows-only binaries and Subnautica.API.dll from ApiDataFormat.GetDownloadFiles
        var apiDataFormat = module.GetType("Subnautica.API.Features.Helper.ApiDataFormat");
        if (apiDataFormat != null)
        {
            PatchGetDownloadFiles(apiDataFormat, module);
            Console.WriteLine("  [+] ApiDataFormat.GetDownloadFiles filtered (skips Windows binaries & preserves patched API).");
        }

        // E. InviteCode async state machines:
        var allTypes = module.GetTypes().ToList();

        var createServerStateMachine = allTypes.FirstOrDefault(t => t.FullName.Contains("<CreateServerAsync>"));
        if (createServerStateMachine != null)
        {
            PatchCreateServerMoveNext(createServerStateMachine, module);
            Console.WriteLine("  [+] InviteCode.CreateServerAsync patched (instant LAN/Tailscale server start).");
        }

        var checkAndConnectStateMachine = allTypes.FirstOrDefault(t => t.FullName.Contains("<CheckAndConnect>"));
        if (checkAndConnectStateMachine != null)
        {
            PatchCheckAndConnectMoveNext(checkAndConnectStateMachine);
            Console.WriteLine("  [+] InviteCode.CheckAndConnect patched (instant connected state).");
        }

        var joinServerStateMachine = allTypes.FirstOrDefault(t => t.FullName.Contains("<JoinServerAsync>"));
        if (joinServerStateMachine != null)
        {
            PatchJoinServerMoveNext(joinServerStateMachine, module);
            Console.WriteLine("  [+] InviteCode.JoinServerAsync patched (direct LAN/Tailscale connection).");
        }

        var waitForHostConnectionStateMachine = allTypes.FirstOrDefault(t => t.FullName.Contains("<WaitForHostConnection>"));
        if (waitForHostConnectionStateMachine != null)
        {
            PatchWaitForHostConnectionMoveNext(waitForHostConnectionStateMachine);
            Console.WriteLine("  [+] InviteCode.WaitForHostConnection patched (instant connected state).");
        }

        var leaveServerStateMachine = allTypes.FirstOrDefault(t => t.FullName.Contains("<LeaveFromServerAsync>"));
        if (leaveServerStateMachine != null)
        {
            PatchCallbackEnumerator(leaveServerStateMachine, "callback");
            Console.WriteLine("  [+] InviteCode.LeaveFromServerAsync patched.");
        }

        var pingServerStateMachine = allTypes.FirstOrDefault(t => t.FullName.Contains("<PingToServerAsync>"));
        if (pingServerStateMachine != null)
        {
            PatchCallbackEnumerator(pingServerStateMachine, "callback");
            Console.WriteLine("  [+] InviteCode.PingToServerAsync patched.");
        }

        foreach (var type in module.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (m.HasBody && m.Body.ExceptionHandlers.Count > 0)
                {
                    ValidateExceptionHandlers(m);
                }
            }
        }

        asm.Write(outputDll);
    }

    static void PatchLauncherExe(string inputExe, string outputExe, byte[] compressedPatchedApi, string workDir, byte[] apiJsonB64Bytes, byte[] creditsJsonBytes)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(workDir);
        var readerParams = new ReaderParameters {
            AssemblyResolver = resolver,
            ReadWrite = false
        };
        using var asm = AssemblyDefinition.ReadAssembly(inputExe, readerParams);
        var module = asm.MainModule;

        // Clean default constants on CustomFontTool.SetLabelFont to avoid Cecil resolving System.Drawing.Common
        var customFontTool = module.GetType("Subnautica.Multiplayer.Core.CustomFontTool");
        if (customFontTool != null)
        {
            var setLabelFont = customFontTool.Methods.FirstOrDefault(m => m.Name == "SetLabelFont");
            if (setLabelFont != null)
            {
                foreach (var p in setLabelFont.Parameters)
                {
                    p.HasDefault = false;
                    p.HasConstant = false;
                    p.Constant = null;
                }
                Console.WriteLine("  [+] Cleaned default constants on CustomFontTool.SetLabelFont.");
            }
        }

        // 1. Replace costura.subnautica.api.dll.compressed resource
        var oldRes = module.Resources.OfType<EmbeddedResource>()
            .FirstOrDefault(r => r.Name == "costura.subnautica.api.dll.compressed");
        if (oldRes != null)
        {
            module.Resources.Remove(oldRes);
            var newRes = new EmbeddedResource("costura.subnautica.api.dll.compressed", ManifestResourceAttributes.Private, compressedPatchedApi);
            module.Resources.Add(newRes);
            Console.WriteLine("  [+] Replaced embedded costura.subnautica.api.dll.compressed with patched assembly.");
        }

        // 2. Patch LauncherScreen
        var launcherScreen = module.GetType("Subnautica.Multiplayer.LauncherScreen");
        if (launcherScreen != null)
        {
            StubMethodBool(launcherScreen, "InstallNetbird", true);
            StubMethodBool(launcherScreen, "SetupFirewall", true);
            Console.WriteLine("  [+] LauncherScreen.InstallNetbird & SetupFirewall bypassed.");

            var downloadDeps = launcherScreen.Methods.FirstOrDefault(m => m.Name == "DownloadDependencies");
            if (downloadDeps != null)
            {
                HookDownloadDependencies(downloadDeps, module);
                Console.WriteLine("  [+] Hooked DownloadDependencies to deploy patched Subnautica.API.dll.");
            }
        }

        // 3. Patch FirstScreen (Splash Screen)
        var firstScreen = module.GetType("Subnautica.Multiplayer.FirstScreen");
        if (firstScreen != null)
        {
            PatchFirstScreen(firstScreen, module);
            Console.WriteLine("  [+] FirstScreen patched (connection fail popups suppressed, offline resilient).");
        }

        // 4. Inject Bootstrap into Program.Main
        InjectLauncherBootstrap(module, apiJsonB64Bytes, creditsJsonBytes);
        Console.WriteLine("  [+] Program.Main bootstrapped (TLS 1.2/1.3 cert bypass, auto-deploy Api.json and credits.json).");

        // 5. Patch Steam platform
        var steamPlatform = module.GetType("Subnautica.Multiplayer.Platforms.Steam");
        if (steamPlatform != null)
        {
            StubMethodBool(steamPlatform, "IsRunningPlatform", true);
            PatchSteamGetPlatformPath(steamPlatform, module);
            PatchSteamGetGamePath(steamPlatform, module);
            PatchSteamStartGame(steamPlatform, module);
            PatchSteamUpdateAssembly(steamPlatform, module);
            Console.WriteLine("  [+] Steam platform detection & launcher patched for Proton/Linux.");
        }

        foreach (var type in module.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (m.HasBody && m.Body.ExceptionHandlers.Count > 0)
                {
                    ValidateExceptionHandlers(m);
                }
            }
        }

        asm.Write(outputExe);
    }

    // --- Helper Methods ---

    static void StubMethodVoid(TypeDefinition type, string methodName)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method != null)
        {
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ret));
        }
    }

    static void StubMethodBool(TypeDefinition type, string methodName, bool value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method != null)
        {
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));
        }
    }

    static void StubMethodString(TypeDefinition type, string methodName, string value)
    {
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method != null)
        {
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldstr, value));
            il.Append(il.Create(OpCodes.Ret));
        }
    }

    static MethodReference GetActionInvokeMethod(TypeReference actionType, ModuleDefinition module, IEnumerable<Instruction>? instructions = null)
    {
        if (instructions != null)
        {
            var existing = instructions
                .Select(i => i.Operand as MethodReference)
                .FirstOrDefault(mr => mr != null && mr.Name == "Invoke" && mr.DeclaringType.FullName == actionType.FullName);
            if (existing != null)
            {
                return existing;
            }
            var existingAny = instructions
                .Select(i => i.Operand as MethodReference)
                .FirstOrDefault(mr => mr != null && mr.Name == "Invoke");
            if (existingAny != null)
            {
                return existingAny;
            }
        }

        var def = actionType.Resolve();
        var invokeDef = def.Methods.First(m => m.Name == "Invoke");
        var mrResult = new MethodReference("Invoke", module.TypeSystem.Void, actionType)
        {
            HasThis = invokeDef.HasThis,
            ExplicitThis = invokeDef.ExplicitThis,
            CallingConvention = invokeDef.CallingConvention
        };
        foreach (var p in invokeDef.Parameters)
        {
            mrResult.Parameters.Add(new ParameterDefinition(p.ParameterType));
        }
        return mrResult;
    }

    static void PatchCallbackEnumerator(TypeDefinition stateMachine, string callbackFieldName)
    {
        var moveNext = stateMachine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        var callbackField = stateMachine.Fields.FirstOrDefault(f => f.Name == callbackFieldName);
        if (moveNext != null)
        {
            MethodReference? invokeMethod = null;
            if (callbackField != null)
            {
                invokeMethod = GetActionInvokeMethod(callbackField.FieldType, moveNext.Module, moveNext.Body.Instructions);
            }

            moveNext.Body.Instructions.Clear();
            moveNext.Body.ExceptionHandlers.Clear();
            moveNext.Body.Variables.Clear();
            var il = moveNext.Body.GetILProcessor();

            if (callbackField != null && invokeMethod != null)
            {
                var retFalse = il.Create(OpCodes.Ldc_I4_0);
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldfld, callbackField));
                il.Append(il.Create(OpCodes.Brfalse, retFalse));

                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldfld, callbackField));
                il.Append(il.Create(OpCodes.Ldc_I4_0)); // false (isError = false)
                il.Append(il.Create(OpCodes.Callvirt, invokeMethod));

                il.Append(retFalse);
                il.Append(il.Create(OpCodes.Ret));
            }
            else
            {
                il.Append(il.Create(OpCodes.Ldc_I4_0));
                il.Append(il.Create(OpCodes.Ret));
            }

            moveNext.Body.OptimizeMacros();
        }
    }

    static void PatchCheckAndConnectMoveNext(TypeDefinition stateMachine)
    {
        var moveNext = stateMachine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        var thisField = stateMachine.Fields.FirstOrDefault(f => f.Name == "<>4__this");
        if (moveNext != null && thisField != null)
        {
            var inviteCodeType = thisField.FieldType.Resolve();
            var setIsClientConnected = moveNext.Module.ImportReference(inviteCodeType.Methods.First(m => m.Name == "set_IsClientConnected"));
            var setCurrentPeerId = moveNext.Module.ImportReference(inviteCodeType.Methods.First(m => m.Name == "set_CurrentPeerId"));

            moveNext.Body.Instructions.Clear();
            moveNext.Body.ExceptionHandlers.Clear();
            moveNext.Body.Variables.Clear();
            var il = moveNext.Body.GetILProcessor();

            // this.<>4__this.set_IsClientConnected(true);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, thisField));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Callvirt, setIsClientConnected));

            // this.<>4__this.set_CurrentPeerId("LOCAL");
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, thisField));
            il.Append(il.Create(OpCodes.Ldstr, "LOCAL"));
            il.Append(il.Create(OpCodes.Callvirt, setCurrentPeerId));

            // return false;
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));

            moveNext.Body.OptimizeMacros();
        }
    }

    static void PatchWaitForHostConnectionMoveNext(TypeDefinition stateMachine)
    {
        var moveNext = stateMachine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        var thisField = stateMachine.Fields.FirstOrDefault(f => f.Name == "<>4__this");
        if (moveNext != null && thisField != null)
        {
            var inviteCodeType = thisField.FieldType.Resolve();
            var setIsHostConnected = moveNext.Module.ImportReference(inviteCodeType.Methods.First(m => m.Name == "set_IsHostConnected"));

            moveNext.Body.Instructions.Clear();
            moveNext.Body.ExceptionHandlers.Clear();
            moveNext.Body.Variables.Clear();
            var il = moveNext.Body.GetILProcessor();

            // this.<>4__this.set_IsHostConnected(true);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, thisField));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Callvirt, setIsHostConnected));

            // return false;
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));

            moveNext.Body.OptimizeMacros();
        }
    }

    static void PatchJoinServerMoveNext(TypeDefinition stateMachine, ModuleDefinition module)
    {
        var moveNext = stateMachine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        var joinCodeField = stateMachine.Fields.FirstOrDefault(f => f.Name == "joinCode");
        var onSuccessField = stateMachine.Fields.FirstOrDefault(f => f.Name == "onSuccess");
        if (moveNext != null && joinCodeField != null && onSuccessField != null)
        {
            var responseType = module.GetType("Subnautica.API.Features.Helper.LobbyJoinServerResponseFormat");
            var responseCtor = module.ImportReference(responseType.Methods.First(m => m.IsConstructor && !m.HasParameters));
            var setIsError = module.ImportReference(responseType.Methods.First(m => m.Name == "set_IsError"));
            var setServerIp = module.ImportReference(responseType.Methods.First(m => m.Name == "set_ServerIp"));
            var setServerPort = module.ImportReference(responseType.Methods.First(m => m.Name == "set_ServerPort"));

            var invokeSuccess = GetActionInvokeMethod(onSuccessField.FieldType, module, moveNext.Body.Instructions);

            moveNext.Body.Instructions.Clear();
            moveNext.Body.ExceptionHandlers.Clear();
            moveNext.Body.Variables.Clear();

            var respVar = new VariableDefinition(module.ImportReference(responseType));
            moveNext.Body.Variables.Add(respVar);

            var il = moveNext.Body.GetILProcessor();

            // var resp = new LobbyJoinServerResponseFormat();
            il.Append(il.Create(OpCodes.Newobj, responseCtor));
            il.Append(il.Create(OpCodes.Stloc, respVar));

            // resp.IsError = false;
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Callvirt, setIsError));

            // resp.ServerIp = this.joinCode;
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, joinCodeField));
            il.Append(il.Create(OpCodes.Callvirt, setServerIp));

            // resp.ServerPort = 666;
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldc_I4, 666));
            il.Append(il.Create(OpCodes.Callvirt, setServerPort));

            // if (this.onSuccess != null) this.onSuccess.Invoke(resp);
            var afterOnSuccess = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, onSuccessField));
            il.Append(il.Create(OpCodes.Brfalse, afterOnSuccess));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, onSuccessField));
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Callvirt, invokeSuccess));

            il.Append(afterOnSuccess);

            // return false;
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));

            moveNext.Body.OptimizeMacros();
        }
    }

    static void PatchCreateServerMoveNext(TypeDefinition stateMachine, ModuleDefinition module)
    {
        Console.WriteLine($"  [DEBUG] CreateServer stateMachine: {stateMachine.FullName}");
        foreach (var f in stateMachine.Fields)
        {
            Console.WriteLine($"    Field: {f.Name} ({f.FieldType.FullName})");
        }

        var moveNext = stateMachine.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        var thisField = stateMachine.Fields.FirstOrDefault(f => f.Name == "<>4__this");
        var stateField = stateMachine.Fields.FirstOrDefault(f => f.Name == "<>1__state");
        var onSuccessField = stateMachine.Fields.FirstOrDefault(f => f.Name == "onSuccess");
        var onFinishedField = stateMachine.Fields.FirstOrDefault(f => f.Name == "onFinished");

        if (moveNext != null && thisField != null && onSuccessField != null)
        {
            var inviteCodeType = thisField.FieldType.Resolve();
            var setInviteCode = module.ImportReference(inviteCodeType.Methods.First(m => m.Name == "SetInviteCode"));
            var setAccessToken = module.ImportReference(inviteCodeType.Methods.First(m => m.Name == "SetAccessToken"));

            var responseType = module.GetType("Subnautica.API.Features.Helper.LobbyCreateServerResponse");
            var responseCtor = module.ImportReference(responseType.Methods.First(m => m.IsConstructor && !m.HasParameters));
            var setIsError = module.ImportReference(responseType.Methods.First(m => m.Name == "set_IsError"));
            var setJoinCode = module.ImportReference(responseType.Methods.First(m => m.Name == "set_JoinCode"));
            var setRespAccessToken = module.ImportReference(responseType.Methods.First(m => m.Name == "set_AccessToken"));
            var setServerIp = module.ImportReference(responseType.Methods.First(m => m.Name == "set_ServerIp"));
            var setServerPort = module.ImportReference(responseType.Methods.First(m => m.Name == "set_ServerPort"));

            MethodReference? origInvokeSuccess = null;
            MethodReference? origInvokeFinished = null;
            foreach (var inst in moveNext.Body.Instructions)
            {
                if (inst.OpCode == OpCodes.Callvirt && inst.Operand is MethodReference mr && mr.Name == "Invoke")
                {
                    if (mr.Parameters.Count == 1 && origInvokeSuccess == null)
                    {
                        Console.WriteLine($"  [ORIG INVOKE SUCCESS] {mr.FullName} | Declaring: {mr.DeclaringType.FullName} ({mr.DeclaringType.GetType().Name}) | Param0: {mr.Parameters[0].ParameterType.FullName} ({mr.Parameters[0].ParameterType.GetType().Name})");
                        origInvokeSuccess = mr;
                    }
                    else if (mr.Parameters.Count == 0 && origInvokeFinished == null)
                    {
                        Console.WriteLine($"  [ORIG INVOKE FINISHED] {mr.FullName} | Declaring: {mr.DeclaringType.FullName}");
                        origInvokeFinished = mr;
                    }
                }
            }

            var invokeSuccess = origInvokeSuccess ?? GetActionInvokeMethod(onSuccessField.FieldType, module);
            MethodReference? invokeFinished = null;
            if (onFinishedField != null)
            {
                invokeFinished = origInvokeFinished ?? GetActionInvokeMethod(onFinishedField.FieldType, module);
            }

            var logType = module.GetType("Subnautica.API.Features.Log");
            var logError = module.ImportReference(logType.Methods.First(m => m.Name == "Error" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Object"));

            moveNext.Body.Instructions.Clear();
            moveNext.Body.ExceptionHandlers.Clear();
            moveNext.Body.Variables.Clear();

            var respVar = new VariableDefinition(module.ImportReference(responseType));
            moveNext.Body.Variables.Add(respVar);

            var il = moveNext.Body.GetILProcessor();

            // <>1__state = -1;
            if (stateField != null)
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldc_I4_M1));
                il.Append(il.Create(OpCodes.Stfld, stateField));
            }

            // var resp = new LobbyCreateServerResponse();
            il.Append(il.Create(OpCodes.Newobj, responseCtor));
            il.Append(il.Create(OpCodes.Stloc, respVar));

            // resp.IsError = false;
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Callvirt, setIsError));

            // resp.JoinCode = "LAN/IP";
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldstr, "LAN/IP"));
            il.Append(il.Create(OpCodes.Callvirt, setJoinCode));

            // resp.AccessToken = "LOCAL";
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldstr, "LOCAL"));
            il.Append(il.Create(OpCodes.Callvirt, setRespAccessToken));

            // resp.ServerIp = "127.0.0.1";
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldstr, "127.0.0.1"));
            il.Append(il.Create(OpCodes.Callvirt, setServerIp));

            // resp.ServerPort = 666;
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Ldc_I4, 666));
            il.Append(il.Create(OpCodes.Callvirt, setServerPort));

            // this.<>4__this.SetInviteCode(resp.JoinCode);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, thisField));
            il.Append(il.Create(OpCodes.Ldstr, "LAN/IP"));
            il.Append(il.Create(OpCodes.Callvirt, setInviteCode));

            // this.<>4__this.SetAccessToken(resp.AccessToken);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, thisField));
            il.Append(il.Create(OpCodes.Ldstr, "LOCAL"));
            il.Append(il.Create(OpCodes.Callvirt, setAccessToken));

            // try { if (this.onSuccess != null) this.onSuccess.Invoke(resp); }
            var tryStart = il.Create(OpCodes.Ldarg_0);
            var afterOnSuccess = il.Create(OpCodes.Nop);
            var leaveTry = il.Create(OpCodes.Leave, afterOnSuccess);
            var skipInvokeSuccess = il.Create(OpCodes.Nop);

            il.Append(tryStart);
            il.Append(il.Create(OpCodes.Ldfld, onSuccessField));
            il.Append(il.Create(OpCodes.Brfalse, skipInvokeSuccess));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, onSuccessField));
            il.Append(il.Create(OpCodes.Ldloc, respVar));
            il.Append(il.Create(OpCodes.Callvirt, invokeSuccess));

            il.Append(skipInvokeSuccess);
            il.Append(leaveTry);

            // catch (Exception ex) { Log.Error(ex); }
            var catchStart = il.Create(OpCodes.Call, logError);
            il.Append(catchStart);
            var leaveCatch = il.Create(OpCodes.Leave, afterOnSuccess);
            il.Append(leaveCatch);

            il.Append(afterOnSuccess);

            var exceptionType = CoreType(module, "System", "Exception");
            var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = catchStart,
                HandlerStart = catchStart,
                HandlerEnd = afterOnSuccess,
                CatchType = module.ImportReference(exceptionType)
            };
            moveNext.Body.ExceptionHandlers.Add(handler);

            // ALWAYS invoke onFinished:
            if (onFinishedField != null && invokeFinished != null)
            {
                var afterOnFinished = il.Create(OpCodes.Nop);
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldfld, onFinishedField));
                il.Append(il.Create(OpCodes.Brfalse, afterOnFinished));

                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldfld, onFinishedField));
                il.Append(il.Create(OpCodes.Callvirt, invokeFinished));

                il.Append(afterOnFinished);
            }

            // return false;
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));

            moveNext.Body.OptimizeMacros();
        }
    }

    static void PatchToolsGetLauncherVersion(TypeDefinition toolsType, ModuleDefinition module)
    {
        var method = toolsType.Methods.FirstOrDefault(m => m.Name == "GetLauncherVersion");
        if (method == null) return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();

        var elseLabel = il.Create(OpCodes.Ldarg_1);
        var vDotLabel = il.Create(OpCodes.Ldstr, "v3.0.1");
        var noVDotLabel = il.Create(OpCodes.Ldstr, "3.0.1");

        il.Append(il.Create(OpCodes.Ldarg_0)); // addVersionString
        il.Append(il.Create(OpCodes.Brfalse, elseLabel));

        // addVersionString is true:
        il.Append(il.Create(OpCodes.Ldarg_1)); // addImplodeDot
        il.Append(il.Create(OpCodes.Brtrue, vDotLabel));
        il.Append(il.Create(OpCodes.Ldstr, "v301"));
        il.Append(il.Create(OpCodes.Ret));

        il.Append(vDotLabel);
        il.Append(il.Create(OpCodes.Ret));

        // addVersionString is false:
        il.Append(elseLabel);
        il.Append(il.Create(OpCodes.Brtrue, noVDotLabel));
        il.Append(il.Create(OpCodes.Ldstr, "301"));
        il.Append(il.Create(OpCodes.Ret));

        il.Append(noVDotLabel);
        il.Append(il.Create(OpCodes.Ret));

        method.Body.OptimizeMacros();
    }

    static void PatchToolsUserAndId(TypeDefinition toolsType, ModuleDefinition module)
    {
        var getLoggedId = toolsType.Methods.FirstOrDefault(m => m.Name == "GetLoggedId");
        if (getLoggedId != null)
        {
            var stringType = module.TypeSystem.String;
            var isNullOrEmpty = module.ImportReference(new MethodReference("IsNullOrEmpty", module.TypeSystem.Boolean, stringType)
            {
                Parameters = { new ParameterDefinition(stringType) }
            });
            var opEquality = module.ImportReference(new MethodReference("op_Equality", module.TypeSystem.Boolean, stringType)
            {
                Parameters = { new ParameterDefinition(stringType), new ParameterDefinition(stringType) }
            });

            var platformUtilsType = module.GetType("PlatformUtils") ?? new TypeReference("", "PlatformUtils", module, module.AssemblyReferences.First(a => a.Name == "Assembly-CSharp"));
            var get_main = module.ImportReference(new MethodReference("get_main", platformUtilsType, platformUtilsType));
            var getCurrentUserId = module.ImportReference(new MethodReference("GetCurrentUserId", stringType, platformUtilsType) { HasThis = true });

            getLoggedId.Body.Instructions.Clear();
            getLoggedId.Body.ExceptionHandlers.Clear();
            getLoggedId.Body.Variables.Clear();

            var idVar = new VariableDefinition(stringType);
            var rawVar = new VariableDefinition(stringType);
            getLoggedId.Body.Variables.Add(idVar);
            getLoggedId.Body.Variables.Add(rawVar);

            var il = getLoggedId.Body.GetILProcessor();

            // Default: idVar = "76561198000000001";
            il.Append(il.Create(OpCodes.Ldstr, "76561198000000001"));
            il.Append(il.Create(OpCodes.Stloc, idVar));

            var tryStart = il.Create(OpCodes.Call, get_main);
            var afterTryCatch = il.Create(OpCodes.Nop);
            var leaveTry = il.Create(OpCodes.Leave, afterTryCatch);
            var insideTrySkip = il.Create(OpCodes.Nop);

            // try {
            il.Append(tryStart);
            il.Append(il.Create(OpCodes.Brfalse, insideTrySkip));

            il.Append(il.Create(OpCodes.Call, get_main));
            il.Append(il.Create(OpCodes.Callvirt, getCurrentUserId));
            il.Append(il.Create(OpCodes.Stloc, rawVar));

            il.Append(il.Create(OpCodes.Ldloc, rawVar));
            il.Append(il.Create(OpCodes.Call, isNullOrEmpty));
            il.Append(il.Create(OpCodes.Brtrue, insideTrySkip));

            il.Append(il.Create(OpCodes.Ldloc, rawVar));
            il.Append(il.Create(OpCodes.Ldstr, "0"));
            il.Append(il.Create(OpCodes.Call, opEquality));
            il.Append(il.Create(OpCodes.Brtrue, insideTrySkip));

            il.Append(il.Create(OpCodes.Ldloc, rawVar));
            il.Append(il.Create(OpCodes.Stloc, idVar));

            il.Append(insideTrySkip);
            il.Append(leaveTry);
            // }

            // catch (Exception) { }
            var handlerStart = il.Create(OpCodes.Pop);
            il.Append(handlerStart);
            var leaveCatch = il.Create(OpCodes.Leave, afterTryCatch);
            il.Append(leaveCatch);

            // afterTryCatch:
            il.Append(afterTryCatch);
            il.Append(il.Create(OpCodes.Ldloc, idVar));
            il.Append(il.Create(OpCodes.Ret));

            var exceptionRef = CoreType(module, "System", "Exception");
            var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = handlerStart,
                HandlerStart = handlerStart,
                HandlerEnd = afterTryCatch,
                CatchType = module.ImportReference(exceptionRef)
            };
            getLoggedId.Body.ExceptionHandlers.Add(handler);
            getLoggedId.Body.OptimizeMacros();
        }

        var getLoggedInName = toolsType.Methods.FirstOrDefault(m => m.Name == "GetLoggedInName");
        if (getLoggedInName != null)
        {
            var stringType = module.TypeSystem.String;
            var isNullOrEmpty = module.ImportReference(new MethodReference("IsNullOrEmpty", module.TypeSystem.Boolean, stringType)
            {
                Parameters = { new ParameterDefinition(stringType) }
            });

            var envType = CoreType(module, "System", "Environment");
            var getUserName = module.ImportReference(new MethodReference("get_UserName", stringType, envType));

            var platformUtilsType = module.GetType("PlatformUtils") ?? new TypeReference("", "PlatformUtils", module, module.AssemblyReferences.First(a => a.Name == "Assembly-CSharp"));
            var get_main = module.ImportReference(new MethodReference("get_main", platformUtilsType, platformUtilsType));
            var getLoggedInUserName = module.ImportReference(new MethodReference("GetLoggedInUserName", stringType, platformUtilsType) { HasThis = true });

            getLoggedInName.Body.Instructions.Clear();
            getLoggedInName.Body.ExceptionHandlers.Clear();
            getLoggedInName.Body.Variables.Clear();

            var nameVar = new VariableDefinition(stringType);
            var rawNameVar = new VariableDefinition(stringType);
            getLoggedInName.Body.Variables.Add(nameVar);
            getLoggedInName.Body.Variables.Add(rawNameVar);

            var il = getLoggedInName.Body.GetILProcessor();

            // Default: nameVar = Environment.UserName;
            il.Append(il.Create(OpCodes.Call, getUserName));
            il.Append(il.Create(OpCodes.Stloc, nameVar));

            // if (string.IsNullOrEmpty(nameVar)) nameVar = "Player";
            var afterDefaultNameCheck = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldloc, nameVar));
            il.Append(il.Create(OpCodes.Call, isNullOrEmpty));
            il.Append(il.Create(OpCodes.Brfalse, afterDefaultNameCheck));

            il.Append(il.Create(OpCodes.Ldstr, "Player"));
            il.Append(il.Create(OpCodes.Stloc, nameVar));

            il.Append(afterDefaultNameCheck);

            // try {
            var tryStart = il.Create(OpCodes.Call, get_main);
            var afterTryCatch = il.Create(OpCodes.Nop);
            var leaveTry = il.Create(OpCodes.Leave, afterTryCatch);
            var insideTrySkip = il.Create(OpCodes.Nop);

            il.Append(tryStart);
            il.Append(il.Create(OpCodes.Brfalse, insideTrySkip));

            il.Append(il.Create(OpCodes.Call, get_main));
            il.Append(il.Create(OpCodes.Callvirt, getLoggedInUserName));
            il.Append(il.Create(OpCodes.Stloc, rawNameVar));

            il.Append(il.Create(OpCodes.Ldloc, rawNameVar));
            il.Append(il.Create(OpCodes.Call, isNullOrEmpty));
            il.Append(il.Create(OpCodes.Brtrue, insideTrySkip));

            il.Append(il.Create(OpCodes.Ldloc, rawNameVar));
            il.Append(il.Create(OpCodes.Stloc, nameVar));

            il.Append(insideTrySkip);
            il.Append(leaveTry);
            // }

            // catch (Exception) { }
            var handlerStart = il.Create(OpCodes.Pop);
            il.Append(handlerStart);
            var leaveCatch = il.Create(OpCodes.Leave, afterTryCatch);
            il.Append(leaveCatch);

            // afterTryCatch:
            il.Append(afterTryCatch);
            il.Append(il.Create(OpCodes.Ldloc, nameVar));
            il.Append(il.Create(OpCodes.Ret));

            var exceptionRef = CoreType(module, "System", "Exception");
            var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = handlerStart,
                HandlerStart = handlerStart,
                HandlerEnd = afterTryCatch,
                CatchType = module.ImportReference(exceptionRef)
            };
            getLoggedInName.Body.ExceptionHandlers.Add(handler);
            getLoggedInName.Body.OptimizeMacros();
        }
    }

    static void PatchErrorMessagesShowConnectionError(TypeDefinition errorMessagesType, ModuleDefinition module)
    {
        var method = errorMessagesType.Methods.FirstOrDefault(m => m.Name == "ShowConnectionError");
        var showNormal = errorMessagesType.Methods.FirstOrDefault(m => m.Name == "ShowNormalErrorMessage");
        if (method != null && showNormal != null)
        {
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();

            var il = method.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldstr, "Connection failed.\nIf hosting on Linux, ensure UDP port 666 is permitted:\nsudo sysctl -w net.ipv4.ip_unprivileged_port_start=666"));
            il.Append(il.Create(OpCodes.Call, showNormal));
            il.Append(il.Create(OpCodes.Ret));
        }
    }

    static void PatchGetDownloadFiles(TypeDefinition apiDataFormat, ModuleDefinition module)
    {
        var method = apiDataFormat.Methods.FirstOrDefault(m => m.Name == "GetDownloadFiles");
        if (method == null) return;

        method.Body.SimplifyMacros();

        var downloadItemType = module.GetType("Subnautica.API.Features.Helper.ApiDataDownloadItem");
        var getRemoteUrl = downloadItemType.Properties.First(p => p.Name == "RemoteUrl").GetMethod;

        var stringContains = new MethodReference("Contains", module.TypeSystem.Boolean, module.TypeSystem.String)
        {
            HasThis = true,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default
        };
        stringContains.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var stringEndsWith = new MethodReference("EndsWith", module.TypeSystem.Boolean, module.TypeSystem.String)
        {
            HasThis = true,
            ExplicitThis = false,
            CallingConvention = MethodCallingConvention.Default
        };
        stringEndsWith.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var urlVar = new VariableDefinition(module.TypeSystem.String);
        method.Body.Variables.Add(urlVar);

        var il = method.Body.GetILProcessor();

        Instruction? targetInst = null;
        Instruction? nextLoopInst = null;

        for (int i = 0; i < method.Body.Instructions.Count; i++)
        {
            var inst = method.Body.Instructions[i];
            if (inst.OpCode == OpCodes.Stloc_2 || inst.OpCode == OpCodes.Stloc)
            {
                if (inst.Operand is VariableDefinition vd && vd.Index == 2 || inst.OpCode == OpCodes.Stloc_2)
                {
                    targetInst = method.Body.Instructions[i + 1].OpCode == OpCodes.Nop ? method.Body.Instructions[i + 2] : method.Body.Instructions[i + 1];
                }
            }
            if ((inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) && inst.Operand is MethodReference mr && mr.Name == "MoveNext")
            {
                nextLoopInst = method.Body.Instructions[i - 1];
            }
        }

        if (targetInst != null && nextLoopInst != null)
        {
            il.InsertBefore(targetInst, il.Create(OpCodes.Ldloc_2));
            il.InsertBefore(targetInst, il.Create(OpCodes.Callvirt, module.ImportReference(getRemoteUrl)));
            il.InsertBefore(targetInst, il.Create(OpCodes.Stloc, urlVar));

            il.InsertBefore(targetInst, il.Create(OpCodes.Ldloc, urlVar));
            il.InsertBefore(targetInst, il.Create(OpCodes.Ldstr, "netbird"));
            il.InsertBefore(targetInst, il.Create(OpCodes.Callvirt, stringContains));
            il.InsertBefore(targetInst, il.Create(OpCodes.Brtrue, nextLoopInst));

            il.InsertBefore(targetInst, il.Create(OpCodes.Ldloc, urlVar));
            il.InsertBefore(targetInst, il.Create(OpCodes.Ldstr, "wintun"));
            il.InsertBefore(targetInst, il.Create(OpCodes.Callvirt, stringContains));
            il.InsertBefore(targetInst, il.Create(OpCodes.Brtrue, nextLoopInst));

            il.InsertBefore(targetInst, il.Create(OpCodes.Ldloc, urlVar));
            il.InsertBefore(targetInst, il.Create(OpCodes.Ldstr, "Firewall"));
            il.InsertBefore(targetInst, il.Create(OpCodes.Callvirt, stringContains));
            il.InsertBefore(targetInst, il.Create(OpCodes.Brtrue, nextLoopInst));

            il.InsertBefore(targetInst, il.Create(OpCodes.Ldloc, urlVar));
            il.InsertBefore(targetInst, il.Create(OpCodes.Ldstr, "NetBird"));
            il.InsertBefore(targetInst, il.Create(OpCodes.Callvirt, stringContains));
            il.InsertBefore(targetInst, il.Create(OpCodes.Brtrue, nextLoopInst));

            il.InsertBefore(targetInst, il.Create(OpCodes.Ldloc, urlVar));
            il.InsertBefore(targetInst, il.Create(OpCodes.Ldstr, "Subnautica.API.dll"));
            il.InsertBefore(targetInst, il.Create(OpCodes.Callvirt, stringEndsWith));
            il.InsertBefore(targetInst, il.Create(OpCodes.Brtrue, nextLoopInst));
        }

        method.Body.OptimizeMacros();
    }

    static void HookDownloadDependencies(MethodDefinition downloadDeps, ModuleDefinition module)
    {
        downloadDeps.Body.SimplifyMacros();

        var envType = CoreType(module, "System", "Environment");
        var specialFolderType = new TypeReference("System", "SpecialFolder", module, module.TypeSystem.CoreLibrary) { DeclaringType = envType, IsValueType = true };
        var pathType = CoreType(module, "System.IO", "Path");
        var fileType = CoreType(module, "System.IO", "File");
        var fileStreamType = CoreType(module, "System.IO", "FileStream");
        var dirType = CoreType(module, "System.IO", "Directory");
        var dirInfoType = CoreType(module, "System.IO", "DirectoryInfo");
        var streamType = CoreType(module, "System.IO", "Stream");
        var asmType = CoreType(module, "System.Reflection", "Assembly");
        var disposableType = CoreType(module, "System", "IDisposable");

        var sysAsm = GetSystemAssemblyRef(module);
        var deflateStreamType = new TypeReference("System.IO.Compression", "DeflateStream", module, sysAsm);
        var compressionModeType = new TypeReference("System.IO.Compression", "CompressionMode", module, sysAsm) { IsValueType = true };

        var getFolderPath = new MethodReference("GetFolderPath", module.TypeSystem.String, envType);
        getFolderPath.Parameters.Add(new ParameterDefinition(specialFolderType));

        var pathCombine = new MethodReference("Combine", module.TypeSystem.String, pathType);
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getExecutingAssembly = new MethodReference("GetExecutingAssembly", asmType, asmType);

        var getManifestResourceStream = new MethodReference("GetManifestResourceStream", streamType, asmType) { HasThis = true };
        getManifestResourceStream.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileCreate = new MethodReference("Create", fileStreamType, fileType);
        fileCreate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var dirCreate = new MethodReference("CreateDirectory", dirInfoType, dirType);
        dirCreate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getDirName = new MethodReference("GetDirectoryName", module.TypeSystem.String, pathType);
        getDirName.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var copyTo = new MethodReference("CopyTo", module.TypeSystem.Void, streamType) { HasThis = true };
        copyTo.Parameters.Add(new ParameterDefinition(streamType));

        var disposeStream = new MethodReference("Dispose", module.TypeSystem.Void, disposableType) { HasThis = true };

        var deflateCtor = new MethodReference(".ctor", module.TypeSystem.Void, deflateStreamType) { HasThis = true };
        deflateCtor.Parameters.Add(new ParameterDefinition(streamType));
        deflateCtor.Parameters.Add(new ParameterDefinition(compressionModeType));

        var il = downloadDeps.Body.GetILProcessor();
        var rets = downloadDeps.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();

        foreach (var ret in rets)
        {
            var targetPathVar = new VariableDefinition(module.TypeSystem.String);
            var inStreamVar = new VariableDefinition(streamType);
            var deflateStreamVar = new VariableDefinition(streamType);
            var outStreamVar = new VariableDefinition(streamType);

            downloadDeps.Body.Variables.Add(targetPathVar);
            downloadDeps.Body.Variables.Add(inStreamVar);
            downloadDeps.Body.Variables.Add(deflateStreamVar);
            downloadDeps.Body.Variables.Add(outStreamVar);

            var skipLabel = il.Create(OpCodes.Nop);

            // targetPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), @".botbenson\Subnautica Below Zero\Game\Dependencies\Subnautica.API.dll");
            il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4, (int)Environment.SpecialFolder.ApplicationData));
            il.InsertBefore(ret, il.Create(OpCodes.Call, getFolderPath));
            il.InsertBefore(ret, il.Create(OpCodes.Ldstr, @".botbenson\Subnautica Below Zero\Game\Dependencies\Subnautica.API.dll"));
            il.InsertBefore(ret, il.Create(OpCodes.Call, pathCombine));
            il.InsertBefore(ret, il.Create(OpCodes.Stloc, targetPathVar));

            // Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, targetPathVar));
            il.InsertBefore(ret, il.Create(OpCodes.Call, getDirName));
            il.InsertBefore(ret, il.Create(OpCodes.Call, dirCreate));
            il.InsertBefore(ret, il.Create(OpCodes.Pop));

            // inStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("costura.subnautica.api.dll.compressed");
            il.InsertBefore(ret, il.Create(OpCodes.Call, getExecutingAssembly));
            il.InsertBefore(ret, il.Create(OpCodes.Ldstr, "costura.subnautica.api.dll.compressed"));
            il.InsertBefore(ret, il.Create(OpCodes.Callvirt, getManifestResourceStream));
            il.InsertBefore(ret, il.Create(OpCodes.Stloc, inStreamVar));

            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, inStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Brfalse_S, skipLabel));

            // deflate = new DeflateStream(inStream, CompressionMode.Decompress);
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, inStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_0)); // Decompress = 0
            il.InsertBefore(ret, il.Create(OpCodes.Newobj, deflateCtor));
            il.InsertBefore(ret, il.Create(OpCodes.Stloc, deflateStreamVar));

            // outStream = File.Create(targetPath);
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, targetPathVar));
            il.InsertBefore(ret, il.Create(OpCodes.Call, fileCreate));
            il.InsertBefore(ret, il.Create(OpCodes.Stloc, outStreamVar));

            // deflate.CopyTo(outStream);
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, deflateStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, outStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Callvirt, copyTo));

            // outStream.Dispose();
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, outStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Callvirt, disposeStream));

            // deflate.Dispose();
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, deflateStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Callvirt, disposeStream));

            // inStream.Dispose();
            il.InsertBefore(ret, il.Create(OpCodes.Ldloc, inStreamVar));
            il.InsertBefore(ret, il.Create(OpCodes.Callvirt, disposeStream));

            il.InsertBefore(ret, skipLabel);
        }

        downloadDeps.Body.OptimizeMacros();
    }

    static void PatchSteamGetPlatformPath(TypeDefinition steamPlatform, ModuleDefinition module)
    {
        var method = steamPlatform.Methods.FirstOrDefault(m => m.Name == "GetPlatformPath");
        if (method == null) return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();

        // return "Proton";
        il.Append(il.Create(OpCodes.Ldstr, "Proton"));
        il.Append(il.Create(OpCodes.Ret));
    }

    static void PatchSteamGetGamePath(TypeDefinition steamPlatform, ModuleDefinition module)
    {
        var method = steamPlatform.Methods.FirstOrDefault(m => m.Name == "GetGamePath");
        if (method == null) return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();

        var il = method.Body.GetILProcessor();

        var envVar = new VariableDefinition(module.TypeSystem.String);
        var exeVar = new VariableDefinition(module.TypeSystem.String);
        var baseDirVar = new VariableDefinition(module.TypeSystem.String);
        var parentVar = new VariableDefinition(module.TypeSystem.String);

        method.Body.Variables.Add(envVar);
        method.Body.Variables.Add(exeVar);
        method.Body.Variables.Add(baseDirVar);
        method.Body.Variables.Add(parentVar);

        var envType = CoreType(module, "System", "Environment");
        var pathType = CoreType(module, "System.IO", "Path");
        var fileType = CoreType(module, "System.IO", "File");
        var dirType = CoreType(module, "System.IO", "Directory");
        var appDomainType = CoreType(module, "System", "AppDomain");

        var getEnv = new MethodReference("GetEnvironmentVariable", module.TypeSystem.String, envType);
        getEnv.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var stringIsNullOrEmpty = new MethodReference("IsNullOrEmpty", module.TypeSystem.Boolean, module.TypeSystem.String);
        stringIsNullOrEmpty.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileExists = new MethodReference("Exists", module.TypeSystem.Boolean, fileType);
        fileExists.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var pathCombine = new MethodReference("Combine", module.TypeSystem.String, pathType);
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getCurrentDomain = new MethodReference("get_CurrentDomain", appDomainType, appDomainType);

        var getBaseDir = new MethodReference("get_BaseDirectory", module.TypeSystem.String, appDomainType) { HasThis = true };

        var getCurDir = new MethodReference("GetCurrentDirectory", module.TypeSystem.String, dirType);

        var getDirName = new MethodReference("GetDirectoryName", module.TypeSystem.String, pathType);
        getDirName.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getUserName = new MethodReference("get_UserName", module.TypeSystem.String, envType);

        var stringConcat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
        stringConcat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        stringConcat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        stringConcat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var checkSubnauticaZeroPath = il.Create(OpCodes.Nop);
        var checkBaseDir = il.Create(OpCodes.Nop);
        var checkCurDir = il.Create(OpCodes.Nop);
        var checkZDrive = il.Create(OpCodes.Nop);

        // 1. STEAM_COMPAT_INSTALL_PATH
        il.Append(il.Create(OpCodes.Ldstr, "STEAM_COMPAT_INSTALL_PATH"));
        il.Append(il.Create(OpCodes.Call, getEnv));
        il.Append(il.Create(OpCodes.Stloc, envVar));
        il.Append(il.Create(OpCodes.Ldloc, envVar));
        il.Append(il.Create(OpCodes.Call, stringIsNullOrEmpty));
        il.Append(il.Create(OpCodes.Brtrue, checkSubnauticaZeroPath));

        il.Append(il.Create(OpCodes.Ldloc, envVar));
        il.Append(il.Create(OpCodes.Ldstr, "SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, exeVar));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, checkSubnauticaZeroPath));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Ret));

        // 2. SUBNAUTICA_ZERO_PATH
        il.Append(checkSubnauticaZeroPath);
        il.Append(il.Create(OpCodes.Ldstr, "SUBNAUTICA_ZERO_PATH"));
        il.Append(il.Create(OpCodes.Call, getEnv));
        il.Append(il.Create(OpCodes.Stloc, envVar));
        il.Append(il.Create(OpCodes.Ldloc, envVar));
        il.Append(il.Create(OpCodes.Call, stringIsNullOrEmpty));
        il.Append(il.Create(OpCodes.Brtrue, checkBaseDir));

        il.Append(il.Create(OpCodes.Ldloc, envVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, checkBaseDir));
        il.Append(il.Create(OpCodes.Ldloc, envVar));
        il.Append(il.Create(OpCodes.Ret));

        // 3. AppDomain.CurrentDomain.BaseDirectory
        il.Append(checkBaseDir);
        il.Append(il.Create(OpCodes.Call, getCurrentDomain));
        il.Append(il.Create(OpCodes.Callvirt, getBaseDir));
        il.Append(il.Create(OpCodes.Stloc, baseDirVar));

        il.Append(il.Create(OpCodes.Ldloc, baseDirVar));
        il.Append(il.Create(OpCodes.Ldstr, "SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, exeVar));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        var checkParent = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Brfalse, checkParent));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Ret));

        // Parent dir of BaseDirectory
        il.Append(checkParent);
        il.Append(il.Create(OpCodes.Ldloc, baseDirVar));
        il.Append(il.Create(OpCodes.Call, getDirName));
        il.Append(il.Create(OpCodes.Stloc, parentVar));
        il.Append(il.Create(OpCodes.Ldloc, parentVar));
        il.Append(il.Create(OpCodes.Call, stringIsNullOrEmpty));
        il.Append(il.Create(OpCodes.Brtrue, checkCurDir));

        il.Append(il.Create(OpCodes.Ldloc, parentVar));
        il.Append(il.Create(OpCodes.Ldstr, "SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, exeVar));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, checkCurDir));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Ret));

        // 4. Directory.GetCurrentDirectory()
        il.Append(checkCurDir);
        il.Append(il.Create(OpCodes.Call, getCurDir));
        il.Append(il.Create(OpCodes.Ldstr, "SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, exeVar));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, checkZDrive));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Ret));

        // 5. Common Linux Steam paths: Z:\home\{user}\.local\share\Steam\steamapps\common\SubnauticaZero\SubnauticaZero.exe
        il.Append(checkZDrive);
        il.Append(il.Create(OpCodes.Ldstr, @"Z:\home\"));
        il.Append(il.Create(OpCodes.Call, getUserName));
        il.Append(il.Create(OpCodes.Ldstr, @"\.local\share\Steam\steamapps\common\SubnauticaZero\SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Call, stringConcat));
        il.Append(il.Create(OpCodes.Stloc, exeVar));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        var checkSteam2 = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Brfalse, checkSteam2));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Ret));

        // 5b. Z:\home\{user}\.steam\steam\steamapps\common\SubnauticaZero\SubnauticaZero.exe
        il.Append(checkSteam2);
        il.Append(il.Create(OpCodes.Ldstr, @"Z:\home\"));
        il.Append(il.Create(OpCodes.Call, getUserName));
        il.Append(il.Create(OpCodes.Ldstr, @"\.steam\steam\steamapps\common\SubnauticaZero\SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Call, stringConcat));
        il.Append(il.Create(OpCodes.Stloc, exeVar));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        var fallbackLabel = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Brfalse, fallbackLabel));
        il.Append(il.Create(OpCodes.Ldloc, exeVar));
        il.Append(il.Create(OpCodes.Ret));

        // Fallback: return "SubnauticaZero.exe"
        il.Append(fallbackLabel);
        il.Append(il.Create(OpCodes.Ldstr, "SubnauticaZero.exe"));
        il.Append(il.Create(OpCodes.Ret));
    }

    static void PatchSteamStartGame(TypeDefinition steamPlatform, ModuleDefinition module)
    {
        var method = steamPlatform.Methods.FirstOrDefault(m => m.Name == "StartGame");
        if (method == null) return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();

        var il = method.Body.GetILProcessor();

        var getGamePath = module.ImportReference(steamPlatform.Methods.First(m => m.Name == "GetGamePath"));
        var fileType = CoreType(module, "System.IO", "File");
        var pathType = CoreType(module, "System.IO", "Path");

        var fileExists = new MethodReference("Exists", module.TypeSystem.Boolean, fileType);
        fileExists.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getDirName = new MethodReference("GetDirectoryName", module.TypeSystem.String, pathType);
        getDirName.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var sysAsm = GetSystemAssemblyRef(module);
        var psiType = new TypeReference("System.Diagnostics", "ProcessStartInfo", module, sysAsm);
        var psiCtor = new MethodReference(".ctor", module.TypeSystem.Void, psiType) { HasThis = true };

        var setFileName = new MethodReference("set_FileName", module.TypeSystem.Void, psiType) { HasThis = true };
        setFileName.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var setWorkingDir = new MethodReference("set_WorkingDirectory", module.TypeSystem.Void, psiType) { HasThis = true };
        setWorkingDir.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var processType = new TypeReference("System.Diagnostics", "Process", module, sysAsm);
        var processStart = new MethodReference("Start", processType, processType);
        processStart.Parameters.Add(new ParameterDefinition(psiType));

        var gamePathVar = new VariableDefinition(module.TypeSystem.String);
        var psiVar = new VariableDefinition(psiType);
        method.Body.Variables.Add(gamePathVar);
        method.Body.Variables.Add(psiVar);

        var retFalseLabel = il.Create(OpCodes.Ldc_I4_0);
        var retInst = il.Create(OpCodes.Ret);

        // gamePath = this.GetGamePath();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, getGamePath));
        il.Append(il.Create(OpCodes.Stloc, gamePathVar));

        // if (!File.Exists(gamePath)) return false;
        il.Append(il.Create(OpCodes.Ldloc, gamePathVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, retFalseLabel));

        // psi = new ProcessStartInfo();
        il.Append(il.Create(OpCodes.Newobj, psiCtor));
        il.Append(il.Create(OpCodes.Stloc, psiVar));

        // psi.FileName = gamePath;
        il.Append(il.Create(OpCodes.Ldloc, psiVar));
        il.Append(il.Create(OpCodes.Ldloc, gamePathVar));
        il.Append(il.Create(OpCodes.Callvirt, setFileName));

        // psi.WorkingDirectory = Path.GetDirectoryName(gamePath);
        il.Append(il.Create(OpCodes.Ldloc, psiVar));
        il.Append(il.Create(OpCodes.Ldloc, gamePathVar));
        il.Append(il.Create(OpCodes.Call, getDirName));
        il.Append(il.Create(OpCodes.Callvirt, setWorkingDir));

        // Process.Start(psi);
        il.Append(il.Create(OpCodes.Ldloc, psiVar));
        il.Append(il.Create(OpCodes.Call, processStart));
        il.Append(il.Create(OpCodes.Pop));

        // return true;
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));

        // return false;
        il.Append(retFalseLabel);
        il.Append(retInst);

        method.Body.OptimizeMacros();
    }

    static void PatchSteamUpdateAssembly(TypeDefinition steamPlatform, ModuleDefinition module)
    {
        var method = steamPlatform.Methods.FirstOrDefault(m => m.Name == "UpdateAssembly");
        if (method == null) return;

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();

        var il = method.Body.GetILProcessor();

        var getAssemblyFilePath = module.ImportReference(steamPlatform.Methods.First(m => m.Name == "GetAssemblyFilePath"));

        var envType = CoreType(module, "System", "Environment");
        var specialFolderType = new TypeReference("System", "SpecialFolder", module, module.TypeSystem.CoreLibrary) { DeclaringType = envType, IsValueType = true };
        var pathType = CoreType(module, "System.IO", "Path");
        var fileType = CoreType(module, "System.IO", "File");

        var getFolderPath = new MethodReference("GetFolderPath", module.TypeSystem.String, envType);
        getFolderPath.Parameters.Add(new ParameterDefinition(specialFolderType));

        var pathCombine = new MethodReference("Combine", module.TypeSystem.String, pathType);
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileExists = new MethodReference("Exists", module.TypeSystem.Boolean, fileType);
        fileExists.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileCopy = new MethodReference("Copy", module.TypeSystem.Void, fileType);
        fileCopy.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        fileCopy.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        fileCopy.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

        var stringConcat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
        stringConcat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        stringConcat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var stringIsNullOrEmpty = new MethodReference("IsNullOrEmpty", module.TypeSystem.Boolean, module.TypeSystem.String);
        stringIsNullOrEmpty.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var targetVar = new VariableDefinition(module.TypeSystem.String);
        var sourceVar = new VariableDefinition(module.TypeSystem.String);
        var backupVar = new VariableDefinition(module.TypeSystem.String);

        method.Body.Variables.Add(targetVar);
        method.Body.Variables.Add(sourceVar);
        method.Body.Variables.Add(backupVar);

        var retFalseLabel = il.Create(OpCodes.Ldc_I4_0);
        var retInst = il.Create(OpCodes.Ret);
        var skipBackupLabel = il.Create(OpCodes.Nop);

        // target = this.GetAssemblyFilePath();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, getAssemblyFilePath));
        il.Append(il.Create(OpCodes.Stloc, targetVar));

        il.Append(il.Create(OpCodes.Ldloc, targetVar));
        il.Append(il.Create(OpCodes.Call, stringIsNullOrEmpty));
        il.Append(il.Create(OpCodes.Brtrue, retFalseLabel));

        // source = Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), @".botbenson\Subnautica Below Zero\Game\Core\Assembly-CSharp.dll");
        il.Append(il.Create(OpCodes.Ldc_I4, (int)Environment.SpecialFolder.ApplicationData));
        il.Append(il.Create(OpCodes.Call, getFolderPath));
        il.Append(il.Create(OpCodes.Ldstr, @".botbenson\Subnautica Below Zero\Game\Core\Assembly-CSharp.dll"));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, sourceVar));

        il.Append(il.Create(OpCodes.Ldloc, sourceVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, retFalseLabel));

        // backup = target + ".orig";
        il.Append(il.Create(OpCodes.Ldloc, targetVar));
        il.Append(il.Create(OpCodes.Ldstr, ".orig"));
        il.Append(il.Create(OpCodes.Call, stringConcat));
        il.Append(il.Create(OpCodes.Stloc, backupVar));

        // if (File.Exists(target) && !File.Exists(backup)) File.Copy(target, backup, false);
        il.Append(il.Create(OpCodes.Ldloc, targetVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, skipBackupLabel));

        il.Append(il.Create(OpCodes.Ldloc, backupVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brtrue, skipBackupLabel));

        il.Append(il.Create(OpCodes.Ldloc, targetVar));
        il.Append(il.Create(OpCodes.Ldloc, backupVar));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Call, fileCopy));

        il.Append(skipBackupLabel);

        // File.Copy(source, target, true);
        il.Append(il.Create(OpCodes.Ldloc, sourceVar));
        il.Append(il.Create(OpCodes.Ldloc, targetVar));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Call, fileCopy));

        // return true;
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));

        // return false;
        il.Append(retFalseLabel);
        il.Append(retInst);

        method.Body.OptimizeMacros();
    }

    static void PatchToolsTls(TypeDefinition toolsType, ModuleDefinition module)
    {
        var systemAsm = module.AssemblyReferences.FirstOrDefault(a => a.Name == "System");
        if (systemAsm == null) return;

        var x509CertRef = new TypeReference("System.Security.Cryptography.X509Certificates", "X509Certificate", module, module.TypeSystem.CoreLibrary);
        var x509ChainRef = new TypeReference("System.Security.Cryptography.X509Certificates", "X509Chain", module, systemAsm);
        var sslPolicyErrorsRef = new TypeReference("System.Net.Security", "SslPolicyErrors", module, systemAsm) { IsValueType = true };

        var cbMethod = new MethodDefinition("ValidateServerCertificate", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
        cbMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        cbMethod.Parameters.Add(new ParameterDefinition(x509CertRef));
        cbMethod.Parameters.Add(new ParameterDefinition(x509ChainRef));
        cbMethod.Parameters.Add(new ParameterDefinition(sslPolicyErrorsRef));
        cbMethod.Body = new MethodBody(cbMethod);
        cbMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        cbMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        toolsType.Methods.Add(cbMethod);

        var spmType = new TypeReference("System.Net", "ServicePointManager", module, systemAsm);
        var secProtoProp = new MethodReference("set_SecurityProtocol", module.TypeSystem.Void, spmType);
        secProtoProp.Parameters.Add(new ParameterDefinition(new TypeReference("System.Net", "SecurityProtocolType", module, systemAsm) { IsValueType = true }));

        var callbackProp = new MethodReference("set_ServerCertificateValidationCallback", module.TypeSystem.Void, spmType);
        var callbackType = new TypeReference("System.Net.Security", "RemoteCertificateValidationCallback", module, systemAsm);
        callbackProp.Parameters.Add(new ParameterDefinition(callbackType));

        var callbackCtor = new MethodReference(".ctor", module.TypeSystem.Void, callbackType);
        callbackCtor.HasThis = true;
        callbackCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        callbackCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));

        var cctor = toolsType.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (cctor != null)
        {
            var il = cctor.Body.GetILProcessor();
            var first = cctor.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 4032));
            il.InsertBefore(first, il.Create(OpCodes.Call, secProtoProp));
            il.InsertBefore(first, il.Create(OpCodes.Ldnull));
            il.InsertBefore(first, il.Create(OpCodes.Ldftn, cbMethod));
            il.InsertBefore(first, il.Create(OpCodes.Newobj, callbackCtor));
            il.InsertBefore(first, il.Create(OpCodes.Call, callbackProp));
        }
    }

    static void InjectLauncherBootstrap(ModuleDefinition module, byte[] apiJsonB64Bytes, byte[] creditsJsonBytes)
    {
        if (apiJsonB64Bytes.Length > 0)
        {
            module.Resources.Add(new EmbeddedResource("embedded_api_json", ManifestResourceAttributes.Private, apiJsonB64Bytes));
        }
        if (creditsJsonBytes.Length > 0)
        {
            module.Resources.Add(new EmbeddedResource("embedded_credits_json", ManifestResourceAttributes.Private, creditsJsonBytes));
        }

        var progType = module.GetType("Subnautica.Multiplayer.Program");
        if (progType == null) return;

        var systemAsm = module.AssemblyReferences.FirstOrDefault(a => a.Name == "System");
        if (systemAsm == null) return;

        var x509CertRef = new TypeReference("System.Security.Cryptography.X509Certificates", "X509Certificate", module, module.TypeSystem.CoreLibrary);
        var x509ChainRef = new TypeReference("System.Security.Cryptography.X509Certificates", "X509Chain", module, systemAsm);
        var sslPolicyErrorsRef = new TypeReference("System.Net.Security", "SslPolicyErrors", module, systemAsm) { IsValueType = true };

        var cbMethod = new MethodDefinition("ValidateServerCertificate", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
        cbMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        cbMethod.Parameters.Add(new ParameterDefinition(x509CertRef));
        cbMethod.Parameters.Add(new ParameterDefinition(x509ChainRef));
        cbMethod.Parameters.Add(new ParameterDefinition(sslPolicyErrorsRef));
        cbMethod.Body = new MethodBody(cbMethod);
        cbMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        cbMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        progType.Methods.Add(cbMethod);

        var bootMethod = new MethodDefinition("BootstrapLinux", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        bootMethod.Body = new MethodBody(bootMethod);
        bootMethod.Body.InitLocals = true;
        var il = bootMethod.Body.GetILProcessor();

        var spmType = new TypeReference("System.Net", "ServicePointManager", module, systemAsm);
        var secProtoProp = new MethodReference("set_SecurityProtocol", module.TypeSystem.Void, spmType);
        secProtoProp.Parameters.Add(new ParameterDefinition(new TypeReference("System.Net", "SecurityProtocolType", module, systemAsm) { IsValueType = true }));

        var callbackProp = new MethodReference("set_ServerCertificateValidationCallback", module.TypeSystem.Void, spmType);
        var callbackType = new TypeReference("System.Net.Security", "RemoteCertificateValidationCallback", module, systemAsm);
        callbackProp.Parameters.Add(new ParameterDefinition(callbackType));

        var callbackCtor = new MethodReference(".ctor", module.TypeSystem.Void, callbackType);
        callbackCtor.HasThis = true;
        callbackCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        callbackCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));

        var retTarget = il.Create(OpCodes.Ret);
        var tryStart = il.Create(OpCodes.Ldc_I4, 4032);
        il.Append(tryStart);
        il.Append(il.Create(OpCodes.Call, secProtoProp));
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ldftn, cbMethod));
        il.Append(il.Create(OpCodes.Newobj, callbackCtor));
        il.Append(il.Create(OpCodes.Call, callbackProp));

        InjectUnpackResource(module, il, "embedded_api_json", @".botbenson\Subnautica Below Zero\App\Api.json");
        InjectUnpackResource(module, il, "embedded_credits_json", @".botbenson\Subnautica Below Zero\App\credits.json");
        InjectUnpackCompressedResource(module, il, "costura.subnautica.api.dll.compressed", @".botbenson\Subnautica Below Zero\Game\Dependencies\Subnautica.API.dll");

        var leaveTry = il.Create(OpCodes.Leave, retTarget);
        il.Append(leaveTry);

        var catchHandler = il.Create(OpCodes.Pop);
        il.Append(catchHandler);
        var leaveCatch = il.Create(OpCodes.Leave, retTarget);
        il.Append(leaveCatch);

        il.Append(retTarget);

        var exceptionType = CoreType(module, "System", "Exception");
        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchHandler,
            HandlerStart = catchHandler,
            HandlerEnd = retTarget,
            CatchType = exceptionType
        };
        bootMethod.Body.ExceptionHandlers.Add(handler);

        progType.Methods.Add(bootMethod);

        var mainMethod = progType.Methods.FirstOrDefault(m => m.Name == "Main");
        if (mainMethod != null)
        {
            var mainIl = mainMethod.Body.GetILProcessor();
            mainIl.InsertBefore(mainMethod.Body.Instructions[0], mainIl.Create(OpCodes.Call, bootMethod));
        }
    }

    static void InjectUnpackCompressedResource(ModuleDefinition module, ILProcessor il, string resName, string relSubPath)
    {
        var envType = CoreType(module, "System", "Environment");
        var specialFolderType = new TypeReference("System", "SpecialFolder", module, module.TypeSystem.CoreLibrary) { DeclaringType = envType, IsValueType = true };
        var pathType = CoreType(module, "System.IO", "Path");
        var fileType = CoreType(module, "System.IO", "File");
        var fileStreamType = CoreType(module, "System.IO", "FileStream");
        var dirType = CoreType(module, "System.IO", "Directory");
        var dirInfoType = CoreType(module, "System.IO", "DirectoryInfo");
        var streamType = CoreType(module, "System.IO", "Stream");
        var asmType = CoreType(module, "System.Reflection", "Assembly");
        var disposableType = CoreType(module, "System", "IDisposable");

        var sysAsm = GetSystemAssemblyRef(module);
        var deflateStreamType = new TypeReference("System.IO.Compression", "DeflateStream", module, sysAsm);
        var compressionModeType = new TypeReference("System.IO.Compression", "CompressionMode", module, sysAsm) { IsValueType = true };

        var getFolderPath = new MethodReference("GetFolderPath", module.TypeSystem.String, envType);
        getFolderPath.Parameters.Add(new ParameterDefinition(specialFolderType));

        var pathCombine = new MethodReference("Combine", module.TypeSystem.String, pathType);
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var dirCreate = new MethodReference("CreateDirectory", dirInfoType, dirType);
        dirCreate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getDirName = new MethodReference("GetDirectoryName", module.TypeSystem.String, pathType);
        getDirName.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getExecutingAssembly = new MethodReference("GetExecutingAssembly", asmType, asmType);

        var getManifestResourceStream = new MethodReference("GetManifestResourceStream", streamType, asmType) { HasThis = true };
        getManifestResourceStream.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileCreate = new MethodReference("Create", fileStreamType, fileType);
        fileCreate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var copyTo = new MethodReference("CopyTo", module.TypeSystem.Void, streamType) { HasThis = true };
        copyTo.Parameters.Add(new ParameterDefinition(streamType));

        var disposeStream = new MethodReference("Dispose", module.TypeSystem.Void, disposableType) { HasThis = true };

        var deflateCtor = new MethodReference(".ctor", module.TypeSystem.Void, deflateStreamType) { HasThis = true };
        deflateCtor.Parameters.Add(new ParameterDefinition(streamType));
        deflateCtor.Parameters.Add(new ParameterDefinition(compressionModeType));

        var targetPathVar = new VariableDefinition(module.TypeSystem.String);
        var inStreamVar = new VariableDefinition(streamType);
        var deflateStreamVar = new VariableDefinition(streamType);
        var outStreamVar = new VariableDefinition(streamType);

        il.Body.Variables.Add(targetPathVar);
        il.Body.Variables.Add(inStreamVar);
        il.Body.Variables.Add(deflateStreamVar);
        il.Body.Variables.Add(outStreamVar);

        var skipLabel = il.Create(OpCodes.Nop);

        // targetPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), relSubPath);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)Environment.SpecialFolder.ApplicationData));
        il.Append(il.Create(OpCodes.Call, getFolderPath));
        il.Append(il.Create(OpCodes.Ldstr, relSubPath));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, targetPathVar));

        // Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
        il.Append(il.Create(OpCodes.Ldloc, targetPathVar));
        il.Append(il.Create(OpCodes.Call, getDirName));
        il.Append(il.Create(OpCodes.Call, dirCreate));
        il.Append(il.Create(OpCodes.Pop));

        // inStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
        il.Append(il.Create(OpCodes.Call, getExecutingAssembly));
        il.Append(il.Create(OpCodes.Ldstr, resName));
        il.Append(il.Create(OpCodes.Callvirt, getManifestResourceStream));
        il.Append(il.Create(OpCodes.Stloc, inStreamVar));

        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Brfalse, skipLabel));

        // deflate = new DeflateStream(inStream, CompressionMode.Decompress);
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Ldc_I4_0)); // Decompress = 0
        il.Append(il.Create(OpCodes.Newobj, deflateCtor));
        il.Append(il.Create(OpCodes.Stloc, deflateStreamVar));

        // outStream = File.Create(targetPath);
        il.Append(il.Create(OpCodes.Ldloc, targetPathVar));
        il.Append(il.Create(OpCodes.Call, fileCreate));
        il.Append(il.Create(OpCodes.Stloc, outStreamVar));

        // deflate.CopyTo(outStream);
        il.Append(il.Create(OpCodes.Ldloc, deflateStreamVar));
        il.Append(il.Create(OpCodes.Ldloc, outStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, copyTo));

        // outStream.Dispose();
        il.Append(il.Create(OpCodes.Ldloc, outStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeStream));

        // deflate.Dispose();
        il.Append(il.Create(OpCodes.Ldloc, deflateStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeStream));

        // inStream.Dispose();
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeStream));

        il.Append(skipLabel);
    }

    static void InjectUnpackResource(ModuleDefinition module, ILProcessor il, string resName, string relSubPath)
    {
        var envType = CoreType(module, "System", "Environment");
        var specialFolderType = new TypeReference("System", "SpecialFolder", module, module.TypeSystem.CoreLibrary) { DeclaringType = envType, IsValueType = true };
        var pathType = CoreType(module, "System.IO", "Path");
        var fileType = CoreType(module, "System.IO", "File");
        var fileStreamType = CoreType(module, "System.IO", "FileStream");
        var dirType = CoreType(module, "System.IO", "Directory");
        var dirInfoType = CoreType(module, "System.IO", "DirectoryInfo");
        var streamType = CoreType(module, "System.IO", "Stream");
        var asmType = CoreType(module, "System.Reflection", "Assembly");
        var disposableType = CoreType(module, "System", "IDisposable");

        var getFolderPath = new MethodReference("GetFolderPath", module.TypeSystem.String, envType);
        getFolderPath.Parameters.Add(new ParameterDefinition(specialFolderType));

        var pathCombine = new MethodReference("Combine", module.TypeSystem.String, pathType);
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        pathCombine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileExists = new MethodReference("Exists", module.TypeSystem.Boolean, fileType);
        fileExists.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var dirCreate = new MethodReference("CreateDirectory", dirInfoType, dirType);
        dirCreate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getDirName = new MethodReference("GetDirectoryName", module.TypeSystem.String, pathType);
        getDirName.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var getExecutingAssembly = new MethodReference("GetExecutingAssembly", asmType, asmType);

        var getManifestResourceStream = new MethodReference("GetManifestResourceStream", streamType, asmType) { HasThis = true };
        getManifestResourceStream.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var fileCreate = new MethodReference("Create", fileStreamType, fileType);
        fileCreate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var copyTo = new MethodReference("CopyTo", module.TypeSystem.Void, streamType) { HasThis = true };
        copyTo.Parameters.Add(new ParameterDefinition(streamType));

        var disposeStream = new MethodReference("Dispose", module.TypeSystem.Void, disposableType) { HasThis = true };

        var targetPathVar = new VariableDefinition(module.TypeSystem.String);
        var inStreamVar = new VariableDefinition(streamType);
        var outStreamVar = new VariableDefinition(streamType);

        il.Body.Variables.Add(targetPathVar);
        il.Body.Variables.Add(inStreamVar);
        il.Body.Variables.Add(outStreamVar);

        var skipLabel = il.Create(OpCodes.Nop);

        // targetPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), relSubPath);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)Environment.SpecialFolder.ApplicationData));
        il.Append(il.Create(OpCodes.Call, getFolderPath));
        il.Append(il.Create(OpCodes.Ldstr, relSubPath));
        il.Append(il.Create(OpCodes.Call, pathCombine));
        il.Append(il.Create(OpCodes.Stloc, targetPathVar));

        // if (File.Exists(targetPath)) goto skipLabel;
        il.Append(il.Create(OpCodes.Ldloc, targetPathVar));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brtrue, skipLabel));

        // Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
        il.Append(il.Create(OpCodes.Ldloc, targetPathVar));
        il.Append(il.Create(OpCodes.Call, getDirName));
        il.Append(il.Create(OpCodes.Call, dirCreate));
        il.Append(il.Create(OpCodes.Pop));

        // inStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
        il.Append(il.Create(OpCodes.Call, getExecutingAssembly));
        il.Append(il.Create(OpCodes.Ldstr, resName));
        il.Append(il.Create(OpCodes.Callvirt, getManifestResourceStream));
        il.Append(il.Create(OpCodes.Stloc, inStreamVar));

        // if (inStream == null) goto skipLabel;
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Brfalse, skipLabel));

        // outStream = File.Create(targetPath);
        il.Append(il.Create(OpCodes.Ldloc, targetPathVar));
        il.Append(il.Create(OpCodes.Call, fileCreate));
        il.Append(il.Create(OpCodes.Stloc, outStreamVar));

        // inStream.CopyTo(outStream);
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Ldloc, outStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, copyTo));

        // outStream.Dispose();
        il.Append(il.Create(OpCodes.Ldloc, outStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeStream));

        // inStream.Dispose();
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeStream));

        il.Append(skipLabel);
    }

    static void PatchFirstScreen(TypeDefinition firstScreen, ModuleDefinition module)
    {
        var method = firstScreen.Methods.FirstOrDefault(m => m.Name == "AsyncLoadForm");
        if (method == null) return;

        method.Body.SimplifyMacros();

        var instructions = method.Body.Instructions;
        for (int i = 0; i < instructions.Count; i++)
        {
            var inst = instructions[i];

            // 1. Credits error check
            if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference mr && mr.Name == "CheckCreditsPage")
            {
                for (int j = i + 1; j < i + 10 && j < instructions.Count; j++)
                {
                    if (instructions[j].OpCode == OpCodes.Brfalse_S || instructions[j].OpCode == OpCodes.Brfalse)
                    {
                        instructions[j].OpCode = instructions[j].OpCode == OpCodes.Brfalse_S ? OpCodes.Br_S : OpCodes.Br;
                        Console.WriteLine("  [+] FirstScreen: Credits error check bypassed.");
                        break;
                    }
                }
            }

            // 2. Version error check
            if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference mr2 && mr2.Name == "CheckLauncherVersion")
            {
                for (int j = i + 1; j < i + 15 && j < instructions.Count; j++)
                {
                    if (instructions[j].OpCode == OpCodes.Brfalse_S || instructions[j].OpCode == OpCodes.Brfalse)
                    {
                        instructions[j].OpCode = instructions[j].OpCode == OpCodes.Brfalse_S ? OpCodes.Br_S : OpCodes.Br;
                        Console.WriteLine("  [+] FirstScreen: Version error check bypassed.");
                        break;
                    }
                }
            }

            // 3. Slider image download catch block
            if (inst.OpCode == OpCodes.Ldstr && (string)inst.Operand == "Slider Image Download Error: {0}")
            {
                for (int j = i + 1; j < i + 10 && j < instructions.Count; j++)
                {
                    if (instructions[j].OpCode == OpCodes.Call && instructions[j].Operand is MethodReference logMr && logMr.Name == "Error")
                    {
                        Instruction? leaveTarget = null;
                        for (int k = i - 1; k >= 0; k--)
                        {
                            if (instructions[k].OpCode == OpCodes.Leave_S || instructions[k].OpCode == OpCodes.Leave)
                            {
                                leaveTarget = instructions[k].Operand as Instruction;
                                break;
                            }
                        }

                        if (leaveTarget != null)
                        {
                            instructions[j + 1].OpCode = OpCodes.Leave;
                            instructions[j + 1].Operand = leaveTarget;
                            for (int k = j + 2; k < instructions.Count; k++)
                            {
                                if (instructions[k] == leaveTarget) break;
                                if (instructions[k].OpCode == OpCodes.Leave || instructions[k].OpCode == OpCodes.Leave_S)
                                {
                                    instructions[k].OpCode = OpCodes.Nop;
                                    instructions[k].Operand = null;
                                    break;
                                }
                                instructions[k].OpCode = OpCodes.Nop;
                                instructions[k].Operand = null;
                            }
                            Console.WriteLine("  [+] FirstScreen: Slider image download error popup suppressed.");
                        }
                        break;
                    }
                }
            }

            // 4. Plugin image download catch block
            if (inst.OpCode == OpCodes.Ldstr && (string)inst.Operand == "Plugin Image Download Error: {0}")
            {
                for (int j = i + 1; j < i + 10 && j < instructions.Count; j++)
                {
                    if (instructions[j].OpCode == OpCodes.Call && instructions[j].Operand is MethodReference logMr && logMr.Name == "Error")
                    {
                        Instruction? leaveTarget = null;
                        for (int k = i - 1; k >= 0; k--)
                        {
                            if (instructions[k].OpCode == OpCodes.Leave_S || instructions[k].OpCode == OpCodes.Leave)
                            {
                                leaveTarget = instructions[k].Operand as Instruction;
                                break;
                            }
                        }

                        if (leaveTarget != null)
                        {
                            instructions[j + 1].OpCode = OpCodes.Leave;
                            instructions[j + 1].Operand = leaveTarget;
                            for (int k = j + 2; k < instructions.Count; k++)
                            {
                                if (instructions[k] == leaveTarget) break;
                                if (instructions[k].OpCode == OpCodes.Leave || instructions[k].OpCode == OpCodes.Leave_S)
                                {
                                    instructions[k].OpCode = OpCodes.Nop;
                                    instructions[k].Operand = null;
                                    break;
                                }
                                instructions[k].OpCode = OpCodes.Nop;
                                instructions[k].Operand = null;
                            }
                            Console.WriteLine("  [+] FirstScreen: Plugin image download error popup suppressed.");
                        }
                        break;
                    }
                }
            }
        }

        method.Body.OptimizeMacros();
    }

    static void DumpMethod(string exePath, string typeFilter, string methodFilter)
    {
        Console.WriteLine($"Dumping method {typeFilter}::{methodFilter} from {exePath}...");
        using var asm = AssemblyDefinition.ReadAssembly(exePath);
        Console.WriteLine("Assembly References:");
        foreach (var r in asm.MainModule.AssemblyReferences)
        {
            Console.WriteLine($"  - {r.FullName}");
        }
        foreach (var type in asm.MainModule.Types.Concat(asm.MainModule.Types.SelectMany(t => t.NestedTypes)))
        {
            if (!type.FullName.Contains(typeFilter)) continue;
            foreach (var m in type.Methods)
            {
                if (!m.Name.Contains(methodFilter)) continue;
                Console.WriteLine($"\n--- Method: {type.FullName}::{m.Name} ---");
                if (m.Body == null) { Console.WriteLine("  <no body>"); continue; }
                foreach (var inst in m.Body.Instructions)
                {
                    Console.WriteLine($"  IL_{inst.Offset:X4}: {inst.OpCode} {inst.Operand}");
                }
            }
        }
    }

    static void InspectLauncher(string exePath)
    {
        Console.WriteLine($"Inspecting {exePath}...");
        using var asm = AssemblyDefinition.ReadAssembly(exePath);
        foreach (var type in asm.MainModule.Types)
        {
            InspectType(type);
        }
    }

    static void InspectType(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes)
        {
            InspectType(nested);
        }

        foreach (var method in type.Methods)
        {
            if (method.Body == null) continue;
            foreach (var inst in method.Body.Instructions)
            {
                if (inst.OpCode == OpCodes.Ldstr)
                {
                    string str = (string)inst.Operand;
                    if (str.Contains("APP_") || str.Contains("antivirus") || str.Contains("blocking") || str.Contains("connection"))
                    {
                        Console.WriteLine($"Method {type.FullName}::{method.Name} uses string: \"{str}\"");
                    }
                }
                else if (inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt)
                {
                    var mRef = inst.Operand as MethodReference;
                    if (mRef != null && (mRef.Name.Contains("CheckLauncherVersion") || mRef.Name.Contains("GetLauncherApi") || mRef.Name.Contains("DownloadDependencies")))
                    {
                        Console.WriteLine($"Method {type.FullName}::{method.Name} calls {mRef.DeclaringType.Name}::{mRef.Name}");
                    }
                }
            }
        }
    }

    static void PatchSettingsApiSelfHealing(TypeDefinition settingsType, ModuleDefinition module)
    {
        var apiProp = settingsType.Properties.FirstOrDefault(p => p.Name == "Api");
        var creditsProp = settingsType.Properties.FirstOrDefault(p => p.Name == "CreditsApi");

        var pathsType = module.GetType("Subnautica.API.Features.Paths");
        var getLauncherApiPath = module.ImportReference(pathsType.Methods.First(m => m.Name == "GetLauncherApiFilePath" && !m.HasParameters));
        var getLauncherCreditsPath = module.ImportReference(pathsType.Methods.First(m => m.Name == "GetLauncherCreditsApiFilePath" && !m.HasParameters));

        var fileType = CoreType(module, "System.IO", "File");
        var fileExists = new MethodReference("Exists", module.TypeSystem.Boolean, fileType);
        fileExists.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var fileReadAllText = new MethodReference("ReadAllText", module.TypeSystem.String, fileType);
        fileReadAllText.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var jsonRef = module.AssemblyReferences.First(a => a.Name == "Newtonsoft.Json");
        var jsonConvertType = new TypeReference("Newtonsoft.Json", "JsonConvert", module, jsonRef);
        var desMethod = new MethodReference("DeserializeObject", module.TypeSystem.Object, jsonConvertType);
        var genParam = new GenericParameter("T", desMethod);
        desMethod.GenericParameters.Add(genParam);
        desMethod.ReturnType = genParam;
        desMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        // 1. Patch get_Api
        if (apiProp != null && apiProp.GetMethod != null)
        {
            var getApi = apiProp.GetMethod;
            var backingField = settingsType.Fields.FirstOrDefault(f => f.Name.Contains("<Api>k__BackingField") || f.Name == "api" || f.FieldType.FullName.Contains("ApiDataFormat"));
            if (backingField != null)
            {
                var apiDataFormatType = module.GetType("Subnautica.API.Features.Helper.ApiDataFormat");
                var initMethod = module.ImportReference(apiDataFormatType.Methods.First(m => m.Name == "Initialize" && m.Parameters.Count == 1));

                var genDeserializeApi = new GenericInstanceMethod(desMethod);
                genDeserializeApi.GenericArguments.Add(module.ImportReference(backingField.FieldType));

                getApi.Body.Instructions.Clear();
                getApi.Body.ExceptionHandlers.Clear();
                getApi.Body.Variables.Clear();

                var jsonVar = new VariableDefinition(module.TypeSystem.String);
                var apiVar = new VariableDefinition(module.ImportReference(backingField.FieldType));
                getApi.Body.Variables.Add(jsonVar);
                getApi.Body.Variables.Add(apiVar);

                var il = getApi.Body.GetILProcessor();
                var retLabel = il.Create(OpCodes.Ldsfld, backingField);
                var tryFailLabel = il.Create(OpCodes.Nop);

                // if (<Api> != null) goto retLabel;
                il.Append(il.Create(OpCodes.Ldsfld, backingField));
                il.Append(il.Create(OpCodes.Brtrue, retLabel));

                // try:
                var tryStart = il.Create(OpCodes.Call, getLauncherApiPath);
                il.Append(tryStart);
                il.Append(il.Create(OpCodes.Call, fileExists));
                il.Append(il.Create(OpCodes.Brfalse, tryFailLabel));

                // json = File.ReadAllText(Paths.GetLauncherApiFilePath());
                il.Append(il.Create(OpCodes.Call, getLauncherApiPath));
                il.Append(il.Create(OpCodes.Call, fileReadAllText));
                il.Append(il.Create(OpCodes.Stloc, jsonVar));

                // api = JsonConvert.DeserializeObject<ApiDataFormat>(json);
                il.Append(il.Create(OpCodes.Ldloc, jsonVar));
                il.Append(il.Create(OpCodes.Call, genDeserializeApi));
                il.Append(il.Create(OpCodes.Stloc, apiVar));

                // if (api == null) goto tryFailLabel;
                il.Append(il.Create(OpCodes.Ldloc, apiVar));
                il.Append(il.Create(OpCodes.Brfalse, tryFailLabel));

                // <Api> = api;
                il.Append(il.Create(OpCodes.Ldloc, apiVar));
                il.Append(il.Create(OpCodes.Stsfld, backingField));

                // api.Initialize(json);
                il.Append(il.Create(OpCodes.Ldloc, apiVar));
                il.Append(il.Create(OpCodes.Ldloc, jsonVar));
                il.Append(il.Create(OpCodes.Callvirt, initMethod));

                var leaveTry = il.Create(OpCodes.Leave, retLabel);
                il.Append(leaveTry);

                il.Append(tryFailLabel);
                var leaveTryFail = il.Create(OpCodes.Leave, retLabel);
                il.Append(leaveTryFail);

                var catchHandler = il.Create(OpCodes.Pop);
                il.Append(catchHandler);
                var leaveCatch = il.Create(OpCodes.Leave, retLabel);
                il.Append(leaveCatch);

                il.Append(retLabel);
                il.Append(il.Create(OpCodes.Ret));

                var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
                {
                    TryStart = tryStart,
                    TryEnd = catchHandler,
                    HandlerStart = catchHandler,
                    HandlerEnd = retLabel,
                    CatchType = CoreType(module, "System", "Exception")
                };
                getApi.Body.ExceptionHandlers.Add(handler);
            }
        }

        // 2. Patch get_CreditsApi
        if (creditsProp != null && creditsProp.GetMethod != null)
        {
            var getCredits = creditsProp.GetMethod;
            var backingField = settingsType.Fields.FirstOrDefault(f => f.Name.Contains("<CreditsApi>k__BackingField") || f.Name == "creditsApi" || f.FieldType.FullName.Contains("ApiCreditsDataFormat"));
            if (backingField != null)
            {
                var genDeserializeCredits = new GenericInstanceMethod(desMethod);
                genDeserializeCredits.GenericArguments.Add(module.ImportReference(backingField.FieldType));

                getCredits.Body.Instructions.Clear();
                getCredits.Body.ExceptionHandlers.Clear();
                getCredits.Body.Variables.Clear();

                var creditsVar = new VariableDefinition(module.ImportReference(backingField.FieldType));
                getCredits.Body.Variables.Add(creditsVar);

                var il = getCredits.Body.GetILProcessor();
                var retLabel = il.Create(OpCodes.Ldsfld, backingField);
                var tryFailLabel = il.Create(OpCodes.Nop);

                // if (<CreditsApi> != null) goto retLabel;
                il.Append(il.Create(OpCodes.Ldsfld, backingField));
                il.Append(il.Create(OpCodes.Brtrue, retLabel));

                // try:
                var tryStart = il.Create(OpCodes.Call, getLauncherCreditsPath);
                il.Append(tryStart);
                il.Append(il.Create(OpCodes.Call, fileExists));
                il.Append(il.Create(OpCodes.Brfalse, tryFailLabel));

                // credits = JsonConvert.DeserializeObject<ApiCreditsDataFormat>(File.ReadAllText(Paths.GetLauncherCreditsApiFilePath()));
                il.Append(il.Create(OpCodes.Call, getLauncherCreditsPath));
                il.Append(il.Create(OpCodes.Call, fileReadAllText));
                il.Append(il.Create(OpCodes.Call, genDeserializeCredits));
                il.Append(il.Create(OpCodes.Stloc, creditsVar));

                // <CreditsApi> = credits;
                il.Append(il.Create(OpCodes.Ldloc, creditsVar));
                il.Append(il.Create(OpCodes.Stsfld, backingField));

                var leaveTry = il.Create(OpCodes.Leave, retLabel);
                il.Append(leaveTry);

                il.Append(tryFailLabel);
                var leaveTryFail = il.Create(OpCodes.Leave, retLabel);
                il.Append(leaveTryFail);

                var catchHandler = il.Create(OpCodes.Pop);
                il.Append(catchHandler);
                var leaveCatch = il.Create(OpCodes.Leave, retLabel);
                il.Append(leaveCatch);

                il.Append(retLabel);
                il.Append(il.Create(OpCodes.Ret));

                var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
                {
                    TryStart = tryStart,
                    TryEnd = catchHandler,
                    HandlerStart = catchHandler,
                    HandlerEnd = retLabel,
                    CatchType = CoreType(module, "System", "Exception")
                };
                getCredits.Body.ExceptionHandlers.Add(handler);
            }
        }
    }

    static void PatchGZipUncompress(TypeDefinition toolsType, ModuleDefinition module)
    {
        var method = toolsType.Methods.FirstOrDefault(m => m.Name == "GZipUncompress");
        if (method == null) return;

        var base64Decode = toolsType.Methods.First(m => m.Name == "Base64ByteDecode");
        var pathsType = module.GetType("Subnautica.API.Features.Paths");
        var getLauncherApiPath = module.ImportReference(pathsType.Methods.First(m => m.Name == "GetLauncherApiFilePath" && !m.HasParameters));

        var stringType = module.TypeSystem.String;
        var byteArrayType = new ArrayType(module.TypeSystem.Byte);
        var sysAsm = GetSystemAssemblyRef(module);

        var memStreamType = CoreType(module, "System.IO", "MemoryStream");
        var memStreamCtorFull = new MethodReference(".ctor", module.TypeSystem.Void, memStreamType) { HasThis = true };
        memStreamCtorFull.Parameters.Add(new ParameterDefinition(byteArrayType));
        memStreamCtorFull.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        memStreamCtorFull.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

        var memStreamCtorSimple = new MethodReference(".ctor", module.TypeSystem.Void, memStreamType) { HasThis = true };
        memStreamCtorSimple.Parameters.Add(new ParameterDefinition(byteArrayType));

        var memStreamCtorDefault = new MethodReference(".ctor", module.TypeSystem.Void, memStreamType) { HasThis = true };
        var memStreamToArray = new MethodReference("ToArray", byteArrayType, memStreamType) { HasThis = true };

        var streamType = CoreType(module, "System.IO", "Stream");
        var streamCopyTo = new MethodReference("CopyTo", module.TypeSystem.Void, streamType) { HasThis = true };
        streamCopyTo.Parameters.Add(new ParameterDefinition(streamType));

        var disposableType = CoreType(module, "System", "IDisposable");
        var disposeMethod = new MethodReference("Dispose", module.TypeSystem.Void, disposableType) { HasThis = true };

        var compressionModeType = new TypeReference("System.IO.Compression", "CompressionMode", module, sysAsm) { IsValueType = true };
        var deflateStreamType = new TypeReference("System.IO.Compression", "DeflateStream", module, sysAsm);
        var deflateCtor = new MethodReference(".ctor", module.TypeSystem.Void, deflateStreamType) { HasThis = true };
        deflateCtor.Parameters.Add(new ParameterDefinition(streamType));
        deflateCtor.Parameters.Add(new ParameterDefinition(compressionModeType));

        var gzipStreamType = new TypeReference("System.IO.Compression", "GZipStream", module, sysAsm);
        var gzipCtor = new MethodReference(".ctor", module.TypeSystem.Void, gzipStreamType) { HasThis = true };
        gzipCtor.Parameters.Add(new ParameterDefinition(streamType));
        gzipCtor.Parameters.Add(new ParameterDefinition(compressionModeType));

        var encodingType = CoreType(module, "System.Text", "Encoding");
        var getUtf8 = new MethodReference("get_UTF8", encodingType, encodingType);
        var getString = new MethodReference("GetString", stringType, encodingType) { HasThis = true };
        getString.Parameters.Add(new ParameterDefinition(byteArrayType));

        var fileType = CoreType(module, "System.IO", "File");
        var fileExists = new MethodReference("Exists", module.TypeSystem.Boolean, fileType);
        fileExists.Parameters.Add(new ParameterDefinition(stringType));
        var fileReadAllText = new MethodReference("ReadAllText", stringType, fileType);
        fileReadAllText.Parameters.Add(new ParameterDefinition(stringType));

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();

        var rawVar = new VariableDefinition(byteArrayType);
        var inStreamVar = new VariableDefinition(streamType);
        var decompressVar = new VariableDefinition(streamType);
        var outMsVar = new VariableDefinition(memStreamType);
        var resultVar = new VariableDefinition(stringType);

        method.Body.Variables.Add(rawVar);
        method.Body.Variables.Add(inStreamVar);
        method.Body.Variables.Add(decompressVar);
        method.Body.Variables.Add(outMsVar);
        method.Body.Variables.Add(resultVar);

        var il = method.Body.GetILProcessor();
        var tryFailLabel = il.Create(OpCodes.Nop);
        var fallbackLabel = il.Create(OpCodes.Nop);
        var retLabel = il.Create(OpCodes.Ldloc, resultVar);

        // try start:
        // if (content == null) goto tryFailLabel;
        var tryStart = il.Create(OpCodes.Ldarg_0);
        il.Append(tryStart);
        il.Append(il.Create(OpCodes.Brfalse, tryFailLabel));

        // raw = Tools.Base64ByteDecode(content);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, base64Decode));
        il.Append(il.Create(OpCodes.Stloc, rawVar));

        // if (raw == null || raw.Length < 3) goto tryFailLabel;
        il.Append(il.Create(OpCodes.Ldloc, rawVar));
        il.Append(il.Create(OpCodes.Brfalse, tryFailLabel));
        il.Append(il.Create(OpCodes.Ldloc, rawVar));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Ldc_I4_3));
        il.Append(il.Create(OpCodes.Blt, tryFailLabel));

        // if (raw[0] == 0x78) { isZlib }
        var gzipBranch = il.Create(OpCodes.Nop);
        var decompressCommon = il.Create(OpCodes.Nop);

        il.Append(il.Create(OpCodes.Ldloc, rawVar));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ldelem_U1));
        il.Append(il.Create(OpCodes.Ldc_I4, 0x78));
        il.Append(il.Create(OpCodes.Bne_Un, gzipBranch));

        // inStream = new MemoryStream(raw, 2, raw.Length - 2);
        il.Append(il.Create(OpCodes.Ldloc, rawVar));
        il.Append(il.Create(OpCodes.Ldc_I4_2));
        il.Append(il.Create(OpCodes.Ldloc, rawVar));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Ldc_I4_2));
        il.Append(il.Create(OpCodes.Sub));
        il.Append(il.Create(OpCodes.Newobj, memStreamCtorFull));
        il.Append(il.Create(OpCodes.Stloc, inStreamVar));

        // decompress = new DeflateStream(inStream, CompressionMode.Decompress);
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Ldc_I4_0)); // Decompress = 0
        il.Append(il.Create(OpCodes.Newobj, deflateCtor));
        il.Append(il.Create(OpCodes.Stloc, decompressVar));
        il.Append(il.Create(OpCodes.Br, decompressCommon));

        // gzipBranch:
        il.Append(gzipBranch);
        il.Append(il.Create(OpCodes.Ldloc, rawVar));
        il.Append(il.Create(OpCodes.Newobj, memStreamCtorSimple));
        il.Append(il.Create(OpCodes.Stloc, inStreamVar));

        // decompress = new GZipStream(inStream, CompressionMode.Decompress);
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Ldc_I4_0)); // Decompress = 0
        il.Append(il.Create(OpCodes.Newobj, gzipCtor));
        il.Append(il.Create(OpCodes.Stloc, decompressVar));

        // decompressCommon:
        il.Append(decompressCommon);
        il.Append(il.Create(OpCodes.Newobj, memStreamCtorDefault));
        il.Append(il.Create(OpCodes.Stloc, outMsVar));

        // decompress.CopyTo(outMs);
        il.Append(il.Create(OpCodes.Ldloc, decompressVar));
        il.Append(il.Create(OpCodes.Ldloc, outMsVar));
        il.Append(il.Create(OpCodes.Callvirt, streamCopyTo));

        // result = Encoding.UTF8.GetString(outMs.ToArray());
        il.Append(il.Create(OpCodes.Call, getUtf8));
        il.Append(il.Create(OpCodes.Ldloc, outMsVar));
        il.Append(il.Create(OpCodes.Callvirt, memStreamToArray));
        il.Append(il.Create(OpCodes.Callvirt, getString));
        il.Append(il.Create(OpCodes.Stloc, resultVar));

        // outMs.Dispose(); decompress.Dispose(); inStream.Dispose();
        il.Append(il.Create(OpCodes.Ldloc, outMsVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeMethod));
        il.Append(il.Create(OpCodes.Ldloc, decompressVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeMethod));
        il.Append(il.Create(OpCodes.Ldloc, inStreamVar));
        il.Append(il.Create(OpCodes.Callvirt, disposeMethod));

        var leaveTry = il.Create(OpCodes.Leave, retLabel);
        il.Append(leaveTry);

        il.Append(tryFailLabel);
        var leaveTryFail = il.Create(OpCodes.Leave, fallbackLabel);
        il.Append(leaveTryFail);

        // Catch block:
        var catchHandler = il.Create(OpCodes.Pop);
        il.Append(catchHandler);
        var leaveCatch = il.Create(OpCodes.Leave, fallbackLabel);
        il.Append(leaveCatch);

        // fallbackLabel (normal code after try/catch):
        il.Append(fallbackLabel);
        var fallbackRet = il.Create(OpCodes.Ldstr, "{}");
        il.Append(il.Create(OpCodes.Call, getLauncherApiPath));
        il.Append(il.Create(OpCodes.Call, fileExists));
        il.Append(il.Create(OpCodes.Brfalse, fallbackRet));

        il.Append(il.Create(OpCodes.Call, getLauncherApiPath));
        il.Append(il.Create(OpCodes.Call, fileReadAllText));
        il.Append(il.Create(OpCodes.Stloc, resultVar));
        il.Append(il.Create(OpCodes.Br, retLabel));

        il.Append(fallbackRet);
        il.Append(il.Create(OpCodes.Stloc, resultVar));
        il.Append(il.Create(OpCodes.Br, retLabel));

        il.Append(retLabel);
        il.Append(il.Create(OpCodes.Ret));

        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchHandler,
            HandlerStart = catchHandler,
            HandlerEnd = fallbackLabel,
            CatchType = CoreType(module, "System", "Exception")
        };
        method.Body.ExceptionHandlers.Add(handler);
    }

    static void ValidateExceptionHandlers(MethodDefinition method)
    {
        var instructions = method.Body.Instructions;
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            int tryStartIndex = instructions.IndexOf(handler.TryStart);
            int tryEndIndex = instructions.IndexOf(handler.TryEnd);
            int handlerStartIndex = instructions.IndexOf(handler.HandlerStart);
            int handlerEndIndex = handler.HandlerEnd != null ? instructions.IndexOf(handler.HandlerEnd) : instructions.Count;

            if (tryStartIndex < 0 || tryEndIndex < 0 || handlerStartIndex < 0)
            {
                throw new InvalidOperationException($"Method {method.FullName}: invalid exception handler boundaries.");
            }

            // Check try block:
            for (int i = tryStartIndex; i < tryEndIndex; i++)
            {
                var inst = instructions[i];
                if (inst.Operand is Instruction target)
                {
                    int targetIndex = instructions.IndexOf(target);
                    // If branching outside try block, it MUST be Leave / Leave_S
                    if (targetIndex < tryStartIndex || targetIndex >= tryEndIndex)
                    {
                        if (inst.OpCode != OpCodes.Leave && inst.OpCode != OpCodes.Leave_S)
                        {
                            throw new InvalidOperationException($"Method {method.FullName}: IL instruction at {inst.Offset:X4} ({inst.OpCode}) branches outside try block without Leave!");
                        }
                    }
                }
            }

            // Check handler block:
            for (int i = handlerStartIndex; i < handlerEndIndex; i++)
            {
                var inst = instructions[i];
                if (inst.Operand is Instruction target)
                {
                    int targetIndex = instructions.IndexOf(target);
                    // If branching outside handler block, it MUST be Leave / Leave_S
                    if (targetIndex < handlerStartIndex || targetIndex >= handlerEndIndex)
                    {
                        if (inst.OpCode != OpCodes.Leave && inst.OpCode != OpCodes.Leave_S)
                        {
                            throw new InvalidOperationException($"Method {method.FullName}: IL instruction at {inst.Offset:X4} ({inst.OpCode}) branches outside handler block without Leave!");
                        }
                    }
                }
            }
        }
    }
}

