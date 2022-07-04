#pragma once

typedef struct
{
    // Keep in sync with src/injection/AssemblyInjector.cs.

    const wchar_t *nonnull *nonnull argv;
    uint32_t argc;
    uint32_t injector_process_id;
    uint32_t main_thread_id;
} ruptura_parameters;

static_assert(sizeof(ruptura_parameters) == 24);

__declspec(dllexport) uint32_t __stdcall ruptura_main(ruptura_parameters *nonnull parameters);
