#include "fixture.h"
#include <stddef.h>

#ifdef _WIN32
#define IMPORT __declspec(dllimport)
#else
#define IMPORT
#endif

IMPORT int32_t CDECL iso_hook_dep_calc(int32_t value);
IMPORT int32_t CDECL iso_hook_dep_ord(int32_t value);

static int32_t hook_data_scratch[4] = { 11, 22, 33, 44 };
static int32_t hook_bss_scratch[4];
static const uint8_t hook_readonly_magic[8] = { 'R', 'D', 'A', 'T', 'A', '_', 'V', '1' };
static int32_t boot_cfg_loaded;
static char boot_cfg_text[128];

#ifdef _WIN32
#include <windows.h>

static void try_load_boot_cfg(HINSTANCE hinstDLL)
{
    WCHAR module_path[2048];
    static const WCHAR file_name[] = L"iso_boot.cfg";
    DWORD len = GetModuleFileNameW(hinstDLL, module_path, 2000);
    DWORD last_sep = 0;
    int has_sep = 0;
    DWORD i;
    HANDLE hfile;
    DWORD bytes_read = 0;

    if (len == 0 || len >= 2000)
    {
        return;
    }

    for (i = 0; i < len; ++i)
    {
        if (module_path[i] == L'\\' || module_path[i] == L'/')
        {
            last_sep = i;
            has_sep = 1;
        }
    }

    if (!has_sep || last_sep + 1 + (sizeof(file_name) / sizeof(WCHAR)) >= 2048)
    {
        return;
    }

    for (i = 0; i < sizeof(file_name) / sizeof(WCHAR); ++i)
    {
        module_path[last_sep + 1 + i] = file_name[i];
    }

    hfile = CreateFileW(
        module_path,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    if (hfile == INVALID_HANDLE_VALUE)
    {
        return;
    }

    if (ReadFile(hfile, boot_cfg_text, (DWORD)(sizeof(boot_cfg_text) - 1), &bytes_read, NULL))
    {
        boot_cfg_text[bytes_read] = '\0';
        boot_cfg_loaded = 1;
    }

    CloseHandle(hfile);
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)lpvReserved;
    if (fdwReason == DLL_PROCESS_ATTACH)
    {
        try_load_boot_cfg(hinstDLL);
    }

    return TRUE;
}
#endif

API int32_t CDECL iso_hook_call_named(int32_t value)
{
    return iso_hook_dep_calc(value);
}

API int32_t CDECL iso_hook_call_ordinal(int32_t value)
{
    return iso_hook_dep_ord(value);
}

API int32_t *CDECL iso_hook_get_data_scratch_ptr(void)
{
    return hook_data_scratch;
}

API void CDECL iso_hook_write_data_scratch(int32_t a, int32_t b, int32_t c, int32_t d)
{
    hook_data_scratch[0] = a;
    hook_data_scratch[1] = b;
    hook_data_scratch[2] = c;
    hook_data_scratch[3] = d;
}

API int32_t *CDECL iso_hook_get_bss_scratch_ptr(void)
{
    return hook_bss_scratch;
}

API void CDECL iso_hook_write_bss_scratch(int32_t a, int32_t b, int32_t c, int32_t d)
{
    hook_bss_scratch[0] = a;
    hook_bss_scratch[1] = b;
    hook_bss_scratch[2] = c;
    hook_bss_scratch[3] = d;
}

API const uint8_t *CDECL iso_hook_get_readonly_magic_ptr(void)
{
    return hook_readonly_magic;
}

API int32_t CDECL iso_hook_sum_readonly_magic(void)
{
    volatile const uint8_t *ptr = hook_readonly_magic;
    int32_t sum = 0;
    size_t i;
    for (i = 0; i < sizeof(hook_readonly_magic); ++i)
    {
        sum += ptr[i];
    }

    return sum;
}

API int32_t CDECL iso_hook_was_boot_cfg_loaded(void)
{
    return boot_cfg_loaded;
}

API const char *CDECL iso_hook_get_boot_cfg_ptr(void)
{
    return boot_cfg_text;
}
