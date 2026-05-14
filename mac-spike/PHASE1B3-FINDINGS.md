# Phase 1.B.3 — Cherry-pick Il2CppInterop PR #262 + retest

**Author:** interop-builder (mac-spike team)
**Date:** 2026-05-14
**Result:** ❌ **FAIL** — PR #262 does not resolve the `Pass16ScanMethodRefs` crash. Phase 1 has reached its NO-GO escape hatch.

---

## TL;DR

Built `Il2CppInterop` from PR #262 (commit `2e82b6c`, branch `fix/il2cppinterop-hook-resolution`), dropped the rebuilt DLLs in over `BepInEx/core/`, and relaunched MTGA. Same exact `ArgumentOutOfRangeException` at `XrefScannerLowLevel.ExtractTargetAddress` (`XrefScannerLowLevel.cs:97`) called from `XrefScanMetadataGenerationUtil.FindMetadataInitForMethod` (line 50) inside `Pass16ScanMethodRefs`. Byte-for-byte identical stack trace to the pre-PR262 baseline. `BepInEx/interop/` stays empty.

**Why it didn't work** is visible in the PR diff: PR #262 only touches `Il2CppInterop.Runtime/Injection/` — specifically `InjectorHelpers.cs` (adds a bounds-check on `s_ClassInitSignatures` `FirstOrDefault` results) and `MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.cs` (falls back to `imageGetType` when the xref is mis-aligned, instead of recursing). Neither change touches `Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs` where the crash actually lives. The hazre PR addresses *runtime* hook resolution; our crash is in *generator* xref scanning. Sibling bugs, distinct fixes.

The DLLs in `BepInEx/core/` have been **restored to the baseline ci.829 set** (MD5-verified — same hashes as `mac-spike/interop-test/pre-pr262-baseline/`). Build artifacts and a clean clone of PR #262 are preserved at `mac-spike/il2cppinterop-fork/` for future reference.

---

## What was done

1. **Cloned** `BepInEx/Il2CppInterop` (depth 50) to `mac-spike/il2cppinterop-fork/`.
2. **Fetched** PR #262 as `pr-262` (head SHA `2e82b6c40ce203d96784e8a0bf727e924cb8ef59`, by hazre, authored 2026-05-02). Files modified by the PR:
   - `Il2CppInterop.Runtime/Injection/Hooks/MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.cs` (+3/-3)
   - `Il2CppInterop.Runtime/Injection/InjectorHelpers.cs` (+2/-2)
3. **Built** `Il2CppInterop.sln -c Release` with .NET SDK 8.0.413 (no net6 SDK was strictly needed — SDK 8.0 ships the net6.0 targeting pack and the solution's `global.json` pins `8.0.0` with `rollForward: latestFeature`). 191 warnings, 0 errors, 14.39 s.
4. **Backed up** the existing ci.829 DLLs from `BepInEx/core/` to `mac-spike/interop-test/pre-pr262-baseline/` (separate folder from `mac-spike/interop-test/baseline/` so the two backups don't conflate).
5. **Swapped** the rebuilt DLLs in (TFM matches baseline):
   - `Il2CppInterop.Common.dll` ← `bin/Il2CppInterop.Common/netstandard2.0/`
   - `Il2CppInterop.Generator.dll` ← `bin/Il2CppInterop.Generator/netstandard2.1/`
   - `Il2CppInterop.Runtime.dll` ← `bin/Il2CppInterop.Runtime/net6.0/`
   - `Il2CppInterop.HarmonySupport.dll` ← `bin/Il2CppInterop.HarmonySupport/net6.0/`
   - Confirmed swap via `cmp` byte-diff vs. baseline; new Runtime/Generator/Common/HarmonySupport hashes all differ from baseline.
6. **Relaunched** MTGA via `mac-spike/staging/run_bepinex.sh` (background) and monitored `BepInEx/LogOutput.log` with a tail+grep monitor watching for terminal markers (`Done in`, `Failed to generate`, `ArgumentOutOfRangeException`, `Chainloader initialized`).
7. **Killed** MTGA after the failure event fired (~3 minutes total: 36 s Cpp2IL + ~5 s Pass00–15 + crash + exception unwind + downloads thread shutdown).
8. **Restored** baseline DLLs (MD5-verified back to original hashes).

Archived logs in `mac-spike/interop-test/`:
- `LogOutput-symlink-fix.log` — Phase 1.B.2 baseline (ci.829 unchanged) — for reference.
- `LogOutput-pre-pr262-launch.log` — same as above, snapshot just before the PR262 launch.
- `LogOutput-pr262.log` — **this run, with PR262 DLLs swapped in**.

---

## The new (= same) stack trace

First six lines (this run, `LogOutput-pr262.log`):

```
[Error  :InteropManager] Failed to generate Il2Cpp interop assemblies: System.AggregateException: One or more errors occurred. (Specified argument was out of the range of valid values.) ...
 ---> System.ArgumentOutOfRangeException: Specified argument was out of the range of valid values.
   at Il2CppInterop.Common.XrefScans.XrefScannerLowLevel.ExtractTargetAddress(Instruction& instruction) in /home/runner/work/Il2CppInterop/Il2CppInterop/Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs:line 97
   at Il2CppInterop.Common.XrefScans.XrefScannerLowLevel.JumpTargetsImpl(Decoder myDecoder, Boolean ignoreRetn)+MoveNext() in /home/runner/work/Il2CppInterop/Il2CppInterop/Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs:line 32
   at Il2CppInterop.Generator.Utils.XrefScanMetadataGenerationUtil.FindMetadataInitForMethod(MethodRewriteContext method, Int64 gameAssemblyBase) in /home/runner/work/Il2CppInterop/Il2CppInterop/Il2CppInterop.Generator/Utils/XrefScanMetadataGenerationUtil.cs:line 50
   at Il2CppInterop.Generator.Passes.Pass16ScanMethodRefs.<>c__DisplayClass2_0.<DoPass>b__2(MethodRewriteContext originalTypeMethod) in /home/runner/work/Il2CppInterop/Il2CppInterop/Il2CppInterop.Generator/Passes/Pass16ScanMethodRefs.cs:line 56
```

Note the `/home/runner/work/...` prefix is `SourceLink`-rewritten github-actions path metadata stamped during my local `dotnet build` (Directory.Build.props has `<DebugType>embedded</DebugType>`, and the project uses default SourceLink behavior that maps local source paths to canonical upstream URLs). MD5 verification confirms the DLLs loaded at runtime were my local builds, not the originals.

The pipeline progresses exactly as far as in 1.B.2: Cpp2IL 36.7 s, Pass00–15 ≈ 5 s, Pass16 throws AggregateException with three sibling `ArgumentOutOfRangeException`s (3 parallel worker threads all tripping the same bug on different methods).

`BepInEx/interop/` is empty. `Chainloader initialized` still fires (BepInEx itself survives — the AggregateException is caught inside `Il2CppInteropManager.GenerateInteropAssemblies`).

---

## Why PR #262 doesn't apply (revisited)

PR #262 diff in full (from `git show 2e82b6c`):

```diff
--- a/Il2CppInterop.Runtime/Injection/Hooks/MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.cs
@@ -75,8 +75,8 @@
                 if ((getTypeInfoFromTypeDefinitionIndex.ToInt64() & 0xF) != 0)
                 {
-                    Logger.Instance.LogTrace("Image::GetType xref wasn't aligned, attempting to resolve from icall");
-                    return FindGetTypeInfoFromTypeDefinitionIndex(true);
+                    Logger.Instance.LogTrace("Image::GetType xref wasn't aligned, GetTypeInfoFromTypeDefinitionIndex is likely inlined into Image::GetType");
+                    getTypeInfoFromTypeDefinitionIndex = imageGetType;
                 }
--- a/Il2CppInterop.Runtime/Injection/InjectorHelpers.cs
@@ -187,7 +187,7 @@
             nint pClassInit = s_ClassInitSignatures
                 .Select(s => MemoryUtils.FindSignatureInModule(Il2CppModule, s))
-                .FirstOrDefault(p => p != 0);
+                .FirstOrDefault(p => p != 0 && (long)p >= (long)Il2CppModule.BaseAddress && (long)p < (long)Il2CppModule.BaseAddress + Il2CppModule.ModuleMemorySize);
```

Both edits are in `Il2CppInterop.Runtime/Injection/*`. The crash path is in `Il2CppInterop.Common.XrefScans.XrefScannerLowLevel.ExtractTargetAddress` (called by `XrefScanMetadataGenerationUtil.FindMetadataInitForMethod` in `Il2CppInterop.Generator/`). These are unrelated files in unrelated assemblies. The mismatch was foreseeable from the diff alone, but team-lead asked to run the test anyway as the last known candidate fix — confirmed negative result.

The PR is the right fix for the §3-predicted `0x80131506` `FindClassInit` crash that *Lonely Mountains: Snow Riders* hits (Il2CppInterop #1122). MTGA hits a different sibling bug in the same family: the **disassembler** (Iced 1.17.0) gets a malformed instruction during `Generator` xref scanning and `ExtractTargetAddress` throws on a missing operand. Likely the LLVM-optimized v31 code stream contains x86_64 instruction encodings that Iced 1.17.0 misclassifies — same root family (LLVM/v31 optimization), different scanner.

---

## Hypothetical further fixes (not pursued — Phase 1 NO-GO)

These are documented only to record what wasn't tried. **Team-lead instructed not to attempt ad-hoc fixes beyond PR #262**; capturing them here so future investigators don't waste a weekend rediscovering them.

1. **Upgrade `Iced` 1.17.0 → 1.21+** in `Il2CppInterop.Common.csproj`. The 1.18+ releases added correctness fixes for some malformed-instruction edge cases. *But:* a major-version bump of Iced has historically required code changes in `XrefScannerLowLevel` (decoder API changes), and there's no evidence that any specific Iced fix targets MTGA's crash. Speculative.
2. **Try/catch around the `FirstOrDefault` in `FindMetadataInitForMethod`** — return `0` instead of throwing. Pass16 would then skip methods whose metadata-init can't be found rather than crashing the whole generator. *But:* this is a generator-correctness regression; the resulting interop assemblies would have a partial set of method metadata-init pointers. Some Harmony patches and IL2CPP class injections would fail at runtime in unpredictable ways. Worth doing for "hello world" only if Phase 1.C resumes.
3. **Wait for upstream**. The `XrefScannerLowLevel` failure is not actively tracked in any open Il2CppInterop issue I could find. Lonely Mountains' #1122 is for `FindClassInit`, not `FindMetadataInitForMethod`. There is no in-flight PR for this code path.

None of these is the kind of fix you can do casually. They expand the project's scope from "use BepInEx 6" to "maintain a fork of Il2CppInterop's disassembler stack against MTGA's specific LLVM-optimized binary." That is **a different decision** than the user signed up for when they asked about Mac support.

---

## Recommendation

**Phase 1 → NO-GO.** The BepInEx 6 + Il2CppInterop loader cannot produce interop assemblies for MTGA's Unity 2022.3.62f2 / metadata v31 binary on macOS without forking and modifying Il2CppInterop's disassembler scanning — work that is materially larger than the "1–2 weeks of evening work" Phase 1 budget. The Phase 1.A risk register flagged this exact failure family ("BepInEx collaborator publicly stated 2025-11-05 that IL2CPP support is currently a dead end on macOS"); 1.B.2 + 1.B.3 confirmed it experimentally on MTGA's specific binary.

Per the original Phase 1 NO-GO exit criterion in `MAC_SUPPORT_PLAN.md`:

> generated Il2CppInterop bindings don't resolve game types. **Document findings and pause the project** — none of the rest of the plan matters until this works.

**Phase 1.C should not spawn.** Without `BepInEx/interop/Il2Cpp*.dll` we cannot reference game types from a plugin; HarmonyX patches have nothing to bind against.

**Suggested next step for the user:** the project's macOS support pauses here with two viable holding patterns:
1. Add the README note from `MAC_SUPPORT_PLAN.md` line 213 acknowledging the block.
2. Subscribe to Il2CppInterop's release feed; if a future ci-build addresses xref-scan failures on Mach-O / v31 / LLVM-optimized binaries (most likely via an Iced upgrade and a try/catch on `ExtractTargetAddress`), re-run 1.B.3 as a smoke test before reopening Phase 1.

---

## Files touched / created

Created:
- `mac-spike/il2cppinterop-fork/` — full clone, checked out at PR #262 (`pr-262` branch, commit `2e82b6c`). Build outputs in `bin/Il2CppInterop.{Common,Runtime,Generator,HarmonySupport}/*/`.
- `mac-spike/interop-test/pre-pr262-baseline/` — backup of the ci.829 DLLs that were in `BepInEx/core/` before this experiment. (Separate from `mac-spike/interop-test/baseline/`, which was 1.B.2's backup of the *same* DLLs — kept distinct per team-lead's instruction.)
- `mac-spike/interop-test/LogOutput-pre-pr262-launch.log` — snapshot of LogOutput just before swap (identical content to `LogOutput-symlink-fix.log`).
- `mac-spike/interop-test/LogOutput-pr262.log` — the failing run with PR #262 DLLs in `BepInEx/core/`.
- `mac-spike/PHASE1B3-FINDINGS.md` — this document.

Modified then restored:
- `mac-spike/staging/BepInEx/core/Il2CppInterop.{Common,Runtime,Generator,HarmonySupport}.dll` — swapped to PR262 build for the test run, **restored to baseline ci.829** after the negative result (MD5-verified).

Not touched (per Phase 1.B.2 instruction): the symlink shim at `mac-spike/staging/` (`MTGA_Data/`, `MTGA_Data.downloads-cache/`, `GameAssembly.dylib`).
