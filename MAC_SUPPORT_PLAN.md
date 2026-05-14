# Mac Support — Feasibility Roadmap

## Context

The developer's assessment is accurate: **MTGA on macOS ships as an IL2CPP build**, while the Windows build runs on Mono. The current mod depends on **BepInEx 5 + HarmonyX**, which only works against Mono. That single fact — not the Windows-specific helpers in the codebase — is the real blocker for Mac support.

This document is a research roadmap, not an implementation plan. It defines small spikes that produce a go/no-go signal before committing to the full port, and lists the work that follows if those spikes succeed. The goal is to leave the developer with a clear "what to try first, in what order, and when to stop" — so they can pick this up later without re-doing discovery.

---

## What Actually Blocks Mac Support

| Blocker | Severity | Why |
|---|---|---|
| MTGA macOS uses **IL2CPP** | **Critical** | Game C# is compiled to native C++; HarmonyX/Mono-style runtime patching does not apply. Reflection-based discovery (`AppDomain.CurrentDomain.GetAssemblies()` in `Plugin.cs`) returns no managed game assemblies. |
| **BepInEx 5** is Mono-only | **Critical** | The `winhttp.dll` UnityDoorstop entry point is a Windows/Mono path. macOS needs a `.dylib`-based doorstop and BepInEx 6's IL2CPP loader. |
| Windows P/Invoke in `Plugin/Helpers/WindowHelper.cs:13-40` | Medium | Already gated by `#if !UNITY_STANDALONE_OSX`; macOS replacement needs Cocoa `NSRunningApplication.activate()`. |
| Registry-based URL scheme in `Plugin/UrlScheme/UrlSchemeRegistrar.cs:36-59` | Medium | macOS requires `CFBundleURLSchemes` in an `Info.plist`; runtime registration is not really a thing on Mac. The file already has a no-op branch for `PlatformID.MacOSX` at line 23. |
| PowerShell installer (`install.ps1`, `Installer/Program.cs`) | Low | Mechanical rewrite to bash/zsh. |
| File-save dialog in `Plugin/Features/CollectionExporter.cs` | Low | Swap Win32 `comdlg32` for a Unity-side path picker or `NSSavePanel`. |

Cross-platform parts that already work and should not need changes: `Plugin/UrlScheme/TcpIpcServer.cs`, `Plugin/Firebase/*` (uses `UnityWebRequest`), all pure-C# state in `Plugin/State/`, and the Harmony patches in `Plugin/Patches/*` *as patches* — though each one will need its target types rebound to Il2CppInterop equivalents.

---

## The Approach: BepInEx 6 + Il2CppInterop

BepInEx 6 (pre-release) targets IL2CPP via Il2CppInterop, which generates managed proxy assemblies from `global-metadata.dat`. macOS injection uses a `.dylib` doorstop instead of `winhttp.dll`. This is the only path that preserves the mod's current feature set.

**Known risks the spike must validate:**
- BepInEx 6 IL2CPP support on **macOS specifically** is the least-trodden surface; most documented IL2CPP usage targets Windows games (Among Us, etc.).
- MTGA may obfuscate or strip metadata; Il2CppInterop needs a usable `global-metadata.dat`.
- Apple Gatekeeper / code-signing / SIP will likely block an unsigned `.dylib` from loading into a notarized app — testers may need to strip the quarantine bit (`xattr -dr com.apple.quarantine`) and disable library validation for MTGA's binary, or the mod will need to re-sign with an ad-hoc signature.
- Apple Silicon adds a second architecture target: every IL2CPP binary is `arm64` or `x86_64`, not both, and the mod must match.
- Every Harmony patch in `Plugin/Patches/*` (13 files) will need to be rewritten against Il2CppInterop-generated types (different namespace, different method signatures, no `MonoBehaviour` inheritance — must use `Il2CppSystem` types). HarmonyX's IL2CPP variant exists but its patch syntax and supported features differ from the Mono version.

---

## Phased Plan

### Phase 0 — Confirm the runtime (30 minutes) — ✅ **COMPLETE: GO**

Inspected the Steam-distributed MTGA bundle at `/Users/rexl/Library/Application Support/Steam/steamapps/common/MTGA/MTGA.app` (signed 2026-04-23 by Wizards of the Coast LLC, team `63JKFDP62M`).

| Check | Result |
|---|---|
| `Contents/Frameworks/GameAssembly.dylib` present | ✅ 255 MB, universal `x86_64 + arm64` |
| `Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat` present | ✅ 30 MB |
| Metadata magic bytes (expect `AF 1B B1 FA` for plaintext IL2CPP v24+) | ✅ `af1b b1fa` — plaintext |
| Metadata version (next u32 LE) | `0x1F` = **31** (Unity 2022.3+ era) |
| Unity engine version | **2022.3.62f2** (LTS, build `7670c08855a9`) |
| Main exe architecture | Universal `x86_64 + arm64` |
| `GameAssembly.dylib` code-sign flags | `0x0 (none)` — **no hardened runtime, no library validation** |
| Main `MTGA` exe code-sign flags | `0x0 (none)` — same |
| Bundle ID | `com.wizards.mtga` |

**Implications for Phase 1:**
- IL2CPP metadata version 31 is recent. Il2CppInterop's `Cpp2IL` frontend must support it (recent BepInEx 6 builds do; pin a known-good revision).
- The universal binary means **one mod build covers both Apple Silicon and Intel** — the Phase 2 risk about "every IL2CPP binary is arm64 or x86_64, not both" is downgraded. Plugin must still be a universal `.dylib` (or two thin builds in `fat` form) but no need to ship separate user-facing builds.
- The single best piece of news: **the main `MTGA` executable has neither the hardened runtime (`0x10000`) nor the library validation flag (`0x2000`) set.** Library validation is what forces only Apple-signed or same-team-signed dylibs to load. Without it, an unsigned (or ad-hoc-signed) BepInEx doorstop should load without needing to disable SIP or re-sign the host app. Gatekeeper quarantine (`xattr -dr com.apple.quarantine`) is still likely required for installer-downloaded `.dylib`s, but the deeper code-signing nightmare appears to be avoided.

**Exit criterion (go):** ✅ All checks pass. Proceed to Phase 1.

### Phase 1 — Il2CppInterop spike (1–2 weeks of evening work) — ❌ **NO-GO** (2026-05-14)

Goal: produce a `.dylib` that loads into MTGA on macOS, logs "hello world", and successfully calls one game method via Il2CppInterop. **No mod features yet.**

#### Phase 1.A — Research ✅ complete (2026-05-14)

Full writeup at [`mac-spike/RESEARCH.md`](mac-spike/RESEARCH.md). Key pins:

- **BepInEx 6 build #755** (commit `3fab71a`, 2026-03-07), artifact `BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.755+3fab71a.zip`. Bundles HarmonyX 2.10.2, Il2CppInterop 1.5.1-ci.829, Cpp2IL 2022.1.0-development.1452, MonoMod.RuntimeDetour 22.7.31.1, .NET 6.0.
- **No macOS-arm64 artifact exists** — must run x86_64 under Rosetta 2. Fine because MTGA's GameAssembly.dylib is universal. Apple Silicon is a Phase 3+ concern.
- **Interop generation is automatic** — first launch of MTGA with BepInEx installed triggers Cpp2IL → Il2CppInterop pipeline and writes to `BepInEx/interop/`. No manual CLI step.
- **Code-sign workflow (5 steps):** `xattr -dr com.apple.quarantine .` → `codesign --force --sign -` on `libdoorstop.dylib` + every `dotnet/*.dylib` → set `executable_name="MTGA.app"` in `run_bepinex.sh` → `chmod +x` → launch from Terminal (NOT Steam — `DYLD_*` env-var pass-through through Steam macOS is broken). **SIP does NOT need to be disabled.**
- **HarmonyX 2.10.2 + Il2CppInterop.HarmonySupport** translates `[HarmonyPatch(typeof(Il2Cpp.*))]` attributes into native detours against `GameAssembly.dylib` function pointers. Plugin entry point is `BasePlugin.Load()` (IL2CPP), not `BaseUnityPlugin.Awake()` (Mono).

#### Phase 1 — Elevated risk register (from Phase 1.A research)

The research surfaced known showstoppers that the spike must work around or fail against. These do **not** invalidate the plan but materially lower the probability of success:

- **🚨 BepInEx collaborator [toebeann](https://github.com/toebeann) wrote on 2025-11-06 (UTC) that "IL2CPP support is currently a dead end on macOS afaik"** ([BepInEx#1199 comment 3494802381](https://github.com/BepInEx/BepInEx/issues/1199#issuecomment-3494802381)). The hedge is theirs. Toebeann's `author_association` on the repo is `COLLABORATOR` — push access, not org spokesperson — and they recommend CrossOver + Windows BepInEx as the practical alternative.
- **🚨 Lonely Mountains: Snow Riders runs on the exact same Unity version as MTGA (2022.3.62f2) and crashes on launch** with `0x80131506` in `InjectorHelpers.FindClassInit()`. Root cause: Il2CppInterop's byte-pattern signature scan can't locate `il2cpp::vm::Class::Init` in LLVM-optimized v31 builds. Fallback: an Atelier Resleriana modder (`ongandrew` in [issue #1122](https://github.com/BepInEx/BepInEx/issues/1122)) published a Ghidra-derived x86_64 signature patch — also Unity 2022.3.62f2 — that we can apply to a forked Il2CppInterop if hit.
- **🚨 Il2CppInterop's `Process.GetCurrentProcess().Modules` reliance is broken on Darwin** ([Il2CppInterop #206](https://github.com/BepInEx/Il2CppInterop/issues/206), open since 2024). Means class injection and managed→IL2CPP delegate conversion will likely never work on macOS without porting MelonLoader's [PR #900](https://github.com/LavaGang/MelonLoader/pull/900) workarounds back into BepInEx. **Method patches on existing game classes — what most of `Plugin/Patches/*` does — have a fighting chance. Class injection and delegate-based UI work (much of `Plugin/UI/*`) probably do not.**
- **OPEN:** [Il2CppInterop PR #262](https://github.com/BepInEx/Il2CppInterop/pull/262) (May 2026, unmerged) addresses some v31 hook failures — may need cherry-picking.

**Updated implication for Phase 2 (UI layer step #5):** if class injection cannot be made to work on macOS, the UI layer port via ImGui-style overlay is no longer an alternative — it becomes the *only* viable approach. Mac-side UI cannot inherit from `MonoBehaviour`.

#### Phase 1.B — Workspace setup ✅ executed, ❌ smoke test failed (2026-05-14)

The bootstrap and injection mechanics all worked. The failure is one step deeper in the stack than the researcher predicted.

**What worked:**
- BepInEx 755 downloaded, extracted, staged into a 1GB copy of `MTGA.app` under `mac-spike/staging/`.
- Code-sign workflow (`xattr -dr com.apple.quarantine`, ad-hoc `codesign --force --sign -` on `libdoorstop.dylib` + `dotnet/*.dylib`) accepted on this M-series Mac.
- Terminal-launched `./run_bepinex.sh` — `DYLD_INSERT_LIBRARIES` propagated through to MTGA.
- `libdoorstop.dylib` injected; `Doorstop.Entrypoint.Start()` executed; BepInEx Preloader reached `Il2CppInteropManager.Initialize()`.
- MTGA host process survived to the asset-bundle download phase (13 GB of card art downloaded — the game's main thread was running independently of the failed preloader thread).

**What failed:** The BepInEx preloader threw before generating any `interop/*.dll`:

```
System.TypeInitializationException: The type initializer for 'Il2CppInterop.Runtime.Runtime.UnityVersionHandler' threw an exception.
 ---> System.ApplicationException: No handler
   at Il2CppInterop.Runtime.Runtime.UnityVersionHandler.GetHandler[T]()  (UnityVersionHandler.cs:124)
   at Il2CppInterop.Runtime.Runtime.UnityVersionHandler.RecalculateHandlers()  (line 102)
   at Il2CppInterop.Runtime.Runtime.UnityVersionHandler..cctor()  (line 75)
   at Il2CppInterop.Runtime.Startup.Il2CppInteropRuntime.Start()  (line 42)
   at BepInEx.Unity.IL2CPP.Il2CppInteropManager.Initialize()  (line 244)
   at BepInEx.Unity.IL2CPP.Preloader.Run()  (line 66)
   at Doorstop.Entrypoint.Start()  (DoorstopEntrypoint.cs:39)
```

Source: `mac-spike/staging/MTGA.app/Contents/MacOS/preloader_20260514_134253_618.log`

**Diagnosis:** Il2CppInterop's `UnityVersionHandler` static initializer iterates a registered set of handler classes (`Unity2019_4_*`, `Unity2022_1_*`, etc.) and dispatches based on the target's Unity version + metadata version. Build 755's bundled Il2CppInterop 1.5.1-ci.829 has **no handler class covering Unity 2022.3 / metadata v31** — the dispatch table lookup throws "No handler" before any IL2CPP code is touched. This is **earlier in the stack** than the predicted `FindClassInit` crash (which would happen during interop generation, after this point).

**Implication:** the issue is not LLVM-optimized binaries or aggressive inlining — it's a missing version-handler registration in the upstream library. Two possible fixes:
1. A newer Il2CppInterop `ci-*` build may already register a Unity 2022.3 handler. Bisect ci.829 → ci.840+ and check changelog/handler classes.
2. Failing that, fork Il2CppInterop, add a `Unity2022_3Handler` class derived from the nearest existing handler, and rebuild against BepInEx 755's runtime.

PR #262 (open) may or may not be relevant — it targets *signature resolution* in `InjectorHelpers`, not version-handler registration. Distinct issue, distinct fix.

#### Phase 1.B.2 — Resolve UnityVersionHandler dispatch ✅ root-cause solved (2026-05-14)

Full writeup at [`mac-spike/PHASE1B2-FINDINGS.md`](mac-spike/PHASE1B2-FINDINGS.md).

The "No handler" exception was a **false lead**: build 755's bundled Il2CppInterop 1.5.1-ci.829 already registers handlers covering Unity 2022.3 (e.g. `NativeClassStructHandler_29_1` is `[ApplicableToUnityVersionsSince("2022.2.0")]`). Bisecting ci.829 → ci.832 → ci.833 → ci.843 → ci.845 confirmed the handler set is *byte-identical* across all of them; upgrading the Il2CppInterop NuGet would not have changed anything.

The actual cause was **macOS path-detection in BepInEx**:
- `Paths.GameRootPath` resolves to the directory *containing* `MTGA.app` (four parents up from the executable). On Mac that's `…/MTGA/`, where there is an unrelated `MTGA_Data/` (asset-cache for Downloads). BepInEx finds that dir, sets `GameDataPath = .../MTGA_Data/`, then `UnityInfo.DetermineVersion()` walks `MTGA_Data/{globalgamemanagers,data.unity3d,mainData}` — none of which exist there. The real Unity player data lives at `MTGA.app/Contents/Resources/Data/`, per standard macOS Unity bundle layout, and is never searched.
- All three lookups fail. The Windows-only `FileVersionInfo` branch is skipped. The `Managed/UnityEngine.CoreModule.dll` heuristic finds nothing. `UnityInfo.Version = default` (= `0.0.0.0`).
- `UnityVersionHandler` then compares each handler's `StartVersion` (>= 2018.x) against `0.0.0` — every comparison says "greater than", no handler is selected, and `GetHandler<T>()` throws "No handler".

Fix is symlinks at the staging root (no DLL changes, original Il2CppInterop ci.829 still in place):

```sh
mv MTGA_Data MTGA_Data.downloads-cache              # preserve 13 GB asset cache
mkdir MTGA_Data
ln -s ../MTGA.app/Contents/Resources/Data/globalgamemanagers MTGA_Data/globalgamemanagers
ln -s ../MTGA.app/Contents/Resources/Data/il2cpp_data       MTGA_Data/il2cpp_data
ln -s ../MTGA_Data.downloads-cache/Downloads                MTGA_Data/Downloads
ln -s ../MTGA_Data.downloads-cache/Logs                     MTGA_Data/Logs
ln -s MTGA.app/Contents/Frameworks/GameAssembly.dylib       GameAssembly.dylib
```

After this fix BepInEx logs `Running under Unity 2022.3.62f2`, runs all 11 `UnityVersionHandler` cctors cleanly, loads `GameAssembly.dylib` via `DllImport` resolution, parses metadata v31 via `Cpp2IL`, generates dummy assemblies, and proceeds through `Il2CppInteropGen` passes 0–15. **Latent BepInEx bug:** `BepInEx.Unity.Common/UnityInfo.cs:94–96` is missing a `return` after the `UnityEngine.CoreModule.dll` heuristic sets `Version = new UnityVersion(2017,...)`, so it falls through to `Version = default` and overwrites the guess. Not exploitable for our fix but worth filing upstream.

#### Phase 1.B.3 — Il2CppInterop PR #262 cherry-pick + rebuild ❌ **FAIL** (2026-05-14)

Full writeup at [`mac-spike/PHASE1B3-FINDINGS.md`](mac-spike/PHASE1B3-FINDINGS.md).

Cherry-picked PR #262 (commit `2e82b6c`), rebuilt `Il2CppInterop.{Common,Runtime,Generator,HarmonySupport}` with .NET SDK 8.0.413, dropped the rebuilt DLLs in over `BepInEx/core/`, relaunched MTGA. **Same exact `ArgumentOutOfRangeException` at `XrefScannerLowLevel.cs:97`** — byte-for-byte identical stack trace to the pre-PR262 run. `BepInEx/interop/` stays empty.

Why it didn't help is visible in the PR diff: PR #262 only modifies `Il2CppInterop.Runtime/Injection/InjectorHelpers.cs` (bounds-check on `FindClassInit` signature scan) and `…/Hooks/MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.cs` (inline fallback). Neither touches `Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs`, which is where MTGA crashes. The hazre PR fixes the *runtime* `0x80131506` failure (Lonely Mountains, issue #1122); MTGA hits a sibling bug in the *generator* xref scanner — same root family (Iced-based x86_64 disassembly tripping on LLVM-optimized v31 binaries), different code path. There is no open PR targeting this code path.

The PR262 build is preserved at `mac-spike/il2cppinterop-fork/` for future reference. `BepInEx/core/` was restored to baseline ci.829 (MD5-verified). The symlink shim from 1.B.2 was not disturbed.

#### Phase 1 — NO-GO outcome

Per the original Phase 1 NO-GO exit criterion:

> generated Il2CppInterop bindings don't resolve game types. **Document findings and pause the project** — none of the rest of the plan matters until this works.

Phase 1.B.2 cleared a path-detection blocker via symlinks. Phase 1.B.3 attempted the final known candidate fix (PR #262) and confirmed it doesn't address the remaining failure mode. The next layer of fix is "fork Il2CppInterop's disassembler stack and maintain it against MTGA's specific LLVM-optimized binary" — materially larger than the Phase 1 evening-work budget, with no upstream momentum (a BepInEx collaborator publicly described macOS IL2CPP as "currently a dead end ... afaik" in November 2025; see the risk-register link above). **Phase 1.C does not spawn.** macOS support pauses here.

#### Phase 1.C — Hello-world plugin (NOT SPAWNED — Phase 1 NO-GO)

`BasePlugin` with one `[HarmonyPrefix]` against a trivial game method. Build pipeline produces a managed `.dll` dropped into the staged BepInEx plugins folder.

#### Phase 1 — Show stopper, precisely

The single technical failure that ended the spike, in one paragraph so future-you doesn't have to reread the three findings docs:

> `Il2CppInterop.Generator/Passes/Pass16ScanMethodRefs.DoPass` (the generator pass that scans method cross-references to emit interop metadata) calls into `Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.JumpTargetsImpl`, which uses an [Iced](https://github.com/icedland/iced) x86_64 decoder to walk function bodies inside `GameAssembly.dylib`. On MTGA's specific LLVM-optimized IL2CPP v31 binary, that decoder hits a control-flow construct it can't classify and `ExtractTargetAddress(Instruction&)` at `XrefScannerLowLevel.cs:97` throws `ArgumentOutOfRangeException`. The exception is caught by `Il2CppInteropManager.GenerateInteropAssemblies` as an `AggregateException`, generation aborts, and `BepInEx/interop/` stays empty. No interop assemblies = no plugin can reference game types = no mod.

Things that this is **NOT**:
- Not a code-signing problem (handled with `xattr -dr com.apple.quarantine` + ad-hoc `codesign --sign -`)
- Not a Doorstop / injection problem (loader survives, Preloader runs)
- Not a Unity version detection problem (resolved with the symlink shim; BepInEx reports `Running under Unity 2022.3.62f2`)
- Not a metadata-parsing problem (`Cpp2IL` finishes; dummy assemblies are emitted)
- Not the predicted `0x80131506 FindClassInit` crash (different code path; `ongandrew`'s Ghidra signature does **not** apply)

The fix is "harden the Iced-based xref scanner against optimized v31 binaries." That requires either a new code path in `Il2CppInterop.Common/XrefScans/`, a fallback that retries with a different disassembly strategy, or skipping the affected methods with a downgrade warning instead of aborting the whole generator pass. None of those exist as open PRs as of 2026-05-14.

---

### Resume conditions — what to watch for if BepInEx / Il2CppInterop change

The spike state is preserved (`mac-spike/staging/` with symlink shim intact, BepInEx 755 installed, pre-PR262 ci.829 DLLs baselined under `mac-spike/interop-test/pre-pr262-baseline/`, PR #262 build preserved at `mac-spike/il2cppinterop-fork/`). To retest after an upstream change, drop new DLLs into `staging/BepInEx/core/`, `cd mac-spike/staging`, run `./run_bepinex.sh` in a terminal, watch `BepInEx/LogOutput.log`. Success looks like `[Info :Il2CppInteropGen] Pass16ScanMethodRefs … Done in 00:00:XX.XX` followed by `BepInEx/interop/` populating with `Il2Cpp*.dll` files. If that happens, Phase 1.C (hello-world plugin) is unblocked and the spike resumes.

Specific signals, in rough priority order:

1. **Il2CppInterop xref scanner gets hardened.** This is the direct fix. Watch:
   - Any commit touching [`Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs`](https://github.com/BepInEx/Il2CppInterop/blob/master/Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs) (especially `ExtractTargetAddress` / `JumpTargetsImpl`).
   - Any commit touching [`Il2CppInterop.Generator/Passes/Pass16ScanMethodRefs.cs`](https://github.com/BepInEx/Il2CppInterop/blob/master/Il2CppInterop.Generator/Passes/Pass16ScanMethodRefs.cs) or [`Il2CppInterop.Generator/Utils/XrefScanMetadataGenerationUtil.cs`](https://github.com/BepInEx/Il2CppInterop/blob/master/Il2CppInterop.Generator/Utils/XrefScanMetadataGenerationUtil.cs).
   - New issues/PRs on `BepInEx/Il2CppInterop` mentioning `ExtractTargetAddress`, `ArgumentOutOfRangeException`, `Pass16`, "xref scan", or "optimized IL2CPP". GitHub search query worth saving: [`repo:BepInEx/Il2CppInterop Pass16 OR XrefScannerLowLevel OR ExtractTargetAddress`](https://github.com/search?q=repo%3ABepInEx%2FIl2CppInterop+Pass16+OR+XrefScannerLowLevel+OR+ExtractTargetAddress&type=issues).
   - Follow-ups to [PR #262](https://github.com/BepInEx/Il2CppInterop/pull/262) that extend into `Il2CppInterop.Common` (PR #262 itself only touches `Il2CppInterop.Runtime.Injection.*` — not our crash site).

2. **MelonLoader's macOS work gets ported into BepInEx / Il2CppInterop.** [MelonLoader PR #900](https://github.com/LavaGang/MelonLoader/pull/900) (aldelaro5, merged Jan 2026) is the most thorough public investigation of IL2CPP on macOS. Watch:
   - [Il2CppInterop #206](https://github.com/BepInEx/Il2CppInterop/issues/206) (`Process.GetCurrentProcess().Modules` doesn't enumerate dylibs on Darwin) — closure of this issue likely correlates with broader macOS readiness.
   - Any PR on `BepInEx/Il2CppInterop` referencing aldelaro5 or MelonLoader#900 by name.

3. **BepInEx adds native macOS bundle awareness.** This would let us drop the symlink shim from Phase 1.B.2 and would also signal renewed maintainer attention to the platform. Watch:
   - A `MacOSBundleDataPath` (or equivalent) added to [`BepInEx.Core/Paths.cs`](https://github.com/BepInEx/BepInEx/blob/master/BepInEx.Core/Paths.cs).
   - [`BepInEx.Unity.Common/UnityInfo.cs`](https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.Common/UnityInfo.cs) learning to look inside `<game>.app/Contents/Resources/Data/`.
   - The latent bug we found in `UnityInfo.cs:94–96` (missing `return` after the `Managed/UnityEngine.CoreModule.dll` heuristic sets `Version = new UnityVersion(2017,…)` — control falls through to `Version = default` and overwrites the guess) being fixed. Filing this upstream is a free maintenance task if anyone is so inclined.

4. **A native `macos-arm64` BepInEx 6 IL2CPP build ships.** Currently the only macOS artifact on [builds.bepinex.dev](https://builds.bepinex.dev/projects/bepinex_be) is `macos-x64`, forcing Rosetta 2 on Apple Silicon. The appearance of `BepInEx-Unity.IL2CPP-macos-arm64-*.zip` would not by itself fix our showstopper, but it strongly signals macOS investment. Track [BepInEx#899](https://github.com/BepInEx/BepInEx/issues/899) (Apple Silicon support) for closure.

5. **Public reports of BepInEx 6 IL2CPP working end-to-end on Unity 2022.3+ macOS games.** Most useful signal would be a working mod for **Lonely Mountains: Snow Riders** (also Unity 2022.3.62f2 — *exact* engine version match with MTGA) or **Atelier Resleriana** (same Unity version, different optimizer settings). Caveat: those games are documented as hitting the `0x80131506 FindClassInit` crash, which is a *different* code path from our `Pass16` failure. So a `FindClassInit` fix landing wouldn't automatically unblock us — we specifically need someone to solve the xref-scanner failure mode. Watch:
   - [BepInEx#1122](https://github.com/BepInEx/BepInEx/issues/1122) (metadata v31 thread; contains the `ongandrew` Ghidra signature for FindClassInit). If it's closed with a real fix, check whether the same PR touches xref scans.
   - Modding communities for those two games (Discord servers, Nexus Mods, r/Unity3D posts) reporting macOS success.

6. **MTGA changes the equation.** Lower-probability but worth noting:
   - MTGA bumps its Unity version (next LTS branch). The new binary might have different optimizer settings that don't trip Iced.
   - WotC ships a macOS update with IL2CPP disabled / Mono enabled (extremely unlikely).
   - MTGA ships a Wine/CrossOver-shaped Mac client (also unlikely, but precedented at Riot Games for Valorant on macOS).

7. **Toebeann reverses the "dead end" assessment.** Search [their public comments on BepInEx/Il2CppInterop](https://github.com/issues?q=commenter%3Atoebeann+org%3ABepInEx) for any update softening the November 2025 read. They flagged the assessment with `afaik` and recommended CrossOver as the practical workaround — both signal they're open to being wrong, not that they're committed to the negative position.

If a candidate signal lands, the retest cycle is ~15 minutes: swap DLLs, run the launch script, observe LogOutput. The 13 GB asset cache at `mac-spike/staging/MTGA_Data.downloads-cache/` is preserved, so MTGA does not re-download on relaunch.

---

#### Exit criteria (unchanged)

- **GO:** a single HarmonyX-IL2CPP patch on a trivial game method fires and writes a log line. The BepInEx loader survives launch without crashing MTGA.
- **NO-GO:** Gatekeeper blocks the `.dylib` even after quarantine strip; BepInEx 6 macOS IL2CPP loader is broken on Unity 2022.3.62f2 *and* the `ongandrew` signature patch fails to revive it; generated Il2CppInterop bindings don't resolve game types. **Document findings and pause the project** — none of the rest of the plan matters until this works.

### Phase 2 — Port (estimate only once Phase 1 succeeds)
With the spike proven, the remaining work is large but mechanical. In rough order:

1. **Entry point.** Port `Plugin/Plugin.cs` from `BaseUnityPlugin` (Mono) to `BasePlugin` (IL2CPP). Adjust `[BepInPlugin]` attribute usage and lifecycle hooks (`Load()` instead of `Awake()`).
2. **Type cache.** Replace reflection-based type lookup in `Plugin/GameRefs.cs` with direct references to Il2CppInterop-generated types.
3. **Project structure.** Update `Plugin/MTGAEnhancementSuite.csproj` (or add a sibling project) so the IL2CPP build is a separate target — Windows builds must remain untouched. Two NuGet feeds (BepInEx 5 for Windows, BepInEx 6 IL2CPP for Mac) and conditional `<PackageReference>` blocks.
4. **Patches.** Rewrite each file in `Plugin/Patches/*` (13 files) against Il2CppInterop signatures. This is the bulk of the work and must be done patch-by-patch with manual testing — there is no shortcut. Suggested order (highest value, simplest first): `BestOfPatch`, `CompanionDisablePatch`, `CardVFXPatch`, then `ChallengeCreatePatch`, `DeckValidationPatch`, `ChallengeJoinNotifyPatch`, then the remaining UI-integration patches.
5. **UI layer.** Convert `MonoBehaviour` UI components in `Plugin/UI/*` (~4,500 LOC, especially `EnhancementSuitePanel.cs`) to `Il2CppSystem.Object`-derived equivalents. Components attached to game objects must be registered with Il2CppInterop's class injector. If conversion proves too painful for the larger panels, consider moving them to an ImGui-style overlay rendered by the plugin instead.
6. **Window focus.** Implement `WindowHelper.BringToFront()` for macOS using `NSRunningApplication.runningApplicationWithProcessIdentifier(_:).activate(options:)` via P/Invoke into `AppKit`. Replace the `#if !UNITY_STANDALONE_OSX` no-op at `Plugin/Helpers/WindowHelper.cs:13`.
7. **URL scheme.** Replace the no-op macOS branch at `Plugin/UrlScheme/UrlSchemeRegistrar.cs:23` with installer-driven registration: ship a tiny stub `.app` bundle whose `Info.plist` declares `CFBundleURLSchemes = mtgaes`, which forwards the URL to the running MTGA via the existing `TcpIpcServer`.
8. **File dialog.** Replace `CollectionExporter`'s Win32 save dialog with either an in-Unity path picker, an `NSSavePanel` P/Invoke, or a shell-out to `osascript -e 'choose file name'`.
9. **Installer.** Write `install.sh` to mirror `install.ps1`: locate MTGA via `mdfind 'kMDItemCFBundleIdentifier == "com.wizards.mtga"'`, drop the `.dylib` and BepInEx 6 payload, strip quarantine bits, install the URL-handler `.app` to `~/Applications`. Replace `Installer/Program.cs` with a SwiftUI or shell-wrapped installer; PowerShell is not available on Mac.

---

## Critical Files for the Eventual Port

- `Plugin/Plugin.cs` — entry point; Mono `BaseUnityPlugin` → IL2CPP `BasePlugin`
- `Plugin/GameRefs.cs` — reflection-based type cache; rewrite against Il2CppInterop types
- `Plugin/Patches/*.cs` (13 files) — every patch needs IL2CPP-compatible rewrite
- `Plugin/UI/*.cs` (~4,500 LOC, especially `EnhancementSuitePanel.cs`) — `MonoBehaviour` subclasses need IL2CPP conversion
- `Plugin/Helpers/WindowHelper.cs` — add macOS branch using AppKit
- `Plugin/UrlScheme/UrlSchemeRegistrar.cs:23` — replace no-op macOS branch with installer-driven `.app` bundle
- `Plugin/Features/CollectionExporter.cs` — cross-platform save dialog
- `install.ps1` + `Installer/Program.cs` → new `install.sh` + a tiny macOS installer app
- `MTGAEnhancementSuite.csproj` — add a separate IL2CPP target (or a second project) so Windows builds aren't broken

---

## Verification (Once Implementation Exists)

This roadmap doesn't ship code, but when implementation happens the test plan is:

1. **Phase 1 spike:** Mac tester runs MTGA with the bare `.dylib`; BepInEx log file shows "hello world" and one game-method call result. No crash on launch.
2. **First feature smoke test:** Custom Format Lobby creation works end-to-end Mac → Mac, and Mac → Windows. Firebase backend confirms challenge written.
3. **UI test:** MTGA+ button appears in nav bar; lobby browser populates. Visually compare to Windows.
4. **Cross-platform invite:** Windows user clicks an `mtgaes://` link → Mac MTGA brought to foreground and joins lobby. Validates URL scheme handler + `WindowHelper` macOS branch.
5. **Regression:** Re-run the Windows build on the same branch; no behavior change. The Windows path must remain untouched.
6. **Apple Silicon:** Test on both Intel and Apple Silicon Macs; IL2CPP binaries are architecture-specific.

---

## What to Tell Users in the Meantime

A short note in `README.md` is worth adding even before any code work — something like: *"macOS native support is blocked by MTGA's IL2CPP build (the Windows build uses Mono, which our injection framework targets). We're tracking it but have no ETA."* This sets expectations honestly without overcommitting.
