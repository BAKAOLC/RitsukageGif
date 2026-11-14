#include "portal-scroll-input.h"

int rgc_portal_scroll_input_send(GDBusConnection *bus, const char *session, uint32_t granted_devices,
                                 uint64_t generation, uint32_t stream_id,
                                 const struct rgc_portal_pointer_observation *observation, int region_x,
                                 int region_y, int region_width, int region_height, uint32_t axis, int steps,
                                 GCancellable *cancellable, GError **error)
{
    if (g_cancellable_set_error_if_cancelled(cancellable, error))
        return RGC_SCROLL_UNAVAILABLE;
    if (!(granted_devices & 2)) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_PERMISSION_DENIED,
                            "Pointer control was not authorized for this capture session.");
        return RGC_SCROLL_UNAVAILABLE;
    }
    if (!bus || !session || !g_variant_is_object_path(session) || !observation ||
        observation->generation != generation || observation->stream_id != stream_id || stream_id == 0 ||
        region_width <= 0 || region_height <= 0 || region_x < 0 || region_y < 0 ||
        (int64_t)region_x + region_width > observation->width ||
        (int64_t)region_y + region_height > observation->height || axis > 1 || (steps != -1 && steps != 1)) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_INVALID_ARGUMENT,
                            "The authorized capture source or scroll region changed.");
        return RGC_SCROLL_UNAVAILABLE;
    }
    gint64 age = g_get_monotonic_time() - observation->observed_monotonic_us;
    if (!observation->cursor_valid || observation->observed_monotonic_us == 0 || age < 0 || age > 250000) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_NOT_SUPPORTED,
                            "There is no current pointer observation for the authorized capture source.");
        return RGC_SCROLL_UNAVAILABLE;
    }
    if (observation->cursor_x < region_x || observation->cursor_y < region_y ||
        (int64_t)observation->cursor_x >= (int64_t)region_x + region_width ||
        (int64_t)observation->cursor_y >= (int64_t)region_y + region_height)
        return RGC_SCROLL_POINTER_OUTSIDE;
    GVariantBuilder options;
    g_variant_builder_init(&options, G_VARIANT_TYPE_VARDICT);
    GVariant *reply =
        g_dbus_connection_call_sync(bus, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
                                    "org.freedesktop.portal.RemoteDesktop", "NotifyPointerAxisDiscrete",
                                    g_variant_new("(oa{sv}ui)", session, &options, axis, steps),
                                    G_VARIANT_TYPE_UNIT, G_DBUS_CALL_FLAGS_NONE, 100, cancellable, error);
    if (!reply)
        return error && *error &&
                       (g_error_matches(*error, G_IO_ERROR, G_IO_ERROR_PERMISSION_DENIED) ||
                        g_error_matches(*error, G_DBUS_ERROR, G_DBUS_ERROR_ACCESS_DENIED))
                   ? RGC_SCROLL_UNAVAILABLE
                   : RGC_SCROLL_FAILED;
    g_variant_unref(reply);
    return RGC_SCROLL_SENT;
}

int rgc_portal_scroll_input_prepare(GDBusConnection *bus, const char *session, uint32_t granted_devices,
                                    uint64_t generation, uint32_t stream_id,
                                    const struct rgc_portal_pointer_observation *observation,
                                    int source_width, int source_height, int region_x, int region_y,
                                    int region_width, int region_height, GCancellable *cancellable,
                                    GError **error)
{
    if (g_cancellable_set_error_if_cancelled(cancellable, error))
        return RGC_SCROLL_UNAVAILABLE;
    if (!(granted_devices & 2)) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_PERMISSION_DENIED,
                            "Pointer control was not authorized for this capture session.");
        return RGC_SCROLL_UNAVAILABLE;
    }
    if (!bus || !session || !g_variant_is_object_path(session) || !observation || stream_id == 0 ||
        observation->generation != generation || observation->stream_id != stream_id ||
        source_width <= 0 || source_height <= 0 || source_width != observation->width ||
        source_height != observation->height || region_x < 0 || region_y < 0 || region_width <= 0 ||
        region_height <= 0 || (int64_t)region_x + region_width > source_width ||
        (int64_t)region_y + region_height > source_height) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_INVALID_ARGUMENT,
                            "The authorized capture source or preparation region changed.");
        return RGC_SCROLL_UNAVAILABLE;
    }
    gint64 age = g_get_monotonic_time() - observation->observed_monotonic_us;
    if (observation->cursor_valid && observation->observed_monotonic_us > 0 && age >= 0 && age <= 250000 &&
        observation->cursor_x == region_x + region_width / 2 &&
        observation->cursor_y == region_y + region_height / 2)
        return RGC_SCROLL_SENT;
    if (observation->logical_width <= 0 || observation->logical_height <= 0) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_NOT_SUPPORTED,
                            "The portal did not provide a logical coordinate size for this stream.");
        return RGC_SCROLL_UNAVAILABLE;
    }
    double x = (region_x + region_width / 2) * (double)observation->logical_width / source_width;
    double y = (region_y + region_height / 2) * (double)observation->logical_height / source_height;
    GVariantBuilder options;
    g_variant_builder_init(&options, G_VARIANT_TYPE_VARDICT);
    GVariant *reply = g_dbus_connection_call_sync(
        bus, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
        "org.freedesktop.portal.RemoteDesktop", "NotifyPointerMotionAbsolute",
        g_variant_new("(oa{sv}udd)", session, &options, stream_id, x, y), G_VARIANT_TYPE_UNIT,
        G_DBUS_CALL_FLAGS_NONE, 100, cancellable, error);
    if (!reply)
        return error && *error &&
                       (g_error_matches(*error, G_IO_ERROR, G_IO_ERROR_PERMISSION_DENIED) ||
                        g_error_matches(*error, G_DBUS_ERROR, G_DBUS_ERROR_ACCESS_DENIED))
                   ? RGC_SCROLL_UNAVAILABLE
                   : RGC_SCROLL_FAILED;
    g_variant_unref(reply);
    return RGC_SCROLL_SENT;
}
