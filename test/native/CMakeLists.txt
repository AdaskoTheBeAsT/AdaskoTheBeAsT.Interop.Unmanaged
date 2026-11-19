cmake_minimum_required(VERSION 3.20)
project(unmanaged_fixture LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(fixture SHARED fixture.c)
add_library(fixture_dependency SHARED dependency.c)
add_library(fixture_dependent SHARED dependent.c)
target_link_libraries(fixture_dependent PRIVATE fixture_dependency)

add_library(isoleaf0 SHARED iso_leaf.c)
add_library(isochild SHARED iso_child.c)
target_link_libraries(isochild PRIVATE isoleaf0)

add_library(isodelay SHARED iso_delay.c)
add_library(isoparent SHARED iso_parent.c)
target_link_libraries(isoparent PRIVATE isochild)

add_library(isounused SHARED iso_unused.c)
add_library(isocompact SHARED iso_compact.c)
target_link_libraries(isocompact PRIVATE isoleaf0 isounused)

add_library(isohookdep SHARED iso_hook_dep.c)
if(WIN32 AND MSVC)
  target_sources(isohookdep PRIVATE iso_hook_dep.def)
endif()

add_library(isohook SHARED iso_hook.c)
target_link_libraries(isohook PRIVATE isohookdep)

add_executable(isohelper iso_helper.c)

if(WIN32 AND MSVC)
  target_link_libraries(isoparent PRIVATE isodelay delayimp)
  target_link_options(isoparent PRIVATE "/DELAYLOAD:isodelay.dll")
endif()

if(APPLE)
  set_target_properties(fixture_dependent isochild isoparent isocompact isohook PROPERTIES BUILD_WITH_INSTALL_RPATH TRUE INSTALL_RPATH "@loader_path")
elseif(UNIX)
  set_target_properties(fixture_dependent isochild isoparent isocompact isohook PROPERTIES BUILD_WITH_INSTALL_RPATH TRUE INSTALL_RPATH "$ORIGIN")
endif()

if(WIN32 AND CMAKE_SIZEOF_VOID_P EQUAL 4 AND MSVC)
  target_sources(fixture PRIVATE fixture-x86.def)
endif()

if(NOT WIN32)
  find_package(Threads REQUIRED)
  target_link_libraries(fixture PRIVATE Threads::Threads)
endif()

foreach(target fixture fixture_dependency fixture_dependent isoleaf0 isochild isodelay isoparent isounused isocompact isohookdep isohook isohelper)
  if(MSVC)
    target_compile_options(${target} PRIVATE /W4 /WX)
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Werror)
  endif()
endforeach()
