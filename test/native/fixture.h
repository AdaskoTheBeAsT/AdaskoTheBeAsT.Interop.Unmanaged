#ifndef UNMANAGED_FIXTURE_H
#define UNMANAGED_FIXTURE_H
#include <stdint.h>

#ifdef _WIN32
#define API __declspec(dllexport)
#define CDECL __cdecl
#define STDCALL __stdcall
#else
#define API __attribute__((visibility("default")))
#define CDECL
#define STDCALL
#endif

typedef int32_t (CDECL *callback_t)(int32_t);
typedef int32_t (STDCALL *stdcall_callback_t)(int32_t);
typedef struct pair_t { int32_t first; int32_t second; } pair_t;
#endif
