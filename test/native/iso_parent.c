#include "fixture.h"
#include <stddef.h>

#ifdef _WIN32
#define IMPORT __declspec(dllimport)
#else
#define IMPORT
#endif

IMPORT int32_t CDECL iso_child_step(int32_t delta);
IMPORT int32_t CDECL iso_child_get_state(void);
IMPORT int32_t CDECL iso_child_get_leaf_state(void);

#if defined(_WIN32) && defined(_MSC_VER)
IMPORT int32_t CDECL iso_delay_add_and_get(int32_t delta);
IMPORT int32_t CDECL iso_delay_get_state(void);
#endif

static int32_t parent_state = 10000;
static char iso_ascii_tag[16] = "ORIG_TAG_000000";
static uint16_t iso_utf16_tag[9] = { 'W', 'K', 'R', '_', 'O', 'R', 'I', 'G', 0 };
static char iso_dyn_target_name[16] = "isoleaf0.dll";
static uint8_t iso_short_string_tag[16] = {
    12, 'A', 'P', 'P', 'C', 'O', 'N', 'F', 'I', 'G', '.', 'I', 'N', 0, 0, 0
};

#ifdef _WIN32
#include <windows.h>
static volatile LONG *detach_cursor;
static int32_t *detach_slots;

API void CDECL iso_parent_set_detach_recorder(volatile long *cursor, int32_t *slots)
{
    detach_cursor = (volatile LONG *)cursor;
    detach_slots = slots;
}

API void *CDECL iso_parent_query_loaded_dyn_target(void)
{
    return (void *)GetModuleHandleA(iso_dyn_target_name);
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)hinstDLL;
    if (fdwReason == DLL_PROCESS_DETACH && lpvReserved == NULL && detach_cursor != NULL && detach_slots != NULL)
    {
        LONG idx = InterlockedIncrement(detach_cursor) - 1;
        if (idx >= 0 && idx < 16)
        {
            detach_slots[idx] = 3;
        }
    }

    return TRUE;
}
#endif

API int32_t CDECL iso_parent_step(int32_t delta)
{
    parent_state += delta * 100;
    return parent_state + iso_child_step(delta);
}

API int32_t CDECL iso_parent_get_state(void)
{
    return parent_state;
}

API int32_t CDECL iso_parent_get_child_state(void)
{
    return iso_child_get_state();
}

API int32_t CDECL iso_parent_get_leaf_state(void)
{
    return iso_child_get_leaf_state();
}

API const char *CDECL iso_parent_get_ascii_tag_ptr(void)
{
    return iso_ascii_tag;
}

API const uint16_t *CDECL iso_parent_get_utf16_tag_ptr(void)
{
    return iso_utf16_tag;
}

API const char *CDECL iso_parent_get_dyn_name_ptr(void)
{
    return iso_dyn_target_name;
}

API const uint8_t *CDECL iso_parent_get_short_string_ptr(void)
{
    return iso_short_string_tag;
}

#if defined(_WIN32) && defined(_MSC_VER)
API int32_t CDECL iso_parent_call_delay(int32_t delta)
{
    return iso_delay_add_and_get(delta);
}

API int32_t CDECL iso_parent_get_delay_state(void)
{
    return iso_delay_get_state();
}
#endif
