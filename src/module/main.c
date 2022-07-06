#include <windows.h>

#include "host.h"
#include "main.h"

typedef struct
{
    // Keep in sync with src/memory/InjectedNativeModule.cs.

    size_t size;
    uint32_t injector_process_id;
    HMODULE module_handle;
} ruptura_module_parameters;

typedef struct
{
    // Keep in sync with src/hosting/InjectedProgramContext.cs.

    size_t size;
    uint32_t injector_process_id;
    uint32_t main_thread_id;
} ruptura_context_parameters;

static volatile atomic(HMODULE) ruptura_module;

static volatile atomic(bool) ruptura_running;

BOOL __stdcall DllMain(HMODULE module, uint32_t reason, void *reserved)
{
    (void)reserved;

    assert(module);

    if (reason == DLL_PROCESS_ATTACH)
        ruptura_module = module;

    return TRUE;
}

uint32_t ruptura_main(ruptura_parameters *nonnull parameters)
{
    assert(parameters);
    assert(parameters->size == sizeof(ruptura_parameters) && "Managed/unmanaged ruptura_parameters size mismatch.");

    bool expected = false;

    if (!__c11_atomic_compare_exchange_strong(&ruptura_running, &expected, true, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST))
        return 2;

    uint32_t rc;

    ruptura_host *host = nullptr;
    uint32_t argc = parameters->argument_count;
    wchar_t **argv = calloc(argc, sizeof(wchar_t *));

    if (!argv)
    {
        rc = 1;

        goto failure;
    }

    for (uint32_t i = 0; i < argc; i++)
    {
        const wchar_t *src = parameters->argument_vector[i];
        size_t len = wcslen(src) + 1;
        wchar_t *dst = malloc(len * sizeof(wchar_t));

        if (!dst)
        {
            rc = 1;

            goto failure;
        }

        if ((rc = (uint32_t)wcscpy_s(dst, len, src)))
            goto failure;

        argv[i] = dst;
    }

    if ((rc = ruptura_host_new(&host)))
        goto failure;

    if ((rc = ruptura_host_initialize(host, (const wchar_t **)argv, argc)))
        goto failure;

    ruptura_module_parameters module_params =
    {
        .size = sizeof(ruptura_module_parameters),
        .injector_process_id = parameters->injector_process_id,
        .module_handle = ruptura_module,
    };

    if ((rc = ruptura_host_call(
        host, L"Vezel.Ruptura.Memory.InjectedNativeModule, Vezel.Ruptura.Memory", L"Initialize", &module_params)))
        goto failure;

    ruptura_context_parameters context_params =
    {
        .size = sizeof(ruptura_context_parameters),
        .injector_process_id = parameters->injector_process_id,
        .main_thread_id = parameters->main_thread_id,
    };

    if ((rc = ruptura_host_call(
        host, L"Vezel.Ruptura.Hosting.InjectedProgramContext, Vezel.Ruptura.Hosting", L"Initialize", &context_params)))
        goto failure;

    // parameters will now be freed by the injector.

    if ((rc = ruptura_host_run(host)))
        goto failure;

    rc = ruptura_host_free(host);

    goto done;

failure:
    if (host)
        ruptura_host_free(host);

done:
    if (argv)
    {
        for (uint32_t i = 0; i < argc; i++)
        {
            wchar_t *str = argv[i];

            if (str)
                free(str);
        }

        free(argv);
    }

    FreeLibraryAndExitThread(ruptura_module, rc);
}
