# Phase 1.B.2 — Il2CppInterop "No handler" Investigation

**Author:** interop-engineer (mac-spike team)
**Date:** 2026-05-14
**Result:** ✅ **PASS** — root cause identified and fixed, but a new downstream failure surfaced (`Pass16ScanMethodRefs` xref-scan crash).

---

## TL;DR

The "No handler" exception was **NOT** caused by Il2CppInterop missing version-handler classes. Build 755's bundled Il2CppInterop `1.5.1-ci.829` already contains a complete handler set for Unity 2022.3 (e.g. `NativeClassStructHandler_29_1` is `[ApplicableToUnityVersionsSince("2022.2.0")]`). The dispatch fails because **`UnityInfo.Version` evaluates to `0.0.0` at runtime**, since BepInEx's `Paths.GameDataPath` resolver looks for Unity manager files at the wrong directory on macOS.

**Fix:** symlink layer in the staging root that maps the Linux/Windows directory conventions (`<game>/MTGA_Data/`, `<game>/GameAssembly.dylib`) onto MTGA's macOS bundle layout (`MTGA.app/Contents/Resources/Data/`, `MTGA.app/Contents/Frameworks/GameAssembly.dylib`). After applying this fix, BepInEx reports `Running under Unity 2022.3.62f2`, the entire `UnityVersionHandler` cctor succeeds, IL2CPP runtime native lib loads, Cpp2IL succeeds, dummy assemblies are generated, and Il2CppInteropGen runs through 15 of its 16+ passes before crashing at xref-scan.

The Il2CppInterop NuGet package was NOT upgraded — the existing ci.829 binaries are intact. (I downloaded ci.832/833/843/845 candidates and confirmed via `strings` that their `Native*Handler_*` lists are byte-identical to ci.829, so a drop-in upgrade would have been a no-op for this specific bug.)

---

## Why "No handler" really fired

The static initializer chain is:

```
UnityVersionHandler..cctor()
  └─ RecalculateHandlers()                                 // line 87
      └─ var unityVersion = Il2CppInteropRuntime.Instance.UnityVersion;
      └─ for each interface in InterfacesOfInterest:
            for each (Version, Handler) in VersionedHandlers[type]:
                if (valueTuple.Version > unityVersion) continue;
                Handlers[type] = valueTuple.Handler;
                break;
      └─ GetHandler<INativeAssemblyStructHandler>()        // line 124 — throws "No handler"
```

`Il2CppInteropRuntime.Instance.UnityVersion` is set by `BepInEx.Unity.IL2CPP.Il2CppInteropManager.Initialize()` (line ~244) from `UnityInfo.Version`. `UnityInfo.DetermineVersion()` walks three `ManagerLookup` entries (`globalgamemanagers`, `data.unity3d`, `mainData`) under `UnityInfo.GameDataPath`, then falls back to a Windows `FileVersionInfo` parse, then to a hand-rolled `2017.0.0` guess if `<GameDataPath>/Managed/UnityEngine.CoreModule.dll` exists. If all three fail, `Version = default` (== `0.0.0.0`).

`UnityInfo.GameDataPath` is wired from `Paths.GameDataPath` in `BepInEx.Core/Paths.cs`:

```csharp
GameRootPath = PlatformHelper.Is(Platform.MacOS)
    ? Utility.ParentDirectory(executablePath, 4)
    : Path.GetDirectoryName(executablePath);

GameDataPath = Path.Combine(GameRootPath, $"{ProcessName}_Data");
if (!Directory.Exists(GameDataPath))
    GameDataPath = Path.Combine(GameRootPath, "Data");
```

For MTGA the executable is `MTGA.app/Contents/MacOS/MTGA`; four parents up is the directory *containing* `MTGA.app`. BepInEx then looks for `MTGA_Data/` next to the bundle — but on Mac the user's MTGA install does have an `MTGA_Data/` folder there: it's an *asset-download cache*, completely unrelated to Unity's player data, and it contains only `Downloads/` and `Logs/`. The actual Unity player data (`globalgamemanagers`, `app.info`, `boot.config`, `il2cpp_data/`, `Managed/`, `Resources/`, etc.) is at `MTGA.app/Contents/Resources/Data/` — standard macOS Unity convention, **never** searched by BepInEx.

So every `ManagerLookup.TryLookup` returns false, the Windows-only branch is skipped, the `Managed/UnityEngine.CoreModule.dll` heuristic finds nothing, and the property is set to `default(UnityVersion)` = `0.0.0`. Once `Il2CppInteropManager.Initialize()` constructs `new Version(unityVersion.Major, unityVersion.Minor, unityVersion.Build)` from that, every handler's `StartVersion` (e.g. `Version.Parse("2018.4.34")`) is greater than `0.0.0`, the dispatch loop sets nothing, and `GetHandler<T>()` throws `"No handler"`.

There is also a latent bug at `UnityInfo.cs:94`: after the `Managed/UnityEngine.CoreModule.dll` heuristic sets `Version = new UnityVersion(2017, 0, 0, ...)`, control falls through to `Version = default;` at line 96 (missing `return;`). Even if the heuristic had worked, it would still produce `0.0.0`. Reported upstream-relevant; not exploitable for our fix.

---

## The Fix (symlinks-only, no DLL changes)

Applied at `mac-spike/staging/`:

```bash
# 1. Move the asset cache out of the way (keep the 13 GB of downloads intact)
mv MTGA_Data MTGA_Data.downloads-cache

# 2. Recreate MTGA_Data as a hybrid dir that gives BepInEx what it wants
#    while still letting MTGA write its asset cache to the original place
mkdir MTGA_Data
ln -s ../MTGA.app/Contents/Resources/Data/globalgamemanagers MTGA_Data/globalgamemanagers
ln -s ../MTGA.app/Contents/Resources/Data/il2cpp_data       MTGA_Data/il2cpp_data
ln -s ../MTGA_Data.downloads-cache/Downloads                MTGA_Data/Downloads
ln -s ../MTGA_Data.downloads-cache/Logs                     MTGA_Data/Logs

# 3. Expose GameAssembly.dylib where BepInEx's DllImport resolver looks for it
ln -s MTGA.app/Contents/Frameworks/GameAssembly.dylib GameAssembly.dylib
```

After this, BepInEx's preloader:
1. Reads `MTGA_Data/globalgamemanagers` → parses Unity version string at offset 0x14 → `UnityVersion = 2022.3.62f2`. ✅
2. `UnityVersionHandler` cctor dispatches all 11 interface handlers cleanly. ✅
3. `Il2CppInterop.Runtime.IL2CPP.cctor` calls `il2cpp_domain_get` via `DllImport("GameAssembly")` → resolved through `staging/GameAssembly.dylib` symlink → succeeds. ✅
4. `Cpp2IL` finds `MTGA_Data/il2cpp_data/Metadata/global-metadata.dat` (also via symlink) → loads → generates dummy assemblies. ✅
5. `Il2CppInteropGen` runs Pass00–Pass15 (typedef filling, generic constraints, member creation, method xref scan). ✅
6. **`Pass16ScanMethodRefs` crashes** in `XrefScannerLowLevel.ExtractTargetAddress` with `ArgumentOutOfRangeException` — but this is a different, downstream bug. See "Next failure" below.

The Il2CppInterop DLLs in `BepInEx/core/` are still the unmodified `1.5.1-ci.829` from build 755 — **no DLL swap was applied**.

### Why not just upgrade Il2CppInterop?

I downloaded the next four ci builds (832, 833, 843, 845) from `https://nuget.bepinex.dev/v3/package/il2cppinterop.runtime/...`, extracted the `net6.0` DLLs into `mac-spike/interop-test/ci.845/`, and diffed the `Native*Handler_*` symbol set against ci.829 with `strings | sort -u | diff`. The symbol sets are **identical** — same 11 interfaces, same per-interface handler set (max class metadata version 29.2, max image 27.0, etc.). No newer build adds Unity-version coverage. Upgrading the package wouldn't change anything for this specific failure mode.

Baseline DLLs are at `mac-spike/interop-test/baseline/`. The ci.845 candidates are at `mac-spike/interop-test/ci.845/` for future reference, but were not deployed.

---

## Next failure (NOT solved by this task)

After the symlink fix, the BepInEx LogOutput shows the preloader getting all the way through to:

```
[Info   :Il2CppInteropGen] Filling typedefs...           Done in 00:00:00.49
[Info   :Il2CppInteropGen] Filling generic constraints... Done in 00:00:00.02
[Info   :Il2CppInteropGen] Creating members...           Done in 00:00:02.32
[Info   :Il2CppInteropGen] Scanning method cross-references... Done in 00:00:01.44
[Error  :InteropManager] Failed to generate Il2Cpp interop assemblies:
    System.AggregateException: ... (Specified argument was out of the range of valid values.)
     ---> System.ArgumentOutOfRangeException
       at Il2CppInterop.Common.XrefScans.XrefScannerLowLevel.ExtractTargetAddress(Instruction& instruction)
            in /Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs:line 97
       at Il2CppInterop.Common.XrefScans.XrefScannerLowLevel.JumpTargetsImpl(...)
       at Il2CppInterop.Generator.Utils.XrefScanMetadataGenerationUtil.FindMetadataInitForMethod
            (MethodRewriteContext method, Int64 gameAssemblyBase) at line 50
       at Il2CppInterop.Generator.Passes.Pass16ScanMethodRefs.<>c__DisplayClass2_0.<DoPass>b__2(...)
```

Log archived at: `mac-spike/interop-test/LogOutput-symlink-fix.log`.

This is consistent with the §3 research note that v31 / LLVM-optimized binaries break Il2CppInterop's x86_64 byte-pattern + Iced-decoder scanners. The crash is in `Pass16ScanMethodRefs.FindMetadataInitForMethod` — different code path from the `InjectorHelpers.FindClassInit` `0x80131506` case the Ghidra signature (issue #1122) was written for. The two are sibling bugs in the same family.

BepInEx itself still survives — the AggregateException is caught inside `Il2CppInteropManager.GenerateInteropAssemblies`, `[Message:   BepInEx] Chainloader initialized` is logged, and no `preloader_*.log` is emitted (so the silent-exception trap in `DoorstopEntrypoint.Start` doesn't fire). But `BepInEx/interop/` stays empty — there are no `Il2Cpp*.dll` files for a Phase 1.C plugin to reference.

### Likely fix path for the new failure

PR #262 (`Il2CppInterop` hazre, "harden hook and signature resolution for optimized IL2CPP builds", May 2026, **open**) explicitly addresses xref-scan failures on optimized v31 binaries. That PR is unmerged so it won't appear in any published `ci-*` build until merged. Two options for the next investigator:

1. **Wait/lobby for PR #262 merge**, then re-test with the resulting ci build.
2. **Cherry-pick PR #262 onto a local Il2CppInterop fork**, rebuild, drop in the produced `Il2CppInterop.Common.dll` / `Il2CppInterop.Runtime.dll` over `BepInEx/core/`.

Either way: this is now blocking Phase 1.C and is **a different scope decision** that should go to the user before someone spends a weekend forking. The original "fork Il2CppInterop" mention in `MAC_SUPPORT_PLAN.md` Phase 1.B.2 was tied to the missing-handler theory, which turned out to be wrong; the real reason to fork is PR #262.

---

## Files touched / created

- `mac-spike/staging/MTGA_Data/` — new hybrid directory (symlinks to manager files + asset cache)
- `mac-spike/staging/MTGA_Data.downloads-cache/` — renamed previous `MTGA_Data` (preserves 13 GB cache)
- `mac-spike/staging/GameAssembly.dylib` — symlink to `MTGA.app/Contents/Frameworks/GameAssembly.dylib`
- `mac-spike/staging/BepInEx/LogOutput.log` — new (proves Unity 2022.3.62f2 is now detected)
- `mac-spike/staging/BepInEx/unity-libs/` — populated (Unity reference assemblies downloaded for 2022.3.62)
- `mac-spike/interop-test/baseline/` — backup of the build-755 Il2CppInterop DLLs (unmodified)
- `mac-spike/interop-test/ci.845/` — candidate ci.845 DLLs (downloaded but NOT deployed; identical handler set to ci.829)
- `mac-spike/interop-test/LogOutput-symlink-fix.log` — archived copy of the post-fix preloader log

The Il2CppInterop DLLs in `BepInEx/core/` are unchanged.

---

## Recommendations for next steps

1. **Update `MAC_SUPPORT_PLAN.md` Phase 1.B.2 section** — the failure cause and the fix were both different from what was hypothesized. The new blocker is `Pass16ScanMethodRefs`, addressed by **Il2CppInterop PR #262**, not by a new version handler.
2. **Bake the symlink setup into the bootstrap workflow.** Add to `mac-spike/SETUP.md` (or whatever the setup doc becomes) a step: "after extracting BepInEx next to MTGA.app, create the symlink shim." This is a permanent macOS-on-BepInEx-6 requirement until BepInEx adds a `Paths.MacOSBundleDataPath` fallback.
3. **Open or comment on** [BepInEx/BepInEx issue #1096](https://github.com/BepInEx/BepInEx/issues/1096) ("BepInEx 6 fails to initialize on macOS Intel (no logs, no config generated)") — that issue's symptom (Doorstop seems not to inject; no logs generated) is exactly what we hit before the symlink fix, just confusingly observable because the silent-exception log writes outside the Bundle Data location. The fix here may resolve their issue.
4. **Phase 1.C should NOT be unblocked yet.** Without `BepInEx/interop/Il2Cpp*.dll` files we cannot reference game types in a plugin. The next task is either: (a) get PR #262 merged or cherry-pick it, or (b) demonstrate that PR #262 *itself* doesn't fully solve Pass16 on MTGA's specific binary, in which case Phase 1 should pause for user direction.
