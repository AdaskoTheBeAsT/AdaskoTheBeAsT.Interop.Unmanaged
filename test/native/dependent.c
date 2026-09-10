#include "fixture.h"
#ifdef _WIN32
__declspec(dllimport)
#endif
int32_t CDECL fixture_dependency_value(void);
API int32_t CDECL fixture_answer(void) { return fixture_dependency_value(); }
