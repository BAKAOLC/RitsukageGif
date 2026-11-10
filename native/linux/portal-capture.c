#define _POSIX_C_SOURCE 200809L
#include "portal-video.h"
#include <errno.h>
#include <fcntl.h>
#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <json-glib/json-glib.h>
#include <poll.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <unistd.h>

#define PORTAL "org.freedesktop.portal.Desktop"
#define PORTAL_PATH "/org/freedesktop/portal/desktop"
#define SCREENCAST "org.freedesktop.portal.ScreenCast"
#define REMOTE_DESKTOP "org.freedesktop.portal.RemoteDesktop"
#define FRAME_LIMIT (512u * 1024u * 1024u)

static volatile sig_atomic_t stopping;

typedef struct {
    const char *path;
    GVariant *result;
    guint status;
    gboolean received;
} PortalRequest;

static void stop_signal(int signal_number)
{
    (void)signal_number;
    stopping = 1;
}

static void response_received(GDBusConnection *connection, const gchar *sender, const gchar *path,
                              const gchar *interface, const gchar *signal_name, GVariant *parameters,
                              gpointer data)
{
    (void)connection;
    (void)sender;
    (void)interface;
    (void)signal_name;
    PortalRequest *request = data;
    if (request->received || !request->path || strcmp(request->path, path) != 0)
        return;
    if (!g_variant_is_of_type(parameters, G_VARIANT_TYPE("(ua{sv})")))
        return;
    g_variant_get(parameters, "(u@a{sv})", &request->status, &request->result);
    request->received = TRUE;
}

static void session_closed(GDBusConnection *connection, const gchar *sender, const gchar *path,
                           const gchar *interface, const gchar *signal_name, GVariant *parameters,
                           gpointer data)
{
    (void)connection;
    (void)sender;
    (void)path;
    (void)interface;
    (void)signal_name;
    (void)parameters;
    (void)data;
    stopping = 1;
}

static void close_portal_object(GDBusConnection *bus, const char *path, const char *interface)
{
    if (!path || g_dbus_connection_is_closed(bus))
        return;
    GVariant *reply = g_dbus_connection_call_sync(bus, PORTAL, path, interface, "Close", NULL, NULL,
                                                  G_DBUS_CALL_FLAGS_NONE, 2000, NULL, NULL);
    if (reply)
        g_variant_unref(reply);
}

static GVariant *portal_request(GDBusConnection *bus, const char *interface, const char *method,
                                GVariant *parameters)
{
    PortalRequest request = {0};
    /* Subscribe before making the call. Filter the returned handle only after
       call_sync completes, so an early Response cannot race subscription. */
    guint subscription =
        g_dbus_connection_signal_subscribe(bus, PORTAL, "org.freedesktop.portal.Request", "Response", NULL,
                                           NULL, G_DBUS_SIGNAL_FLAGS_NONE, response_received, &request, NULL);
    GError *error = NULL;
    GVariant *reply =
        g_dbus_connection_call_sync(bus, PORTAL, PORTAL_PATH, interface, method, parameters,
                                    G_VARIANT_TYPE("(o)"), G_DBUS_CALL_FLAGS_NONE, 10000, NULL, &error);
    if (!reply) {
        g_clear_error(&error);
        g_dbus_connection_signal_unsubscribe(bus, subscription);
        fputs("dependency-missing\n", stderr);
        return NULL;
    }
    g_variant_get(reply, "(&o)", &request.path);
    gint64 deadline = g_get_monotonic_time() + 120 * G_TIME_SPAN_SECOND;
    while (!request.received && !stopping && !g_dbus_connection_is_closed(bus) &&
           g_get_monotonic_time() < deadline) {
        while (g_main_context_iteration(NULL, FALSE)) {
        }
        if (!request.received)
            g_usleep(10000);
    }
    if (!request.received)
        close_portal_object(bus, request.path, "org.freedesktop.portal.Request");
    g_dbus_connection_signal_unsubscribe(bus, subscription);
    g_variant_unref(reply);
    if (!request.received || request.status != 0) {
        if (request.result)
            g_variant_unref(request.result);
        fputs(stopping || (request.received && request.status == 1) ? "cancelled\n"
              : !request.received                                   ? "timeout\n"
                                                                    : "permission-denied\n",
              stderr);
        return NULL;
    }
    return request.result;
}

static void init_options(GVariantBuilder *options)
{
    static guint sequence;
    char token[64];
    g_snprintf(token, sizeof(token), "ritsukage_%u_%u", (guint)getpid(), ++sequence);
    g_variant_builder_init(options, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(options, "{sv}", "handle_token", g_variant_new_string(token));
}

static guint property_uint(GDBusConnection *bus, const char *interface, const char *name, guint fallback)
{
    GVariant *reply =
        g_dbus_connection_call_sync(bus, PORTAL, PORTAL_PATH, "org.freedesktop.DBus.Properties", "Get",
                                    g_variant_new("(ss)", interface, name), G_VARIANT_TYPE("(v)"),
                                    G_DBUS_CALL_FLAGS_NONE, 5000, NULL, NULL);
    if (!reply)
        return fallback;
    GVariant *boxed = NULL;
    g_variant_get(reply, "(@v)", &boxed);
    GVariant *value = g_variant_get_variant(boxed);
    guint result =
        g_variant_is_of_type(value, G_VARIANT_TYPE_UINT32) ? g_variant_get_uint32(value) : fallback;
    g_variant_unref(value);
    g_variant_unref(boxed);
    g_variant_unref(reply);
    return result;
}

static int open_remote(GDBusConnection *bus, const char *session)
{
    GVariantBuilder options;
    g_variant_builder_init(&options, G_VARIANT_TYPE_VARDICT);
    GUnixFDList *descriptors = NULL;
    GVariant *reply = g_dbus_connection_call_with_unix_fd_list_sync(
        bus, PORTAL, PORTAL_PATH, SCREENCAST, "OpenPipeWireRemote",
        g_variant_new("(oa{sv})", session, &options), G_VARIANT_TYPE("(h)"), G_DBUS_CALL_FLAGS_NONE, 10000,
        NULL, &descriptors, NULL, NULL);
    if (!reply || !descriptors) {
        if (reply)
            g_variant_unref(reply);
        if (descriptors)
            g_object_unref(descriptors);
        return -1;
    }
    gint index;
    g_variant_get(reply, "(h)", &index);
    int descriptor = g_unix_fd_list_get(descriptors, index, NULL);
    g_variant_unref(reply);
    g_object_unref(descriptors);
    return descriptor;
}

static gboolean member_int(JsonObject *object, const char *name, gint64 *value)
{
    JsonNode *node = json_object_get_member(object, name);
    if (!node || !JSON_NODE_HOLDS_VALUE(node) || json_node_get_value_type(node) != G_TYPE_INT64)
        return FALSE;
    *value = json_node_get_int(node);
    return TRUE;
}
static const char *member_string(JsonObject *object, const char *name)
{
    JsonNode *node = json_object_get_member(object, name);
    return node && JSON_NODE_HOLDS_VALUE(node) && json_node_get_value_type(node) == G_TYPE_STRING
               ? json_node_get_string(node)
               : NULL;
}
static gboolean control_line(const char *line, struct rgc_portal_video *video, GDBusConnection *bus,
                             const char *session, const struct rgc_portal_source *source, uint32_t devices,
                             GCancellable *cancel)
{
    gint64 preparation_deadline = g_get_monotonic_time() + 2 * G_TIME_SPAN_SECOND;
    JsonParser *parser = json_parser_new();
    GError *error = NULL;
    gint64 request_id = 0;
    if (!json_parser_load_from_data(parser, line, -1, &error) ||
        !JSON_NODE_HOLDS_OBJECT(json_parser_get_root(parser))) {
        gboolean written =
            rgc_portal_video_reply(video, 0, "unavailable", "The capture command JSON is invalid.");
        g_clear_error(&error);
        g_object_unref(parser);
        return written;
    }
    JsonObject *object = json_node_get_object(json_parser_get_root(parser));
    if (!member_int(object, "requestId", &request_id) || request_id < 0)
        request_id = 0;
    const char *command = member_string(object, "command");
    gboolean written = TRUE;
    if (command && !strcmp(command, "stop")) {
        stopping = 1;
    } else if (command && (!strcmp(command, "suspend") || !strcmp(command, "resume"))) {
        written = request_id > 0 ? rgc_portal_video_set_active(video, request_id, !strcmp(command, "resume"))
                                 : rgc_portal_video_reply(video, request_id, "unavailable",
                                                          "The capture command request identity is invalid.");
    } else if (command && !strcmp(command, "cursor")) {
        written = rgc_portal_video_cursor(video, request_id);
    } else if (command && !strcmp(command, "capabilities")) {
        written = rgc_portal_video_capabilities(video, request_id);
    } else if (command && !strcmp(command, "configure")) {
        gint64 generation, x, y, width, height, output_width, output_height, fps;
        JsonNode *hdr_node = json_object_get_member(object, "retainHdr");
        const char *stream = member_string(object, "streamId");
        JsonNode *region_node = json_object_get_member(object, "region");
        JsonObject *region =
            region_node && JSON_NODE_HOLDS_OBJECT(region_node) ? json_node_get_object(region_node) : NULL;
        char expected_stream[32];
        g_snprintf(expected_stream, sizeof(expected_stream), "%u", source->node_id);
        if (request_id <= 0 || !member_int(object, "fps", &fps) || fps < 1 || fps > 120 ||
            !hdr_node || !JSON_NODE_HOLDS_VALUE(hdr_node) || json_node_get_value_type(hdr_node) != G_TYPE_BOOLEAN ||
            !member_int(object, "generation", &generation) || generation < 0 ||
            (uint64_t)generation != source->generation || !stream || strcmp(stream, expected_stream) ||
            !region || !member_int(region, "x", &x) || !member_int(region, "y", &y) ||
            !member_int(region, "width", &width) || !member_int(region, "height", &height) ||
            !member_int(object, "outputWidth", &output_width) ||
            !member_int(object, "outputHeight", &output_height) || x < 0 || y < 0 || width <= 0 ||
            height <= 0 || x > G_MAXINT || y > G_MAXINT || width > G_MAXINT || height > G_MAXINT ||
            output_width <= 0 || output_height <= 0 || output_width > 32768 || output_height > 32768) {
            written = rgc_portal_video_reply(video, request_id, "unavailable",
                                             "The capture configuration or source identity is invalid.");
        } else {
            written = rgc_portal_video_configure(video, request_id, (uint64_t)generation, source->node_id,
                                                 (int)x, (int)y, (int)width, (int)height, (int)output_width,
                                                 (int)output_height, (int)fps, json_node_get_boolean(hdr_node));
        }
    } else if (command && (!strcmp(command, "scroll") || !strcmp(command, "prepare-pointer"))) {
        gboolean prepare = !strcmp(command, "prepare-pointer");
        gint64 generation, x, y, width, height, source_width = 0, source_height = 0;
        const char *stream = member_string(object, "streamId"), *axis = member_string(object, "axis");
        JsonNode *region_node = json_object_get_member(object, "region");
        JsonObject *region =
            region_node && JSON_NODE_HOLDS_OBJECT(region_node) ? json_node_get_object(region_node) : NULL;
        char expected_stream[32];
        g_snprintf(expected_stream, sizeof(expected_stream), "%u", source->node_id);
        if (!member_int(object, "generation", &generation) || generation < 0 ||
            (uint64_t)generation != source->generation || !stream || strcmp(stream, expected_stream) ||
            request_id <= 0 || !region ||
            (!prepare && (!axis || (strcmp(axis, "vertical") && strcmp(axis, "horizontal")))) ||
            (prepare && (!member_int(object, "sourceWidth", &source_width) || source_width <= 0 ||
                         source_width > G_MAXINT || !member_int(object, "sourceHeight", &source_height) ||
                         source_height <= 0 || source_height > G_MAXINT)) ||
            !member_int(region, "x", &x) || !member_int(region, "y", &y) ||
            !member_int(region, "width", &width) || !member_int(region, "height", &height) || x < 0 ||
            y < 0 || width <= 0 || height <= 0 || x > G_MAXINT || y > G_MAXINT || width > G_MAXINT ||
            height > G_MAXINT) {
            written = rgc_portal_video_reply(video, request_id, "unavailable",
                                             "The authorized source or scroll coordinates changed.");
        } else {
            struct rgc_portal_pointer_observation observation;
            rgc_portal_video_pointer(video, &observation);
            int result = prepare
                             ? rgc_portal_scroll_input_prepare(
                                   bus, session, devices, source->generation, source->node_id, &observation,
                                   (int)source_width, (int)source_height, (int)x, (int)y, (int)width,
                                   (int)height, cancel, &error)
                             : rgc_portal_scroll_input_send(
                                   bus, session, devices, source->generation, source->node_id, &observation,
                                   (int)x, (int)y, (int)width, (int)height,
                                   !strcmp(axis, "vertical") ? 0 : 1, 1, cancel, &error);
            if (prepare && result == RGC_SCROLL_SENT) {
                int center_x = (int)x + (int)width / 2, center_y = (int)y + (int)height / 2;
                while (TRUE) {
                    if (g_cancellable_set_error_if_cancelled(cancel, &error)) {
                        result = RGC_SCROLL_UNAVAILABLE;
                        break;
                    }
                    struct rgc_portal_pointer_observation current;
                    rgc_portal_video_pointer(video, &current);
                    if (current.generation != observation.generation ||
                        current.stream_id != observation.stream_id || current.width != source_width ||
                        current.height != source_height) {
                        g_set_error_literal(&error, G_IO_ERROR, G_IO_ERROR_INVALID_ARGUMENT,
                                            "The authorized capture source changed during pointer preparation.");
                        result = RGC_SCROLL_UNAVAILABLE;
                        break;
                    }
                    gint64 now = g_get_monotonic_time();
                    if (now >= preparation_deadline) {
                        g_set_error_literal(&error, G_IO_ERROR, G_IO_ERROR_TIMED_OUT,
                                            "No current pointer observation reached the requested capture center.");
                        result = RGC_SCROLL_UNAVAILABLE;
                        break;
                    }
                    gint64 age = now - current.observed_monotonic_us;
                    gboolean exact_center = current.cursor_x == center_x && current.cursor_y == center_y;
                    gboolean updated_center = current.observed_monotonic_us > observation.observed_monotonic_us &&
                                              (int64_t)current.cursor_x >= (int64_t)center_x - 1 &&
                                              (int64_t)current.cursor_x <= (int64_t)center_x + 1 &&
                                              (int64_t)current.cursor_y >= (int64_t)center_y - 1 &&
                                              (int64_t)current.cursor_y <= (int64_t)center_y + 1;
                    if (current.cursor_valid && current.observed_monotonic_us > 0 && age >= 0 && age <= 250000 &&
                        current.cursor_x >= x && current.cursor_y >= y &&
                        (int64_t)current.cursor_x < x + width && (int64_t)current.cursor_y < y + height &&
                        (exact_center || updated_center))
                        break;
                    g_usleep(5000);
                }
            }
            written = (!prepare || result != RGC_SCROLL_SENT || rgc_portal_video_cursor(video, 0)) &&
                      rgc_portal_video_reply(video, request_id,
                                             result == RGC_SCROLL_SENT          ? (prepare ? "prepared" : "sent")
                                             : result == RGC_SCROLL_UNAVAILABLE ? "unavailable"
                                                                                : "failed",
                                             error ? error->message : NULL);
            g_clear_error(&error);
        }
    } else {
        written =
            rgc_portal_video_reply(video, request_id, "unavailable", "The capture command is unsupported.");
    }
    g_object_unref(parser);
    return written;
}

static gboolean run_capture(GDBusConnection *bus, int descriptor, const char *session,
                            const struct rgc_portal_source *source, int fps, gboolean hdr,
                            gboolean metadata_cursor, uint32_t devices, gboolean can_request_pointer,
                            GCancellable *cancel, struct rgc_frame_writer *writer)
{
    GError *error = NULL;
    struct rgc_portal_video *video = rgc_portal_video_new(
        descriptor, source, writer, fps, hdr, metadata_cursor, devices, can_request_pointer, cancel, &error);
    if (!video) {
        g_printerr("Capture initialization failed: %s\n", error ? error->message : "unknown error");
        g_clear_error(&error);
        return FALSE;
    }
    GString *pending = g_string_new(NULL);
    gboolean success = TRUE;
    gint64 deadline = g_get_monotonic_time() + 30 * G_TIME_SPAN_SECOND;
    while (!stopping && !g_cancellable_is_cancelled(cancel) && !g_dbus_connection_is_closed(bus)) {
        while (g_main_context_iteration(NULL, FALSE)) {
        }
        char *reason = NULL;
        if (rgc_portal_video_failed(video, &reason)) {
            g_printerr("Capture source lost: %s\n", reason);
            g_free(reason);
            success = FALSE;
            break;
        }
        if (!rgc_portal_video_has_frame(video) && g_get_monotonic_time() >= deadline) {
            fputs("Capture source timed out before its first frame.\n", stderr);
            success = FALSE;
            break;
        }
        struct pollfd input = {.fd = STDIN_FILENO, .events = POLLIN};
        int ready = poll(&input, 1, 50);
        if (ready < 0) {
            if (errno == EINTR)
                continue;
            success = FALSE;
            break;
        }
        if (ready == 0)
            continue;
        if (input.revents & POLLIN) {
            char data[4096];
            ssize_t length = read(STDIN_FILENO, data, sizeof(data));
            if (length == 0)
                break;
            if (length < 0) {
                if (errno == EINTR || errno == EAGAIN)
                    continue;
                success = FALSE;
                break;
            }
            g_string_append_len(pending, data, length);
            char *newline;
            while ((newline = memchr(pending->str, '\n', pending->len))) {
                gsize length_to_line = (gsize)(newline - pending->str);
                char *line = g_strndup(pending->str, length_to_line);
                gboolean written = control_line(line, video, bus, session, source, devices, cancel);
                g_free(line);
                g_string_erase(pending, 0, (gssize)(length_to_line + 1));
                if (!written) {
                    success = FALSE;
                    stopping = 1;
                    break;
                }
            }
            if (pending->len > 65536) {
                fputs("Capture command exceeds the input limit.\n", stderr);
                success = FALSE;
                break;
            }
        } else if (input.revents & (POLLHUP | POLLERR | POLLNVAL))
            break;
    }
    g_cancellable_cancel(cancel);
    rgc_portal_video_free(video);
    g_string_free(pending, TRUE);
    return success;
}

static gboolean parse_number(const char *text, gint64 minimum, gint64 maximum, gint64 *result)
{
    char *end = NULL;
    errno = 0;
    gint64 value = g_ascii_strtoll(text, &end, 10);
    if (errno || !end || end == text || *end || value < minimum || value > maximum)
        return FALSE;
    *result = value;
    return TRUE;
}

int main(int argc, char **argv)
{
    if (argc < 2 || (strcmp(argv[1], "screen") && strcmp(argv[1], "window"))) {
        fputs("unsupported\n", stderr);
        return 2;
    }
    struct rgc_portal_source source = {.source_type = !strcmp(argv[1], "window") ? 2u : 1u};
    int fps = 30;
    gboolean cursor = TRUE, hdr = FALSE, pointer = FALSE;
    for (int index = 2; index < argc; index += 2) {
        if (index + 1 >= argc) {
            fputs("unsupported\n", stderr);
            return 2;
        }
        const char *name = argv[index], *value = argv[index + 1];
        gint64 number;
        if (!strcmp(name, "--fps") && parse_number(value, 1, 240, &number))
            fps = (int)number;
        else if (!strcmp(name, "--cursor") && parse_number(value, 0, 1, &number))
            cursor = number != 0;
        else if (!strcmp(name, "--generation") && parse_number(value, 0, G_MAXINT64, &number))
            source.generation = (uint64_t)number;
        else if (!strcmp(name, "--color") && (!strcmp(value, "sdr") || !strcmp(value, "hdr")))
            hdr = !strcmp(value, "hdr");
        else if (!strcmp(name, "--input") && (!strcmp(value, "none") || !strcmp(value, "pointer")))
            pointer = !strcmp(value, "pointer");
        else {
            fputs("unsupported\n", stderr);
            return 2;
        }
    }
    signal(SIGINT, stop_signal);
    signal(SIGTERM, stop_signal);
    signal(SIGPIPE, SIG_IGN);
    pid_t parent = getppid();
    if (prctl(PR_SET_PDEATHSIG, SIGTERM) < 0 || parent == 1 || getppid() != parent)
        return 1;
    int output_flags = fcntl(STDOUT_FILENO, F_GETFL);
    int input_flags = fcntl(STDIN_FILENO, F_GETFL);
    if (output_flags < 0 || input_flags < 0 || fcntl(STDOUT_FILENO, F_SETFL, output_flags | O_NONBLOCK) < 0 ||
        fcntl(STDIN_FILENO, F_SETFL, input_flags | O_NONBLOCK) < 0)
        return 1;
    pw_init(NULL, NULL);
    GDBusConnection *bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, NULL);
    if (!bus) {
        fputs("dependency-missing\n", stderr);
        pw_deinit();
        return 1;
    }
    g_dbus_connection_set_exit_on_close(bus, FALSE);
    GCancellable *cancel = g_cancellable_new();
    struct rgc_frame_writer writer;
    rgc_frame_writer_init(&writer, cancel);
    int exit_code = 1, descriptor = -1;
    char *session = NULL;
    guint subscription = 0, devices = 0;
    GVariant *result = NULL;
    GVariantBuilder options;
    source.portal_version = property_uint(bus, SCREENCAST, "version", 1);
    guint source_types = property_uint(bus, SCREENCAST, "AvailableSourceTypes", 1);
    guint cursor_modes =
        source.portal_version >= 2 ? property_uint(bus, SCREENCAST, "AvailableCursorModes", 1) : 1;
    gboolean metadata_cursor = (cursor_modes & 4) != 0;
    source.available_cursor_modes = cursor_modes;
    source.cursor_mode = metadata_cursor ? 4u : cursor ? 2u : 1u;
    guint remote_devices = property_uint(bus, REMOTE_DESKTOP, "AvailableDeviceTypes", 0);
    gboolean can_request_pointer = (remote_devices & 2) != 0;
    if (!(source_types & source.source_type)) {
        fputs("unsupported\n", stderr);
        goto cleanup;
    }
    if (pointer && (!can_request_pointer || !metadata_cursor)) {
        fputs("Pointer control requires an available RemoteDesktop portal and independent cursor metadata.\n",
              stderr);
        goto cleanup;
    }
    const char *session_interface = pointer ? REMOTE_DESKTOP : SCREENCAST;
    init_options(&options);
    char session_token[64];
    g_snprintf(session_token, sizeof(session_token), "ritsukage_session_%u", (guint)getpid());
    g_variant_builder_add(&options, "{sv}", "session_handle_token", g_variant_new_string(session_token));
    result = portal_request(bus, session_interface, "CreateSession", g_variant_new("(a{sv})", &options));
    if (!result)
        goto cleanup;
    if (!g_variant_lookup(result, "session_handle", "s", &session) || !g_variant_is_object_path(session)) {
        fputs("capture-failed\n", stderr);
        goto cleanup;
    }
    g_clear_pointer(&result, g_variant_unref);
    subscription =
        g_dbus_connection_signal_subscribe(bus, PORTAL, "org.freedesktop.portal.Session", "Closed", session,
                                           NULL, G_DBUS_SIGNAL_FLAGS_NONE, session_closed, NULL, NULL);
    if (pointer) {
        init_options(&options);
        g_variant_builder_add(&options, "{sv}", "types", g_variant_new_uint32(2));
        result = portal_request(bus, REMOTE_DESKTOP, "SelectDevices",
                                g_variant_new("(oa{sv})", session, &options));
        if (!result)
            goto cleanup;
        g_clear_pointer(&result, g_variant_unref);
    }
    init_options(&options);
    g_variant_builder_add(&options, "{sv}", "types", g_variant_new_uint32(source.source_type));
    g_variant_builder_add(&options, "{sv}", "multiple", g_variant_new_boolean(FALSE));
    if (source.portal_version >= 2) {
        guint mode = metadata_cursor ? 4u : cursor ? 2u : 1u;
        if (!(cursor_modes & mode)) {
            g_variant_builder_clear(&options);
            fputs("unsupported\n", stderr);
            goto cleanup;
        }
        g_variant_builder_add(&options, "{sv}", "cursor_mode", g_variant_new_uint32(mode));
    } else if (cursor) {
        g_variant_builder_clear(&options);
        fputs("unsupported\n", stderr);
        goto cleanup;
    }
    result = portal_request(bus, SCREENCAST, "SelectSources", g_variant_new("(oa{sv})", session, &options));
    if (!result)
        goto cleanup;
    g_clear_pointer(&result, g_variant_unref);
    init_options(&options);
    result =
        portal_request(bus, session_interface, "Start", g_variant_new("(osa{sv})", session, "", &options));
    if (!result)
        goto cleanup;
    if (pointer && (!g_variant_lookup(result, "devices", "u", &devices) || !(devices & 2))) {
        fputs("permission-denied\n", stderr);
        goto cleanup;
    }
    g_variant_lookup(result, "restore_token", "s", &source.restore_token);
    GVariant *streams = g_variant_lookup_value(result, "streams", G_VARIANT_TYPE("a(ua{sv})"));
    if (!streams || g_variant_n_children(streams) != 1) {
        if (streams)
            g_variant_unref(streams);
        fputs("source-lost\n", stderr);
        goto cleanup;
    }
    GVariant *properties = NULL;
    g_variant_get_child(streams, 0, "(u@a{sv})", &source.node_id, &properties);
    g_variant_lookup(properties, "pipewire-serial", "t", &source.serial);
    g_variant_lookup(properties, "source_type", "u", &source.source_type);
    g_variant_lookup(properties, "id", "s", &source.id);
    source.has_position =
        g_variant_lookup(properties, "position", "(ii)", &source.position_x, &source.position_y);
    source.has_size = g_variant_lookup(properties, "size", "(ii)", &source.size_width, &source.size_height);
    source.has_logical_size =
        g_variant_lookup(properties, "logical_size", "(ii)", &source.logical_width, &source.logical_height);
    g_variant_unref(properties);
    g_variant_unref(streams);
    g_clear_pointer(&result, g_variant_unref);
    descriptor = open_remote(bus, session);
    if (descriptor < 0) {
        fputs("capture-failed\n", stderr);
        goto cleanup;
    }
    int remote_descriptor = descriptor;
    descriptor = -1;
    if (run_capture(bus, remote_descriptor, session, &source, fps, hdr, metadata_cursor, devices,
                    can_request_pointer, cancel, &writer))
        exit_code = 0;
cleanup:
    g_cancellable_cancel(cancel);
    if (result)
        g_variant_unref(result);
    if (subscription)
        g_dbus_connection_signal_unsubscribe(bus, subscription);
    if (session)
        close_portal_object(bus, session, "org.freedesktop.portal.Session");
    g_free(source.id);
    g_free(source.restore_token);
    g_free(session);
    if (descriptor >= 0)
        close(descriptor);
    rgc_frame_writer_clear(&writer);
    g_object_unref(cancel);
    g_object_unref(bus);
    pw_deinit();
    return stopping ? 0 : exit_code;
}
