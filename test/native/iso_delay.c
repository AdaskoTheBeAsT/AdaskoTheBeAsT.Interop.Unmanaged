#include "fixture.h"

static int32_t delay_state = 5000;

API int32_t CDECL iso_delay_add_and_get(int32_t delta)
{
    delay_state += delta;
    return delay_state;
}

API int32_t CDECL iso_delay_get_state(void)
{
    return delay_state;
}
