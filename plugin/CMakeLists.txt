cmake_minimum_required(VERSION 3.5)

if(NOT DEFINED CMAKE_TOOLCHAIN_FILE)
  if(DEFINED ENV{VITASDK})
    set(CMAKE_TOOLCHAIN_FILE "$ENV{VITASDK}/share/vita.toolchain.cmake" CACHE PATH "toolchain file")
  else()
    message(FATAL_ERROR "Please define VITASDK to point to your SDK path!")
  endif()
endif()

project(VitaPresence)
include("$ENV{VITASDK}/share/vita.cmake" REQUIRED)

set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -Wl,-q -Wall -O3 -std=gnu99")
set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -nostdlib")
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -std=c++11 -fno-rtti -fno-exceptions")

link_directories(
  ${CMAKE_CURRENT_BINARY_DIR}
)

# 1. VitaPresence target (magic = 0xCAFECAFE)
add_executable(VitaPresence src/main.c)
target_compile_definitions(VitaPresence PRIVATE VITAPRESENCE_MAGIC=0xCAFECAFE)
target_link_libraries(VitaPresence
  gcc
  taihenForKernel_stub
  taihenModuleUtils_stub
  SceThreadmgrForDriver_stub
  SceSysclibForDriver_stub
  SceNetPsForDriver_stub
  SceSblACMgrForDriver_stub
  SceSysmemForDriver_stub
  SceIofilemgrForDriver_stub
)
vita_create_self(VitaPresence.skprx VitaPresence
  UNSAFE
  CONFIG ${CMAKE_SOURCE_DIR}/VitaPresence.yml
)

# 2. PSTVPresence target (magic = 0xCAFECAFF)
add_executable(PSTVPresence src/main.c)
target_compile_definitions(PSTVPresence PRIVATE VITAPRESENCE_MAGIC=0xCAFECAFF)
target_link_libraries(PSTVPresence
  gcc
  taihenForKernel_stub
  taihenModuleUtils_stub
  SceThreadmgrForDriver_stub
  SceSysclibForDriver_stub
  SceNetPsForDriver_stub
  SceSblACMgrForDriver_stub
  SceSysmemForDriver_stub
  SceIofilemgrForDriver_stub
)
vita_create_self(PSTVPresence.skprx PSTVPresence
  UNSAFE
  CONFIG ${CMAKE_SOURCE_DIR}/VitaPresence.yml
)
