#include <stdio.h>
#include <dlfcn.h>
#include <libkern/OSCacheControl.h>
#include <mach/mach.h>
#include <unistd.h>
typedef int (*DobbyHook_t)(void*, void*, void**);
typedef int (*vicfn)(int);
static vicfn origV=0; static vicfn realV=0;
int hookedV(int x){ return origV?origV(x)+1000:9999; }
int main(){
    setbuf(stdout,NULL);
    void* hd=dlopen("/Users/rexl/Projects/MTGA-Enhancement-Suite/mac-spike/staging/BepInEx/core/libdobby.dylib",RTLD_NOW);
    DobbyHook_t DobbyHook=(DobbyHook_t)dlsym(hd,"DobbyHook");
    void* hv=dlopen("/tmp/libvic.dylib",RTLD_NOW);
    realV=(vicfn)dlsym(hv,"vic");
    printf("before: vic(5)=%d\n", realV(5));            // translate
    int r=DobbyHook((void*)realV,(void*)hookedV,(void**)&origV);
    printf("hook rc=%d; after hook (no flush): vic(5)=%d (expect 1006)\n", r, realV(5));
    // candidate fix: vm_protect toggle RX->RWX->RX style to force Rosetta re-translate
    long pg=getpagesize(); void* page=(void*)((unsigned long)realV & ~(pg-1));
    kern_return_t k = vm_protect(mach_task_self(),(vm_address_t)page,pg,FALSE,VM_PROT_READ|VM_PROT_WRITE|VM_PROT_COPY);
    printf("vm_protect(rw,copy) rc=%d\n", k);
    if(k==0){ vm_protect(mach_task_self(),(vm_address_t)page,pg,FALSE,VM_PROT_READ|VM_PROT_EXECUTE); }
    sys_icache_invalidate((void*)realV,64);
    printf("after toggle+flush: vic(5)=%d (expect 1006)\n", realV(5));
    return 0;
}
