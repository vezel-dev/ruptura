#pragma once

typedef struct
{
    // Keep in sync with src/injection/AssemblyInjector.cs.

    size_t size;
    const wchar_t *nonnull *nonnull argument_vector;
    uint32_t argument_count;
    uint32_t injector_process_id;
    uint32_t main_thread_id;
} ruptura_parameters;

RUPTURA_API uint32_t ruptura_main(ruptura_parameters *nonnull parameters);
