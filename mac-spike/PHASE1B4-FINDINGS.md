# Phase 1.B.4 — Xref-scanner fix lands; new root blocker is Rosetta 2 inline hooking

**Date:** 2026-06-12
**Result:** 🟡 **MAJOR PROGRESS, then a deeper blocker.** The Phase-1 showstopper (`Pass16`
xref-scanner crash) is **fixed** — interop generation now completes and produces 179 game
proxy assemblies, and MTGA boots fully on Apple Silicon with BepInEx injected and the account
logged in. But plugin `Load()` never fires, and the cause is **inline hooks do not take effect
under Rosetta 2** — which would also block every HarmonyX patch. macOS support is unblocked at
the generation layer but blocked at the runtime-hooking layer.

---

## TL;DR

Two fixes this session, one new hard blocker:

1. **FIXED — the Phase-1 NO-GO crash.** `Il2CppInterop.Common/XrefScans/XrefScannerLowLevel.cs`
   `ExtractTargetAddress` threw `ArgumentOutOfRangeException` on the indirect call/jmp instructions
   that MTGA's LLVM-optimized `GameAssembly.dylib` emits. A ~6-line patch (skip indirect branches
   instead of throwing — exactly what the sibling `CallAndIndirectTargetsImpl` already does) makes
   `Pass16ScanMethodRefs` complete. Interop generation now finishes: **179 assemblies** in
   `BepInEx/interop/`, including `SharedClientCore.dll`, `Wizards.Arena.*`, `Wizards.Mtga.*`,
   `Wizards.MDN.GreProtobuf.dll` — the exact game assemblies the mod's patches target.
   Patch: [`patches/01-il2cppinterop-xref-scanner-llvm-fix.patch`](patches/01-il2cppinterop-xref-scanner-llvm-fix.patch).

2. **FIXED — launch architecture + Steam DRM, so MTGA actually boots under the loader.**
   See "Getting the game to boot" below. Net effect: MTGA reaches the **home page**, logged in,
   with BepInEx + the patched Il2CppInterop running underneath.

3. **NEW BLOCKER — Rosetta 2 inline hooking.** BepInEx loads plugins via a Dobby inline detour on
   `il2cpp_runtime_invoke`; that detour reports success but **never intercepts a single call**,
   even though the game booted (so the function ran thousands of times). Isolated reproductions
   confirm Dobby inline hooks simply don't fire under Rosetta 2 on Apple Silicon. Since every
   HarmonyX patch uses the same native-detour mechanism, this blocks the mod's whole reason for
   existing, not just plugin auto-load.

The prior NO-GO writeups concluded "fork Il2CppInterop's disassembler stack." That fork turned
out to be small (the xref fix above) and **works**. The real wall is one layer deeper and was not
previously identified: **Rosetta translation caching defeats inline hooks**, and you cannot escape
to native arm64 because Il2CppInterop's scanners are x86-only.

---

## What was fixed, in detail

### Fix 1 — the xref-scanner crash (the documented showstopper)

`JumpTargetsImpl` walks a method's instructions with an Iced x86_64 decoder. On a `call`/`jmp`
that isn't a short jump it called `ExtractTargetAddress`, whose `switch (instruction.Op0Kind)`
**threw in the `default` case**. The default is hit by *indirect* branches (`call rax`,
`jmp qword ptr [rip+…]`, jump tables) — common in LLVM-optimized macOS IL2CPP binaries, rare in
MSVC-compiled Windows ones (which is why this never surfaced on Windows). One throw aborted the
entire `Pass16` for all methods, leaving `BepInEx/interop/` empty.

The public `XrefScanner.ExtractTargetAddress` already returns `0` for indirect operands and its
caller skips zero. The private copy in `XrefScannerLowLevel` was simply inconsistent. The fix
makes it return `0` and skips zero targets — graceful per-method degradation (a method whose first
call is indirect just doesn't get its metadata-init optimization) instead of a whole-assembly crash.

Built from Il2CppInterop PR #262 base (`2e82b6c`, the same commit Phase 1.B.3 proved loads into
BepInEx 755) + this patch; rebuilt `Il2CppInterop.{Common,Generator,Runtime,HarmonySupport}.dll`
with .NET SDK 8.0.413 and dropped them over `BepInEx/core/`. Result:

```
[Info   :Il2CppInteropGen] Scanning method cross-references... Done in 00:00:02.4062399   ← was the crash
...
[Info   :Il2CppInteropGen] Writing assemblies... Done!
[Message:   BepInEx] Chainloader initialized
```

(Generation trace preserved in `LogOutput-xref-fix-INTEROP-GEN.txt`.)

### Fix 2 — getting the game to boot under the loader

The previous spike never confirmed a full boot. Three Apple-Silicon-specific issues had to be
solved (none were in the earlier findings):

- **Force x86_64 / Rosetta.** BepInEx's bundled CoreCLR (`dotnet/libcoreclr.dylib`) is **x86_64
  only**. `run_bepinex.sh`'s Apple-Silicon branch sets `ARCHPREFERENCE="arm64,x86_64"`, and because
  the BepInEx 755 `libdoorstop.dylib` is now *universal* (arm64+x86_64 — resolves an old open
  question), the game ran **native arm64**, where Doorstop then failed to `dlopen` the x64-only
  CoreCLR. No CLR → no preloader → no `LogOutput.log`. Fix: edit the launch script to
  `ARCHPREFERENCE="x86_64"` + `exec arch -x86_64 -e …`. Everything (game, GameAssembly's x86_64
  slice, Doorstop, CoreCLR) then runs under Rosetta 2.
- **Steam DRM.** Launched from a terminal, `SteamAPI_Init` returns false and MTGA does its own
  "Restarting via Steam" → quits during the `Coroutine_GameStartup`. Root cause: the Steam client
  wasn't running (only a leftover `ipcserver`; `SteamPID=0`). With Steam started + logged in and a
  `steam_appid.txt` (app id **2141910**) present, the Player.log shows `Steam status: Available`
  and the game proceeds.
- **`BacktraceMacUnity.bundle` is arm64-only** (the *only* non-universal component in the app).
  Under Rosetta it can't load — but this is **non-fatal**; the game logs "Cannot startup the native
  client" and continues. No action needed for boot.

After all three, MTGA boots to the home page logged in, with BepInEx underneath. This is strictly
further than Phase 1.B's "asset download" high-water mark.

---

## The new blocker — Rosetta 2 defeats inline hooks

BepInEx 6 IL2CPP loads plugins lazily: `IL2CPPChainloader.Initialize()` installs a **Dobby native
detour on `il2cpp_runtime_invoke`**; the detour `OnInvokeMethod` watches for the game invoking
`Internal_ActiveSceneChanged` and only then calls `Execute()` (which discovers `BepInEx/plugins/`
and calls each `BasePlugin.Load()`). Observed: `Chainloader initialized`, `Runtime invoke patched`
(Dobby reports success) — but **no plugin ever loads**, the game booted fully, and a hello-world
plugin's `Load()` never ran.

To distinguish "hook doesn't fire" from "trigger method never invoked," `BepInEx.Unity.IL2CPP.dll`
was rebuilt with `OnInvokeMethod` logging the first 60 method names it sees. Result:
**zero log lines** — `OnInvokeMethod` is never entered, despite `il2cpp_runtime_invoke` running
thousands of times during boot. The detour is installed but inert.

### Isolated proof (see `rosetta-hook-tests/`)

A standalone C harness links the BepInEx-bundled `libdobby.dylib` and `DobbyHook`s a trivial
function, run under `arch -x86_64`:

- Hook a function, then call it → returns the **original** value (hook didn't fire). `DobbyHook`
  returned `0` (success).
- Hook a function **before its first call** → still doesn't fire (Rosetta translates code pages
  eagerly; the patch is invisible to the cached translation).
- Adding `sys_icache_invalidate(target, 64)` after the patch → no change.
- A `vm_protect` page-protection toggle to force re-translation → crashes (SIGBUS/SIGSEGV) and
  doesn't help.

**Conclusion:** Dobby's x86_64 backend writes the patch but doesn't (cannot, simply) invalidate
Rosetta 2's translation cache. On real x86 hardware the instruction cache is coherent so no flush
is needed; under Rosetta the modified bytes are never re-read. There is **no managed-side or
page-protection fix** that makes it work. (An `sys_icache_invalidate` was added to `DobbyDetour`
anyway — [`patches/02-…`](patches/02-bepinex-dobby-icache-invalidate-arm64.patch) — because it is
correct and *required* on native arm64, even though it is insufficient under Rosetta.)

### Why we can't just run native arm64 (which would fix hooking)

Native arm64 self-modifying code + `sys_icache_invalidate` is the normal, well-supported path, so
inline hooks would fire. But:

- **Il2CppInterop's generator xref scanner is Iced/x86-only** — it cannot decode arm64. (We *use*
  this under Rosetta to generate the interop; it can't run against the arm64 GameAssembly slice.)
- **Il2CppInterop's *runtime* `InjectorHelpers` uses x86 byte-signature scanning** to locate
  il2cpp internal functions for class injection — also x86-specific.
- BepInEx ships only `macos-x64`; `libcoreclr.dylib` and `libdobby.dylib` are x86_64-only.

So the two halves are mutually exclusive with stock tooling: x86_64/Rosetta gets you generation +
loading but no working hooks; native arm64 could get working hooks but breaks generation and the
runtime injector. Bridging them is essentially MelonLoader's in-flight `osx-arm64` work
(LavaGang/MelonLoader PRs #1090, #1174, open as of June 2026), plus an arm64 build of
`libdobby.dylib`, plus making (or generating once under Rosetta and reusing) the interop assemblies.

---

## Current spike state (in `mac-spike/staging/`, gitignored)

- Stock BepInEx 755 + **xref-patched Il2CppInterop DLLs** in `BepInEx/core/` (generation works).
- Symlink shim from Phase 1.B.2 intact; `steam_appid.txt` (2141910) in place; `run_bepinex.sh`
  edited to force x86_64.
- `BepInEx/interop/` populated with 179 cached proxy assemblies (regeneration is skipped on relaunch).
- Hello-world plugin source at `mac-spike/helloworld-plugin/` (builds against the interop; loads
  only once the Rosetta-hook blocker is solved).
- `BepInEx.Unity.IL2CPP.dll` restored to stock (the diagnostic/icache build was investigative).

To reproduce generation: Steam running + logged in, then `cd mac-spike/staging && ./run_bepinex.sh`,
watch `/tmp` stdout or `BepInEx/LogOutput.log` for `Il2CppInteropGen … Done!`.

---

## Updated resume conditions

The Phase 1.B.2/B.3 resume signals (watch the xref scanner / Pass16) are now **satisfied** — that
fix is done and works locally. The blocker to watch is different:

1. **A native `macos-arm64` BepInEx 6 IL2CPP toolchain** (CoreCLR + libdoorstop + libdobby + an
   arm64-capable Il2CppInterop runtime). Track BepInEx#899 and the builds server for a
   `…-macos-arm64-…` artifact, and MelonLoader PRs #1090 / #1174.
2. **Any inline-hook-under-Rosetta breakthrough** (unlikely; it's an Apple Rosetta limitation, not
   a BepInEx bug). The cleaner path is arm64-native, above.
3. **Upstreaming the xref fix** — it's a real, Windows-safe bug fix; worth a PR to
   BepInEx/Il2CppInterop regardless (no open issue/PR touches `ExtractTargetAddress`).

If a native arm64 stack appears, the remaining port work is: generate interop once under Rosetta
(done — cache it), then run game+plugins arm64 with arm64 libdobby; the hello-world plugin already
builds against the interop and is the first thing to smoke-test.
