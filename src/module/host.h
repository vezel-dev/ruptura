#pragma once

typedef struct ruptura_host_ ruptura_host;

uint32_t ruptura_host_new(ruptura_host *nullable *nonnull host);

uint32_t ruptura_host_initialize(ruptura_host *nonnull host, const wchar_t *nonnull *nonnull argv, uint32_t argc);

uint32_t ruptura_host_call(
    ruptura_host *nonnull host,
    const wchar_t *nonnull type_name,
    const wchar_t *nonnull method_name,
    void *nullable parameter);

uint32_t ruptura_host_run(ruptura_host *nonnull host);

uint32_t ruptura_host_free(ruptura_host *nonnull host);
