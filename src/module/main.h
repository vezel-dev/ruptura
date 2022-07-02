#pragma once

typedef struct
{
    const wchar_t *nonnull *nonnull argv;
    uint32_t argc;
    uint32_t injector_process_id;
} ruptura_parameters;

__declspec(dllexport) uint32_t __stdcall ruptura_main(ruptura_parameters *nonnull parameters);
