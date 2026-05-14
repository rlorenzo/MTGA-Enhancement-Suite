# Phase 1.A — BepInEx 6 IL2CPP macOS Research

**Author:** researcher (mac-spike team)
**Date:** 2026-05-14
**Target game:** MTGA on macOS (Unity 2022.3.62f2, IL2CPP, metadata v31, universal `GameAssembly.dylib` arm64+x86_64, host exe code-sign flags 0x0)

---

## TL;DR — The Hard Truths

Before diving in, two findings dominate everything else and the downstream agents must internalize them:

1. **BepInEx 6's IL2CPP support on macOS is officially "a dead end" per BepInEx collaborator toebeann (Nov 2025).** No one is actively working on it inside the BepInEx org. The bleeding-edge builds publish a `macos-x64` IL2CPP artifact, but every recent issue against it (Lonely Mountains: Snow Riders — which is *also* Unity 2022.3.62f2 like MTGA — Atelier Resleriana, JSAB) ends in unresolved crashes. Source: https://github.com/BepInEx/BepInEx/issues/1199#issuecomment-3494802381

2. **The closest cousin project, MelonLoader, did the actual macOS IL2CPP work and reports it as "VERY unreliable"** even after weeks of effort by aldelaro5 — and their findings (PR #900, merged Jan 2026) have **not** been ported back into BepInEx/Il2CPPInterop. The fundamental blocker is `Il2CppInterop`'s reliance on `Process.GetCurrentProcess().Modules` which **doesn't return GameAssembly.dylib on macOS** — neither old Mono, new Mono, nor CoreCLR exposes the loaded dylibs through that API on Darwin. Source: https://github.com/BepInEx/Il2CppInterop/issues/206 (still open as of May 2026), https://github.com/LavaGang/MelonLoader/pull/900

The spike should proceed with eyes open: we are betting we can get *something* to load and inject *some* hooks, but class-injection, late-bound delegate→Il2Cpp conversion, and aggressive-inlining cases (which `GameAssembly.dylib` has more of on macOS thanks to LLVM) are likely to break and may require us to fork Il2CppInterop with MelonLoader's Harmony-patch workarounds.

---

## 1. BepInEx 6 release/commit to pin

### Recommendation: Build #755 (commit `3fab71a`)

- **Artifact name:** `BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.755+3fab71a.zip`
- **Download URL:** `https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.755+3fab71a.zip`
- **Built from commit:** `3fab71a1914132a1ce3a545caf3192da603f2258` (BepInEx/BepInEx)
- **Build date:** 2026-03-07
- **Commit message:** *"Improved support for IL2CPP metadata v23-106 (Unity 6+) (#1284) — chore(deps): update Cpp2IL and Il2CppInterop"*
- **Source:** https://builds.bepinex.dev/projects/bepinex_be , https://github.com/BepInEx/BepInEx/commit/3fab71a1914132a1ce3a545caf3192da603f2258

### Bundled dependency versions (read directly from `Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj` at commit `3fab71a`):

| Package | Version |
|---|---|
| HarmonyX | `2.10.2` |
| Il2CppInterop.Generator | `1.5.1-ci.829` |
| Il2CppInterop.HarmonySupport | `1.5.1-ci.829` |
| Il2CppInterop.Runtime | `1.5.1-ci.829` |
| Il2CppInterop.ReferenceLibs | `1.0.0` |
| Samboy063.Cpp2IL.Core | `2022.1.0-development.1452` |
| MonoMod.RuntimeDetour | `22.7.31.1` |
| Iced (x86 disasm) | `1.21.0` |
| Microsoft.Extensions.Logging | `6.0.0` |
| .NET TFM | `net6.0` |

Source (verified live): https://raw.githubusercontent.com/BepInEx/BepInEx/3fab71a/Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj

### Why this build and not a newer one

Builds 752–755 all contain the same IL2CPP runtime DLLs (the changes between them are mostly Mono/dependency churn). **Build 755 is the latest macOS-x64 IL2CPP artifact at time of writing (2026-05-14).** Build 754 (`c038613`, 2026-02-24) is a known-stable fallback if 755 misbehaves.

Builds older than 752 lack metadata v31 plumbing entirely and will hard-fail with *"Unsupported metadata version found! We support 23–29, got 31"* — confirmed in https://github.com/BepInEx/BepInEx/issues/1122 (Persona 5: The Phantom X).

### IMPORTANT: there is NO `macos-arm64` artifact

The BepInEx CI only publishes `BepInEx-Unity.IL2CPP-macos-x64-*.zip` (also Mono-x64 and the Linux/Windows variants). There is no native arm64 build. For Apple Silicon Macs we must run the entire BepInEx + CoreCLR + Il2CppInterop stack under Rosetta 2.

This is fine for MTGA because `GameAssembly.dylib` is universal (arm64 + x86_64) — Rosetta 2 will pick the x86_64 slice when the parent process is x86_64. The `run_bepinex.sh` script (see §2 below) explicitly handles this with `arch -e DYLD_INSERT_LIBRARIES=...` on Apple Silicon.

**OPEN QUESTION:** Whether Apple Silicon support via the merged MonoMod PR #241 ("Linux and MacOS arm64 support") is actually available end-to-end through BepInEx build 755. MonoMod issue #90 is closed via PR #241, but BepInEx still pins `MonoMod.RuntimeDetour 22.7.31.1` — verify whether that version pre- or post-dates the arm64 work. The Valheim community is still pleading for native ARM64 support as recently as March 2026: https://github.com/BepInEx/BepInEx/issues/899#issuecomment . Working assumption: we run under Rosetta 2.

---

## 2. UnityDoorstop variant for macOS

### What ships in the BepInEx-Unity.IL2CPP-macos-x64 zip

Based on the official install doc (https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) and the `run_bepinex_il2cpp.sh` source (https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/Doorstop/run_bepinex_il2cpp.sh), the archive contains at the top level:

```
BepInEx/                        # Plugin and config root
  core/                         # BepInEx core DLLs incl. BepInEx.Unity.IL2CPP.dll
dotnet/                         # CoreCLR runtime (libcoreclr.dylib + corlibs)
libdoorstop.dylib               # UnityDoorstop native loader
run_bepinex.sh                  # Wrapper script (renamed from run_bepinex_il2cpp.sh)
changelog.txt
```

The user/tester extracts this **into the game folder where the `.app` bundle lives** (i.e. `…/steamapps/common/MTGA/`, alongside `MTGA.app`), **NOT inside** `MTGA.app/Contents/MacOS/`. Source: https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

### Injection mechanism: `DYLD_INSERT_LIBRARIES` (NOT `LC_LOAD_DYLIB`)

The script exports the following env vars before exec-ing the game (verbatim from `run_bepinex_il2cpp.sh` master HEAD):

```sh
export DOORSTOP_ENABLED="$enabled"                         # "1"
export DOORSTOP_TARGET_ASSEMBLY="$target_assembly"         # abs path to BepInEx/core/BepInEx.Unity.IL2CPP.dll
export DOORSTOP_CLR_RUNTIME_CORECLR_PATH="$coreclr_path.$lib_extension"  # dotnet/libcoreclr.dylib
export DOORSTOP_CLR_CORLIB_DIR="$corlib_dir"               # dotnet
export DOORSTOP_IGNORE_DISABLED_ENV="$ignore_disable_switch"
export DOORSTOP_BOOT_CONFIG_OVERRIDE="$boot_config_override"

export DYLD_LIBRARY_PATH="${doorstop_directory}:${DYLD_LIBRARY_PATH}"
if [ -z "$DYLD_INSERT_LIBRARIES" ]; then
    export DYLD_INSERT_LIBRARIES="${doorstop_name}"        # libdoorstop.dylib
else
    export DYLD_INSERT_LIBRARIES="${doorstop_name}:${DYLD_INSERT_LIBRARIES}"
fi

if [ -n "${is_apple_silicon}" ]; then
    export ARCHPREFERENCE="arm64,x86_64"
    # arch strips DYLD_INSERT_LIBRARIES; re-pass it manually
    exec arch -e DYLD_INSERT_LIBRARIES="${DYLD_INSERT_LIBRARIES}" "$executable_path" "$@"
else
    exec "$executable_path" "$@"
fi
```

Selection between `DYLD_INSERT_LIBRARIES` vs `LC_LOAD_DYLIB`: **Doorstop 4 on macOS uses `DYLD_INSERT_LIBRARIES` exclusively.** There is no `LC_LOAD_DYLIB` rewriting performed against the host binary (which would require code-signing changes and `optool` / `install_name_tool` surgery). This is the only mode supported on macOS. Source: shell script linked above + BepInEx 6 release notes ("upgraded Doorstop from v3.4 to v4.3") https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/Doorstop/run_bepinex_il2cpp.sh

### Apple Silicon caveat (relevant to MTGA testers)

Because MTGA's host exe is universal, on M-series Macs the script (a) sets `ARCHPREFERENCE=arm64,x86_64` and (b) wraps the exec in `arch -e DYLD_INSERT_LIBRARIES=…`. The `-e` flag is mandatory because `arch(1)` otherwise strips `DYLD_*` env vars when it re-execs.

**However, BepInEx itself is `macos-x64` only.** The `arch -e arm64,x86_64` preference will pick arm64 first, but `libdoorstop.dylib` is an **x86_64-only** dylib — so the runtime will fall through to the x86_64 slice of MTGA. Net effect on Apple Silicon: MTGA + GameAssembly + Doorstop all run x86_64 under Rosetta 2.

**OPEN QUESTION:** Is `libdoorstop.dylib` in build 755 a universal binary, or x86_64-only? The artifact directory is named `macos-x64`, strongly implying x86_64-only, but the script's Apple-Silicon branch suggests intent to support both. The setup-engineer should `lipo -info libdoorstop.dylib` after download and report back.

### MTGA host exe ↔ Steam launch flow

Steam launches `MTGA.app` (a `LSApplicationCategoryType` bundle). Per the BepInEx run script, the tester does NOT use "Set Launch Options" in Steam. Instead:

1. Extract BepInEx archive next to MTGA.app
2. Edit `run_bepinex.sh` to set `executable_name="MTGA.app"`
3. In Steam, set launch options to: `"/full/path/to/run_bepinex.sh" %command%` — **this is the Linux pattern; on macOS Steam interpretation differs.**

**OPEN QUESTION (important):** Steam on macOS has known issues passing through `DYLD_*` env vars from Launch Options (per https://github.com/ValveSoftware/steam-for-linux/issues/5548 and https://github.com/BepInEx/BepInEx/issues/513). The reliable path is **launching `run_bepinex.sh` directly from Terminal** with `./run_bepinex.sh` after `cd` into the game folder. Steam-launch integration on macOS is a known-broken/finicky area and should be deferred to Phase 2 — Phase 1 testing should always go through terminal.

---

## 3. Il2CppInterop generator — how interop assemblies are produced

### Generation is automatic, NOT a separate CLI step

BepInEx 6 IL2CPP bundles **both** Cpp2IL and Il2CppInterop.Generator **as embedded libraries** in `BepInEx.Unity.IL2CPP.dll`. On the **first launch** of the game with BepInEx installed, the Preloader runs the equivalent of:

1. `RunCpp2Il()` — analyzes `GameAssembly.dylib` + `global-metadata.dat` and produces *"dummy"* managed assemblies (type/method signatures, no bodies). Output: `BepInEx/cpp2il_out/`.
2. `RunIl2CppInteropGenerator()` — wraps the dummy assemblies into real CoreCLR-loadable proxies. Output: `BepInEx/interop/`.
3. Unity reference libraries are written to `BepInEx/interop/unity-libs/`.
4. A hash of `GameAssembly.dylib` + Unity libs is stored at `BepInEx/interop/assembly-hash.txt` to skip regeneration on subsequent launches.

Source: `Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs` (file confirmed at commit `3fab71a` via `gh api`), DeepWiki summary https://deepwiki.com/BepInEx/BepInEx/5.2-il2cpp-interop , and forum posts confirming the "first launch takes several minutes" UX.

### The plugin-author workflow

Phase-1.C will:

1. Launch MTGA once with empty `BepInEx/plugins/` so that `BepInEx/interop/*.dll` is generated.
2. Copy the contents of `BepInEx/interop/` into the plugin's `lib/interop/` folder.
3. Reference whichever proxy DLLs the plugin needs via `<Reference>` items in the `.csproj`.

**No manual `Cpp2IL.exe …` invocation is required.** The downstream plugin agent does not need to set up a separate generator pipeline.

### Metadata v31 status — the critical risk

Build 755 (and the bundled Cpp2IL `2022.1.0-development.1452`) **does parse metadata v31** (Lonely Mountains: Snow Riders on Unity 2022.3.62f2 reaches managed-side loading per https://github.com/BepInEx/BepInEx/issues/1122#issuecomment-… Letiliel). But several **runtime-stage** crashes are documented for the same Unity version:

- **Fatal `0x80131506` CLR error in `InjectorHelpers.FindClassInit()`** — Il2CppInterop's signature scan can't locate `il2cpp::vm::Class::Init` in optimized v31 builds. Workaround: an Atelier Resleriana modder published a Ghidra-derived signature in https://github.com/BepInEx/BepInEx/issues/1122 (comment by `ongandrew`, captured below in §4). Not yet upstreamed.
- **`InvalidOperationException: Sequence contains no elements` in `MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook.FindGetTypeInfoFromTypeDefinitionIndex`** — fixed in *open* Il2CppInterop PR #262 (May 5 2026, not yet merged): https://github.com/BepInEx/Il2CppInterop/pull/262
- **`Class::Init` / `GarbageCollector::RunFinalizer` native-hook layer dies** when the fallback stub dereferences garbage on v31 binaries (https://github.com/BepInEx/BepInEx/issues/1122 comment by `a-l-e-k-k`).
- **Embedded Cpp2IL crashes on certain DLLs** (Rust: `Azure.Storage.Blobs.dll`): https://github.com/BepInEx/BepInEx/issues/1231 . Workaround: replace the bundled `Cpp2IL.Core` with the standalone `2022.1.0-pre-release.20` (Aug 2025) or `2022.1.0-pre-release.21` (Feb 22 2026): https://github.com/SamboyCoding/Cpp2IL/releases/tag/2022.1.0-pre-release.21

Practical implication for Phase 1.B: if first-launch interop generation fails for MTGA, the setup-engineer should swap in the standalone Cpp2IL pre-release.21 binary or be prepared to apply the InjectorHelpers signature patch + Il2CppInterop PR #262 as a forked local build.

---

## 4. Known issues filed against BepInEx (macOS / Unity 2022.3 / IL2CPP v31)

Curated list of open issues that we will likely hit. Cite these in commit messages and READMEs.

| # | Repo | Title | Relevance |
|---|---|---|---|
| [#1122](https://github.com/BepInEx/BepInEx/issues/1122) | BepInEx | Unity 2022.3 (Metadata v31) IL2CPP support required for Persona 5: TPX | Documents the v31 cascade. **Contains a Ghidra-derived `il2cpp_runtime_class_init` signature patch (ongandrew, Atelier Resleriana, also Unity 2022.3.62f2) that may be directly applicable to MTGA.** |
| [#1199](https://github.com/BepInEx/BepInEx/issues/1199) | BepInEx | bepinex_IL2CPP_macos on JSB doesn't work | **Contains the "IL2CPP support is currently a dead end on macOS" statement from collaborator toebeann, Nov 2025.** |
| [#1096](https://github.com/BepInEx/BepInEx/issues/1096) | BepInEx | BepInEx 6 fails to initialize on macOS Intel (no logs, no config generated) | Open since Apr 2025, labeled `needs-replication`. Symptom: Doorstop appears not to inject — could be a code-sign / SIP / quarantine issue. |
| [#1231](https://github.com/BepInEx/BepInEx/issues/1231) | BepInEx | Cpp2IL crashes on Azure.Storage.Blobs.dll generating interop for Rust (Unity 2022.3.41f1, metadata v31) | Fixed by standalone Cpp2IL pre-release.20+. |
| [#1247](https://github.com/BepInEx/BepInEx/issues/1247) | BepInEx | BepInEx GTFO not working on CrossOver 26 / Preview (macOS) | Documents the CrossOver-fallback path if native macOS proves untenable. |
| [#206](https://github.com/BepInEx/Il2CppInterop/issues/206) | Il2CppInterop | `InjectorHelpers.Il2CppModule`'s initializer throws on Mac OS | **The single most important blocker. Filed by aldelaro5 (MelonLoader maintainer). Open as of May 2026.** Three structural issues: (1) hardcoded `GameAssembly.dll/so/UserAssembly.dll` names — `dylib` missing; (2) `Process.GetCurrentProcess().Modules` doesn't enumerate dylibs on Darwin; (3) no dotnet API exposes module base-address/size — `il2cppinterop` cannot patch what it cannot locate. |
| [#262](https://github.com/BepInEx/Il2CppInterop/pull/262) | Il2CppInterop | fix: harden hook and signature resolution for optimized IL2CPP builds | **OPEN PR**, hazre, May 5 2026. Addresses some of the v31 hook failures (false-positive byte-pattern scans + inlined-function fallbacks). May need to be cherry-picked locally. |
| [#911](https://github.com/BepInEx/BepInEx/issues/911) | BepInEx | Docs Out of date, missing macOS arm64 | Reminder that docs lie about ARM64 support. |
| [#899](https://github.com/BepInEx/BepInEx/issues/899) | BepInEx | MacOS arm64 (Apple Silicon) Support - Valheim | Long-running ARM64 thread; *Avakining* notes MonoMod #90 is fixed via PR #241, but no BepInEx release has integrated it. |
| [#989](https://github.com/BepInEx/BepInEx/issues/989) | BepInEx | BepInEx 6.0 crashes due to access violation on Whisky (Wine on Mac) | Wine-bottle path, not our target, but documents Apple Silicon failure modes. |

### The Ghidra-derived InjectorHelpers signature (worth committing to lib/ early)

From https://github.com/BepInEx/BepInEx/issues/1122 (`ongandrew` comment, Atelier Resleriana = Unity 2022.3.62f2 — same Unity build as MTGA):

```csharp
new MemoryUtils.SignatureDefinition
{
    pattern = "\x48\x8B\xC4\x48\x89\x58\x00\x48\x89\x68\x00\x48\x89\x70\x00\x57\x41\x56\x41\x57\x48\x83\xEC\x00",
    mask    = "xxxxxx?xxx?xxx?xxxxxxxxx?",
    xref    = false
},
```
Paired with `Logging.UnityLogListening = false` in `BepInEx.cfg` to defer `BepInEx.Unity.IL2CPP.Logging.IL2CPPUnityLogSource..ctor()` which is the call site that first triggers `FindClassInit()`.

This pattern is **x86_64** instructions, which is what MTGA's GameAssembly will execute under Rosetta 2 on Apple Silicon — so it likely directly applies. The setup-engineer should keep this patch ready to apply to a fork of `Il2CppInterop.Runtime/Injection/InjectorHelpers.cs` if first-launch hits the `0x80131506` crash.

---

## 5. Code-signing / Gatekeeper / quarantine workflow

### MTGA host exe attributes (per Phase 0 capture)

- Code-sign flags: `0x0` → **no hardened runtime**, **no library validation**.
- Standard ad-hoc Apple signing on the Unity player and `GameAssembly.dylib`.

This is the **best-case scenario** for DYLD injection:

| Property | MTGA value | Implication |
|---|---|---|
| Hardened runtime (`0x10000`) | not set | `DYLD_INSERT_LIBRARIES` is honored |
| Library validation (`0x2000`) | not set | unsigned/ad-hoc dylibs from a different team ID can be loaded |
| SIP-protected path | no (Steam install is in `~/Library/Application Support`, user-writable) | env vars not stripped |

Source on flag semantics: https://theevilbit.github.io/posts/dyld_insert_libraries_dylib_injection_in_macos_osx_deep_dive/

### Minimum required steps for a tester

These are the steps the setup-engineer should bake into a `bootstrap.sh` or README:

```bash
# 1. Extract BepInEx 755 next to MTGA.app
cd "/Users/<user>/Library/Application Support/Steam/steamapps/common/MTGA"
unzip ~/Downloads/BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.755+3fab71a.zip

# 2. Strip the quarantine xattr from every file (xattr propagates from the .zip)
xattr -dr com.apple.quarantine .

# 3. Ad-hoc re-sign the dylib so the kernel doesn't reject it on Apple Silicon
codesign --force --sign - libdoorstop.dylib
codesign --force --sign - dotnet/libcoreclr.dylib
# (optionally re-sign every dylib under dotnet/)
find dotnet -name '*.dylib' -exec codesign --force --sign - {} \;

# 4. Configure the run script
sed -i '' 's/executable_name=""/executable_name="MTGA.app"/' run_bepinex.sh
chmod +x run_bepinex.sh

# 5. Launch from terminal (NOT from Steam for Phase 1)
./run_bepinex.sh
```

### Why each step is necessary

- **`xattr -dr com.apple.quarantine`**: when a `.zip` is downloaded via a browser, every file inside inherits the quarantine bit. Gatekeeper then refuses to load unsigned dylibs with *"app is damaged and can't be opened"*. Source: https://hacktricks.wiki/en/macos-hardening/macos-security-and-privilege-escalation/macos-security-protections/macos-gatekeeper.html
- **`codesign --force --sign -`** (ad-hoc): on Apple Silicon, **any dylib with a missing or broken signature is killed at load time** by the kernel — this is unconditional, not gated on library validation. Ad-hoc sign (`--sign -`) is sufficient because the **host exe doesn't have library validation set**, so any team-ID-less signature is accepted. Source: https://theevilbit.github.io/posts/gatekeeper_bypass_or_not_bypass/
- **SIP is NOT needed to be disabled.** Steam installs MTGA into `~/Library/Application Support/Steam/...` which is user-owned, not in `/System` or `/usr`. The exe itself has flags=0x0. `DYLD_INSERT_LIBRARIES` is only stripped on SIP-protected binaries; MTGA is not one. **Confirmation:** Apple's dyld docs + the script's reliance on env-var injection (which would be a no-op if SIP filtering applied).

### Steam-launch implications

**Recommendation for Phase 1: do NOT launch via Steam.** Launch `./run_bepinex.sh` from Terminal. Reasons:

1. Steam on macOS has historic issues passing through `DYLD_*` env vars from Launch Options: https://github.com/ValveSoftware/steam-for-linux/issues/5548
2. When Steam launches `MTGA.app`, it goes via `open` / `LaunchServices`, which **drops env vars** that aren't on a whitelist. `DYLD_INSERT_LIBRARIES` is on the *blocklist* for hardened-runtime processes (we're not hardened, so this is theoretically OK, but Steam's launcher itself may or may not be hardened).
3. Even setting `LSEnvironment` keys in `Info.plist` is unreliable: https://github.com/BepInEx/BepInEx/issues/513

A future Phase-2 deliverable might be a small launcher that re-execs Steam-bootstrap → `run_bepinex.sh`, like the Linux `SteamLaunch` branch already present in the bundled script. For Phase 1, terminal-launch is sufficient.

### What about notarization / Gatekeeper warnings on first run?

Because we ad-hoc sign rather than trying to fake a Developer ID, the first time the game launches *with the loader injected*, Gatekeeper will not pop a warning (no quarantine xattr → no first-launch prompt). If a tester has *not* stripped the quarantine bit, they will see *"libdoorstop.dylib cannot be opened because the developer cannot be verified"*. The fix is `xattr -dr com.apple.quarantine .`.

---

## 6. HarmonyX in BepInEx 6 IL2CPP — can it patch arbitrary methods?

### Short answer

Yes, with caveats. BepInEx 6 IL2CPP bundles **HarmonyX 2.10.2** + **Il2CppInterop.HarmonySupport 1.5.1-ci.829** + **MonoMod.RuntimeDetour 22.7.31.1**. HarmonyX, on top of `Il2CppInterop.HarmonySupport`, knows how to translate `[HarmonyPatch]` attributes that target Il2CppInterop-generated proxy classes into native detours against the underlying `GameAssembly.dylib` function pointers (via the `MethodInfo.GetIl2CppMethodPointer()` extension).

The caveats:

- **Methods with `ref` parameters in IL2CPP**: known to generate invalid IL on some IL2CPP builds — https://github.com/BepInEx/HarmonyX/issues/129, https://github.com/BepInEx/HarmonyX/issues/93 (open).
- **Constructor patching**: https://github.com/BepInEx/Il2CppInterop/issues/87 (caveats around `.ctor` thunking).
- **Aggressively inlined methods on macOS** — the same LLVM-inlining problem from MelonLoader PR #900. If MTGA inlined an `update`-style hot path into 30 different call sites, patching the "named" function won't intercept any of those copies. This is a IL2CPP-on-macOS reality, not a HarmonyX bug.

### Modern call syntax (BepInEx 6, IL2CPP)

A reasonable hello-world plugin (verified against docs https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html and BepInEx.Utility.IL2CPP examples https://github.com/BepInEx/BepInEx.Utility.IL2CPP):

```csharp
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MtgaEnhancementSuite.Mac;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded");

        // Optional: Harmony auto-patch
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(Plugin).Assembly);
    }
}

// Example patch against an Il2CppInterop-generated proxy
[HarmonyPatch(typeof(Il2Cpp.UnityEngine.Application), nameof(Il2Cpp.UnityEngine.Application.Quit))]
public static class QuitPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        Plugin.Log.LogInfo("Application.Quit intercepted");
        return true; // false to skip original
    }
}
```

Things to note:

1. The plugin class extends `BasePlugin` (BepInEx 6 IL2CPP), not `BaseUnityPlugin` (which is the Mono variant). Entry point is `Load()`, not `Awake()`.
2. Il2CppInterop-generated types live under the `Il2Cpp` namespace prefix by default (configurable via `--namespace-prefix`).
3. `[HarmonyPatch(typeof(Foo), nameof(Foo.Method))]` works against Il2Cpp types because `Il2CppInterop.HarmonySupport` registers an `IDetourFactory` that intercepts when the target's `DeclaringType` lives in an Il2Cpp-generated assembly.

The plugin csproj will need (Phase 1.C deliverable):

```xml
<PackageReference Include="BepInEx.Unity.IL2CPP" Version="6.0.0-be.*" PrivateAssets="all" />
<PackageReference Include="BepInEx.PluginInfoProps" Version="2.*" PrivateAssets="all" />
<!-- And local <Reference> items pointing at BepInEx/interop/Il2Cpp*.dll -->
```

**OPEN QUESTION:** Whether BepInEx publishes a NuGet feed for the bleeding-edge IL2CPP libs, or whether the plugin-author must `<Reference HintPath="…/BepInEx/core/BepInEx.Unity.IL2CPP.dll" />` against the extracted zip. The Reactor docs (https://docs.reactor.gg/quick_start/install_bepinex) reference the Among Us / Reactor template flow, but it's unclear if that template is current with build 755. Plugin-author should resolve this by trying NuGet first, then falling back to file references.

---

## Summary of Action Items for Downstream Agents

### For setup-engineer (Phase 1.B)

1. Download `https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.755+3fab71a.zip`.
2. Extract next to `MTGA.app` in the Steam install.
3. Run the **5-step bootstrap** in §5: `xattr -dr com.apple.quarantine .` → `codesign --force --sign -` on every `.dylib` → set `executable_name="MTGA.app"` in `run_bepinex.sh` → `chmod +x` → launch from Terminal.
4. On first launch, **do NOT install any plugin yet**. Confirm:
   - `BepInEx/LogOutput.txt` is created (this is the smoke test — if missing, Doorstop didn't inject).
   - `BepInEx/cpp2il_out/` populates with dummy assemblies.
   - `BepInEx/interop/` populates with proxy assemblies and `assembly-hash.txt`.
   - The game window actually opens (i.e., we didn't hit the `0x80131506` `FindClassInit` crash).
5. If first launch fails at `FindClassInit`, capture the log and prepare to fork Il2CppInterop with the §4 Ghidra signature patch. Set `Logging.UnityLogListening = false` in `BepInEx/config/BepInEx.cfg` as a partial workaround.
6. `lipo -info libdoorstop.dylib` and report whether the dylib is universal or x86_64-only.

### For plugin-author (Phase 1.C)

1. Use the §6 BasePlugin template.
2. Reference the generated `BepInEx/interop/*.dll` files via local `<Reference>` items.
3. Pin HarmonyX **2.10.2** in any direct package reference (matches what BepInEx 6 build 755 ships).
4. Test plan: write a `BasePlugin` that just logs from `Load()`, drop it in `BepInEx/plugins/`, confirm the `LogOutput.txt` line. Then add a single `[HarmonyPrefix]` on something inert (e.g., `Il2Cpp.UnityEngine.Application.Quit`) and verify it fires.

### Risk register

- **HIGH:** Il2CppInterop's macOS module-locating limitation (issue #206) is likely to bite us during class injection or `DelegateSupport.ConvertDelegate` calls. If our plugin only does **method patches on existing IL2CPP classes** (no class injection, no managed→il2cpp delegate conversion), we have a chance. Anything that calls `ClassInjector.RegisterTypeInIl2Cpp` is a coin-flip.
- **HIGH:** The `0x80131506` crash on metadata v31 + macOS. Affects the *exact* Unity version MTGA uses. Mitigation: the Ghidra signature in §4 + Il2CppInterop PR #262.
- **MEDIUM:** Cpp2IL on universal mach-o. Per MelonLoader PR #900, this was fixed in Cpp2IL 2022.1.0-pre-release.20 and the bundled `Samboy063.Cpp2IL.Core 2022.1.0-development.1452` postdates it. Should be OK, but be ready to swap in pre-release.21 if processing fails.
- **MEDIUM:** Steam-launch env-var stripping on macOS. Phase 1 should always terminal-launch.
- **LOW:** Apple Silicon native ARM64 support. We're running x86_64 under Rosetta 2 — that's fine for MTGA because GameAssembly is universal. Native arm64 is a Phase-3+ concern.

---

## Sources (consolidated)

### BepInEx official
- BepInEx bleeding-edge build server: https://builds.bepinex.dev/projects/bepinex_be
- BepInEx IL2CPP install docs: https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html
- BepInEx plugin tutorial: https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html
- BepInEx runtime patching docs (WIP): https://docs.bepinex.dev/master/articles/dev_guide/runtime_patching.html
- BepInEx IL2CPP shell launcher source: https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/Doorstop/run_bepinex_il2cpp.sh
- BepInEx Unity IL2CPP csproj (build 755 pinned): https://github.com/BepInEx/BepInEx/blob/3fab71a/Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj
- DeepWiki — IL2CPP Interop pipeline: https://deepwiki.com/BepInEx/BepInEx/5.2-il2cpp-interop

### Critical issues / PRs
- BepInEx #1122 (metadata v31 + signature patch): https://github.com/BepInEx/BepInEx/issues/1122
- BepInEx #1199 ("dead end" quote): https://github.com/BepInEx/BepInEx/issues/1199
- BepInEx #1096 (macOS Intel init fail): https://github.com/BepInEx/BepInEx/issues/1096
- BepInEx #1231 (Cpp2IL crash on Rust): https://github.com/BepInEx/BepInEx/issues/1231
- BepInEx #899 (Apple Silicon support): https://github.com/BepInEx/BepInEx/issues/899
- BepInEx #911 (docs out of date): https://github.com/BepInEx/BepInEx/issues/911
- Il2CppInterop #206 (macOS module blocker): https://github.com/BepInEx/Il2CppInterop/issues/206
- Il2CppInterop PR #262 (signature hardening, OPEN): https://github.com/BepInEx/Il2CppInterop/pull/262
- UnityDoorstop #61 (Apple Silicon): https://github.com/NeighTools/UnityDoorstop/issues/61
- MonoMod #90 / PR #241 (ARM64 base infra): https://github.com/MonoMod/MonoMod/pull/241
- MelonLoader PR #900 (macOS deep-dive — read this if you only read one thing): https://github.com/LavaGang/MelonLoader/pull/900

### Cpp2IL
- Cpp2IL releases: https://github.com/SamboyCoding/Cpp2IL/releases
- Latest pre-release (Feb 2026, v31-friendly): https://github.com/SamboyCoding/Cpp2IL/releases/tag/2022.1.0-pre-release.21
- Universal mach-o support commit (referenced in MelonLoader PR): https://github.com/SamboyCoding/Cpp2IL/commit/76a9b72af61f23cb932a9af2a685f4fd224341ea

### macOS security / injection background
- DYLD_INSERT_LIBRARIES deep dive: https://theevilbit.github.io/posts/dyld_insert_libraries_dylib_injection_in_macos_osx_deep_dive/
- Gatekeeper bypass overview: https://theevilbit.github.io/posts/gatekeeper_bypass_or_not_bypass/
- macOS Gatekeeper / Quarantine reference: https://hacktricks.wiki/en/macos-hardening/macos-security-and-privilege-escalation/macos-security-protections/macos-gatekeeper.html
- Steam launch options env-var bug: https://github.com/ValveSoftware/steam-for-linux/issues/5548
