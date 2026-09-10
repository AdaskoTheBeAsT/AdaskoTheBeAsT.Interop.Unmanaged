cmake_minimum_required(VERSION 3.20)
project(unmanaged_fixture LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(fixture SHARED fixture.c)
add_library(fixture_dependency SHARED dependency.c)
add_library(fixture_dependent SHARED dependent.c)
target_link_libraries(fixture_dependent PRIVATE fixture_dependency)

if(APPLE)
  set_target_properties(fixture_dependent PROPERTIES BUILD_WITH_INSTALL_RPATH TRUE INSTALL_RPATH "@loader_path")
elseif(UNIX)
  set_target_properties(fixture_dependent PROPERTIES BUILD_WITH_INSTALL_RPATH TRUE INSTALL_RPATH "$ORIGIN")
endif()

if(WIN32 AND CMAKE_SIZEOF_VOID_P EQUAL 4 AND MSVC)
  target_sources(fixture PRIVATE fixture-x86.def)
endif()

if(NOT WIN32)
  find_package(Threads REQUIRED)
  target_link_libraries(fixture PRIVATE Threads::Threads)
endif()

foreach(target fixture fixture_dependency fixture_dependent)
  if(MSVC)
    target_compile_options(${target} PRIVATE /W4 /WX)
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Werror)
  endif()
endforeach()
