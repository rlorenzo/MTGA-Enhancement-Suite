using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MtgaMacHelloWorld;

[BepInPlugin("com.mtgaes.mac.helloworld", "MTGA ES Mac Hello World", "0.1.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo("=== MAC SMOKE TEST: Load() entered ===");

        // 1. Prove the generated interop assemblies resolve a game type at runtime.
        try
        {
            var greType = Il2CppType.Of<Application>();
            Log.LogInfo($"SMOKE 1 OK: resolved Il2Cpp type {greType.FullName}");
        }
        catch (System.Exception e)
        {
            Log.LogError($"SMOKE 1 FAIL (interop type resolution): {e}");
        }

        // 2. Prove a HarmonyX native detour installs against GameAssembly.dylib.
        //    This is the operation that Il2CppInterop issue #206 (Process.Modules on
        //    Darwin) would break. If it throws, hooking is the blocker.
        Harmony harmony = null;
        try
        {
            harmony = new Harmony("com.mtgaes.mac.helloworld");
            harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("SMOKE 2 OK: Harmony.PatchAll installed detour(s)");
        }
        catch (System.Exception e)
        {
            Log.LogError($"SMOKE 2 FAIL (Harmony detour install): {e}");
        }

        // 3. Prove the detour actually fires by invoking the patched method.
        try
        {
            var name = Application.productName;
            Log.LogInfo($"SMOKE 3 OK: Application.productName returned '{name}' (prefix should have logged above)");
        }
        catch (System.Exception e)
        {
            Log.LogError($"SMOKE 3 FAIL (invoke patched method): {e}");
        }

        Log.LogInfo("=== MAC SMOKE TEST: Load() complete ===");
    }
}

[HarmonyPatch(typeof(Application), "get_productName")]
public static class ProductNamePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        Plugin.Log.LogInfo(">>> SMOKE HOOK FIRED: Application.get_productName prefix executed <<<");
    }
}
