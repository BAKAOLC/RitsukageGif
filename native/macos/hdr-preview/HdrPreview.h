#ifndef RG_HDR_PREVIEW_H
#define RG_HDR_PREVIEW_H
#include <stdint.h>
/* All entry points run on the AppKit main thread in the hosting process.
   create returns a retained NSView. NativeControlHost attaches it; destroy releases it.
   Pixels are linear-sRGB half-float RGBA. alphaMode: 0 premultiplied, 1 straight, 2 opaque. white is the source SDR
   reference white in scRGB units (1 = 80 nits), normalized to EDR UI white 1. */
void *rg_hdr_create(void (*changed)(void *context), void *context, const char *metalLibraryPath);
int32_t rg_hdr_present(void *view, const void *pixels, int32_t width, int32_t height, int32_t stride, float white, int32_t alphaMode);
float rg_hdr_headroom(void *view);
void rg_hdr_destroy(void *view);
/* Overlay parent is an NSWindow belonging to the same process. Geometry passed to
   patch/selection is normalized logical source space; pixel texture size remains independent. */
void *rg_hdr_overlay_create(void *view, void *parentNSWindow);
void rg_hdr_overlay_show(void *overlay, int32_t visible);
void rg_hdr_overlay_destroy(void *overlay);
int32_t rg_hdr_patch(void *view, const void *pixels, int32_t width, int32_t height, int32_t stride,
                     float x, float y, float w, float h, int32_t alphaMode);
void rg_hdr_selection(void *view, float x, float y, float w, float h);
#endif
