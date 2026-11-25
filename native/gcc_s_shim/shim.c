/* Marker symbol so the host can check which shim build it loaded. */
__attribute__((visibility("default"))) const char *mm_gcc_s_shim_version(void)
{
    return "TMLDroid mm_gcc_s_shim 0.1 (LLVM libunwind, SONAME libgcc_s.so.1)";
}
