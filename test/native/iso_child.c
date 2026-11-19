#include "fixture.h"
#include <stddef.h>

#ifdef _WIN32
#define IMPORT __declspec(dllimport)
#else
#define IMPORT
#endif

IMPORT int32_t CDECL iso_leaf_add_and_get(int32_t delta);
IMPORT int32_t CDECL iso_leaf_get_state(void);
IMPORT int32_t CDECL iso_sym_primary(void);

static int32_t child_state = 1000;

#ifdef _WIN32
#include <windows.h>
static volatile LONG *detach_cursor;
static int32_t *detach_slots;

API void CDECL iso_child_set_detach_recorder(volatile long *cursor, int32_t *slots)
{
    detach_cursor = (volatile LONG *)cursor;
    detach_slots = slots;
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)hinstDLL;
    if (fdwReason == DLL_PROCESS_DETACH && lpvReserved == NULL && detach_cursor != NULL && detach_slots != NULL)
    {
        LONG idx = InterlockedIncrement(detach_cursor) - 1;
        if (idx >= 0 && idx < 16)
        {
            detach_slots[idx] = 2;
        }
    }

    return TRUE;
}
#endif

API int32_t CDECL iso_child_step(int32_t delta)
{
    child_state += delta * 10;
    return child_state + iso_leaf_add_and_get(delta);
}

API int32_t CDECL iso_child_get_state(void)
{
    return child_state;
}

API int32_t CDECL iso_child_get_leaf_state(void)
{
    return iso_leaf_get_state();
}

API int32_t CDECL iso_child_call_symbol(void)
{
    return iso_sym_primary();
}
