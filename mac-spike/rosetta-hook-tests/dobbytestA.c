#include <stdio.h>
#include <dlfcn.h>
typedef int (*DobbyHook_t)(void*, void*, void**);
static int (*origA)(int)=0;
__attribute__((noinline,used)) int victimA(int x){ return x+1; }
int hookedA(int x){ return origA?origA(x)+1000:9999; }
int main(){
    setbuf(stdout,NULL);
    void* h=dlopen("/Users/rexl/Projects/MTGA-Enhancement-Suite/mac-spike/staging/BepInEx/core/libdobby.dylib",RTLD_NOW);
    if(!h){printf("dlopen fail\n");return 1;}
    DobbyHook_t DobbyHook=(DobbyHook_t)dlsym(h,"DobbyHook");
    int r=DobbyHook((void*)victimA,(void*)hookedA,(void**)&origA);
    printf("hook rc=%d; hook-BEFORE-first-call: victimA(5)=%d  (expect 1006 if fires)\n", r, victimA(5));
    return 0;
}
