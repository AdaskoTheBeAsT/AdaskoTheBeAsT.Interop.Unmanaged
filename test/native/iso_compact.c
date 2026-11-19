#include "fixture.h"

#ifdef _WIN32
#define IMPORT __declspec(dllimport)
#else
#define IMPORT
#endif

IMPORT int32_t CDECL iso_leaf_add_and_get(int32_t delta);
IMPORT int32_t CDECL iso_unused_value(void);

API int32_t CDECL iso_compact_compute(int32_t delta)
{
    return 5000 + iso_leaf_add_and_get(delta);
}

API int32_t CDECL iso_compact_dead_unused_ref(void)
{
    return iso_unused_value();
}
