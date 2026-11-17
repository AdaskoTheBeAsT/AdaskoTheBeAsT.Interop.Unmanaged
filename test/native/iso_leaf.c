#include "fixture.h"
#include <stddef.h>

static int32_t leaf_state = 100;
static int32_t leaf_calls;

#ifdef _WIN32
#include <windows.h>
static volatile LONG *detach_cursor;
static int32_t *detach_slots;

API void CDECL iso_leaf_set_detach_recorder(volatile long *cursor, int32_t *slots)
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
            detach_slots[idx] = 1;
        }
    }

    return TRUE;
}
#endif

API int32_t CDECL iso_leaf_add_and_get(int32_t delta)
{
    leaf_state += delta;
    ++leaf_calls;
    return leaf_state;
}

API int32_t CDECL iso_leaf_get_state(void)
{
    return leaf_state;
}

API int32_t CDECL iso_leaf_get_calls(void)
{
    return leaf_calls;
}

API int32_t CDECL iso_sym_primary(void)
{
    return 111;
}

API int32_t CDECL iso_sym_alt(void)
{
    return 222;
}

API int32_t CDECL iso_sym_same_ln(void)
{
    return 333;
}
