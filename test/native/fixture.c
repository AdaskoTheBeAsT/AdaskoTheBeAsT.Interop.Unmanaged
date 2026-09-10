#include "fixture.h"
#include <stddef.h>

#ifdef _WIN32
#include <windows.h>
static SRWLOCK mutex = SRWLOCK_INIT;
static CONDITION_VARIABLE changed = CONDITION_VARIABLE_INIT;
#define LOCK() AcquireSRWLockExclusive(&mutex)
#define UNLOCK() ReleaseSRWLockExclusive(&mutex)
#define WAIT() SleepConditionVariableSRW(&changed, &mutex, INFINITE, 0)
#define WAKE() WakeAllConditionVariable(&changed)
#else
#include <pthread.h>
static pthread_mutex_t mutex = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t changed = PTHREAD_COND_INITIALIZER;
#define LOCK() pthread_mutex_lock(&mutex)
#define UNLOCK() pthread_mutex_unlock(&mutex)
#define WAIT() pthread_cond_wait(&changed, &mutex)
#define WAKE() pthread_cond_broadcast(&changed)
#endif

static callback_t registered;
static int active;
static int released;
static int32_t counter;

API int32_t CDECL fixture_add(int32_t a, int32_t b) { return a + b; }
API int32_t STDCALL fixture_add_stdcall(int32_t a, int32_t b) { return a + b; }
API void CDECL fixture_increment(int32_t *value) { ++*value; }
API void *CDECL fixture_echo(void *value) { return value; }
API pair_t CDECL fixture_pair(pair_t value) { ++value.first; ++value.second; return value; }
API int32_t CDECL fixture_counter(void) { return ++counter; }
API int32_t CDECL fixture_invoke(callback_t callback, int32_t value) { return callback(value); }
API int32_t CDECL fixture_invoke_stdcall(stdcall_callback_t callback, int32_t value) { return callback(value); }

typedef int32_t (CDECL *ref_callback_t)(int32_t *, int32_t *);
typedef uint8_t (CDECL *text_callback_t)(const uint16_t *);
API int32_t CDECL fixture_invoke_ref(ref_callback_t callback, int32_t value, int32_t *copy)
{
    return callback(&value, copy);
}

API uint8_t CDECL fixture_invoke_text(text_callback_t callback)
{
    const uint16_t text[] = { 65, 66, 0 };
    return callback(text);
}

/* Callers must serialize registration against unregistration. */
API void CDECL fixture_register(callback_t callback)
{
    LOCK();
    registered = callback;
    UNLOCK();
}

API int32_t CDECL fixture_registered(void)
{
    int32_t result;
    LOCK();
    result = registered != NULL;
    UNLOCK();
    return result;
}

API int32_t CDECL fixture_invoke_registered(int32_t value)
{
    callback_t callback;
    int32_t result;
    LOCK();
    callback = registered;
    if (callback == NULL) { UNLOCK(); return -1; }
    ++active;
    UNLOCK();
    result = callback(value);
    LOCK();
    --active;
    WAKE();
    UNLOCK();
    return result;
}

/* On return no in-flight callback remains. Do not call from inside a callback. */
API void CDECL fixture_unregister(void)
{
    LOCK();
    registered = NULL;
    while (active != 0) { WAIT(); }
    UNLOCK();
}

API void CDECL fixture_block(callback_t entered)
{
    entered(0);
    LOCK();
    while (!released) { WAIT(); }
    released = 0;
    UNLOCK();
}

API void CDECL fixture_release(void)
{
    LOCK();
    released = 1;
    WAKE();
    UNLOCK();
}
