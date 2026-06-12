# Rosetta 2 inline-hook isolation tests

These reproduce, in seconds, the finding that **Dobby inline hooks do not fire under
Rosetta 2** on Apple Silicon — the blocker that stops HarmonyX patches (and BepInEx's
own plugin-load trigger) from working even though BepInEx loads and generates interop.

Build + run (must be x86_64 / Rosetta):

    clang -arch x86_64 -O0 -o dobbytestA dobbytestA.c
    arch -x86_64 ./dobbytestA      # hook applied, DobbyHook returns 0, but victim still returns original

- `dobbytestA.c` — hooks a local function *before its first call*; hook still never fires
  (Rosetta translates code pages eagerly; the patch is invisible to the cached translation).
- `dobbytest3.c` — hooks a function in a code-signed dylib (faithful to GameAssembly.dylib);
  tries `sys_icache_invalidate` and a `vm_protect` page-protection toggle. Neither makes the
  hook fire; the vm_protect toggle on the live code page crashes.

Both link against the BepInEx-bundled `libdobby.dylib`. Expectation if Rosetta weren't the
problem: the hooked call returns original+1000. Observed: it returns the original value.
