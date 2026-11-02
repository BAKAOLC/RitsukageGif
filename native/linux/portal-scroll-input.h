#ifndef RGC_PORTAL_SCROLL_INPUT_H
#define RGC_PORTAL_SCROLL_INPUT_H

#include <gio/gio.h>
#include <stdint.h>

enum rgc_scroll_input_result {
    RGC_SCROLL_SENT = 0,
    RGC_SCROLL_AT_END = 1,
    RGC_SCROLL_UNAVAILABLE = 2,
    RGC_SCROLL_FAILED = 3
};

struct rgc_portal_pointer_observation {
    uint64_t generation;
    uint32_t stream_id;
    int width, height;
    int cursor_x, cursor_y;
    gboolean cursor_valid;
    gint64 observed_monotonic_us;
};

int rgc_portal_scroll_input_send(GDBusConnection *bus, const char *session, uint32_t granted_devices,
                                 uint64_t generation, uint32_t stream_id,
                                 const struct rgc_portal_pointer_observation *observation, int region_x,
                                 int region_y, int region_width, int region_height, uint32_t axis, int steps,
                                 GCancellable *cancellable, GError **error);

#endif
