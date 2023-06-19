#include <windows.h>

#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>

#include "host.h"

struct ruptura_host_
{
    HMODULE hostfxr;
    hostfxr_handle handle;
    hostfxr_initialize_for_dotnet_command_line_fn initialize_fn;
    hostfxr_get_runtime_delegate_fn get_delegate_fn;
    get_function_pointer_fn get_function_fn;
    hostfxr_run_app_fn run_app_fn;
    hostfxr_close_fn close_fn;
};

uint32_t ruptura_host_new(ruptura_host *nullable *nonnull host)
{
    assert(host);

    ruptura_host *ptr = calloc(1, sizeof(ruptura_host));

    if (ptr)
        *host = ptr;

    return ptr ? 0 : 1;
}

uint32_t ruptura_host_initialize(ruptura_host *nonnull host, const wchar_t *nonnull *nonnull argv, uint32_t argc)
{
    assert(host);
    assert(argv);
    assert(argc);

    size_t buffer_size = MAX_PATH;
    wchar_t *buffer = malloc(buffer_size * sizeof(wchar_t));

    if (!buffer)
        return 1;

    struct get_hostfxr_parameters hostfxr_params =
    {
        .size = sizeof(struct get_hostfxr_parameters),
        .assembly_path = argv[0],
    };

    uint32_t rc;

    while ((rc = (uint32_t)get_hostfxr_path(buffer, &buffer_size, &hostfxr_params)))
    {
        if (rc == 0x80008098) // HostApiBufferTooSmall
        {
            wchar_t *new_buffer = realloc(buffer, buffer_size * sizeof(wchar_t));

            if (!new_buffer)
            {
                free(buffer);

                return 1;
            }

            buffer = new_buffer;
        }
        else
        {
            free(buffer);

            return rc;
        }
    }

    HMODULE hostfxr = LoadLibrary(buffer);

    free(buffer);

    if (!hostfxr)
        return GetLastError();

    host->hostfxr = hostfxr;

    if (!(host->initialize_fn = (hostfxr_initialize_for_dotnet_command_line_fn)GetProcAddress(
        host->hostfxr, "hostfxr_initialize_for_dotnet_command_line")))
        return GetLastError();

    if (!(host->run_app_fn = (hostfxr_run_app_fn)GetProcAddress(host->hostfxr, "hostfxr_run_app")))
        return GetLastError();

    if (!(host->get_delegate_fn = (hostfxr_get_runtime_delegate_fn)GetProcAddress(
        host->hostfxr, "hostfxr_get_runtime_delegate")))
        return GetLastError();

    if (!(host->close_fn = (hostfxr_close_fn)GetProcAddress(host->hostfxr, "hostfxr_close")))
        return GetLastError();

    struct hostfxr_initialize_parameters initialize_params =
    {
        .size = sizeof(struct hostfxr_initialize_parameters),
        .host_path = argv[0],
    };

    if ((rc = (uint32_t)host->initialize_fn((int32_t)argc, argv, &initialize_params, &host->handle)))
        return rc;

    return (uint32_t)host->get_delegate_fn(host->handle, hdt_get_function_pointer, (void **)&host->get_function_fn);
}

uint32_t ruptura_host_call(
    ruptura_host *nonnull host,
    const wchar_t *nonnull type_name,
    const wchar_t *nonnull method_name,
    void *nullable parameter)
{
    assert(host);
    assert(type_name);
    assert(method_name);

    uint32_t (*func)(void *parameter);

    uint32_t rc;

    if ((rc = (uint32_t)host->get_function_fn(
        type_name, method_name, UNMANAGEDCALLERSONLY_METHOD, nullptr, nullptr, (void **)&func)))
        return rc;

    return func(parameter);
}

uint32_t ruptura_host_run(ruptura_host *nonnull host)
{
    assert(host);

    return (uint32_t)host->run_app_fn(host->handle);
}

uint32_t ruptura_host_free(ruptura_host *nonnull host)
{
    assert(host);

    uint32_t rc;

    if (host->handle && (rc = (uint32_t)host->close_fn(host->handle)))
        return rc;

    if (host->hostfxr && !FreeLibrary(host->hostfxr))
        return GetLastError();

    free(host);

    return 0;
}
