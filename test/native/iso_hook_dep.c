#include "fixture.h"

#if defined(_WIN32) && defined(_MSC_VER)
#define HOOKDEP_API
#else
#define HOOKDEP_API API
#endif

HOOKDEP_API int32_t CDECL iso_hook_dep_calc(int32_t value)
{
    return value + 10;
}

HOOKDEP_API int32_t CDECL iso_hook_dep_ord(int32_t value)
{
    return value * 3;
}
