#include <windows.h>

#include "helpers.h"

#include <dbghelp.h>

void ruptura_helper_extract_context(
    void *nonnull context,
    void *nullable *nonnull ip,
    void *nullable *nonnull sp,
    void *nullable *nonnull fp)
{
    assert(context);
    assert(ip);
    assert(sp);
    assert(fp);

    CONTEXT *ctx = (CONTEXT *)context;

#if defined(ZIG_BIT_64)
    *ip = (void *)ctx->Rip;
    *sp = (void *)ctx->Rsp;
    *fp = (void *)ctx->Rbp;
#else
    *ip = (void *)ctx->Eip;
    *sp = (void *)ctx->Esp;
    *fp = (void *)ctx->Ebp;
#endif
}
