#include <windows.h>

#include "host.h"
#include "main.h"

typedef struct
{
    HMODULE module_handle;
    uint32_t injector_process_id;
} ruptura_state;

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

    bool expected = false;

    if (!__c11_atomic_compare_exchange_strong(&ruptura_running, &expected, true, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST))
        return 2;

    uint32_t rc;

    ruptura_host *host = nullptr;
    uint32_t argc = parameters->argc;
    wchar_t **argv = calloc(argc, sizeof(wchar_t *));

    if (!argv)
    {
        rc = 1;

        goto failure;
    }

    for (uint32_t i = 0; i < argc; i++)
    {
        const wchar_t *src = parameters->argv[i];
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

    ruptura_state state =
    {
        .module_handle = ruptura_module,
        .injector_process_id = parameters->injector_process_id,
    };

    // After this call, parameters is freed by the injector.
    if ((rc = ruptura_host_call(
        host, L"Vezel.Ruptura.Hosting.InjectedProgramContext, Vezel.Ruptura.Hosting", L"Initialize", &state)))
        goto failure;

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
