#define _POSIX_C_SOURCE 200809L
#include <errno.h>
#include <fcntl.h>
#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <gst/app/gstappsink.h>
#include <gst/video/video.h>
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

static GVariant *portal_request(GDBusConnection *bus, const char *method, GVariant *parameters)
{
    PortalRequest request = {0};
    /* Subscribe before making the call. Filter the returned handle only after
       call_sync completes, so an early Response cannot race subscription. */
    guint subscription =
        g_dbus_connection_signal_subscribe(bus, PORTAL, "org.freedesktop.portal.Request", "Response", NULL,
                                           NULL, G_DBUS_SIGNAL_FLAGS_NONE, response_received, &request, NULL);
    GError *error = NULL;
    GVariant *reply =
        g_dbus_connection_call_sync(bus, PORTAL, PORTAL_PATH, SCREENCAST, method, parameters,
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

static guint property_uint(GDBusConnection *bus, const char *name, guint fallback)
{
    GVariant *reply =
        g_dbus_connection_call_sync(bus, PORTAL, PORTAL_PATH, "org.freedesktop.DBus.Properties", "Get",
                                    g_variant_new("(ss)", SCREENCAST, name), G_VARIANT_TYPE("(v)"),
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

static gboolean write_bytes(const void *buffer, size_t count)
{
    const guint8 *bytes = buffer;
    gint64 deadline = g_get_monotonic_time() + 5 * G_TIME_SPAN_SECOND;
    while (count && !stopping) {
        ssize_t written = write(STDOUT_FILENO, bytes, count);
        if (written > 0) {
            bytes += written;
            count -= (size_t)written;
            continue;
        }
        if (written < 0 && errno == EINTR)
            continue;
        if (written < 0 && (errno == EAGAIN || errno == EWOULDBLOCK)) {
            struct pollfd output = {.fd = STDOUT_FILENO, .events = POLLOUT};
            if (g_get_monotonic_time() >= deadline)
                return FALSE;
            int ready = poll(&output, 1, 100);
            if (ready < 0 && errno != EINTR)
                return FALSE;
            if (ready > 0 && output.revents & (POLLERR | POLLHUP | POLLNVAL))
                return FALSE;
            continue;
        }
        return FALSE;
    }
    return count == 0;
}

static gboolean write_sample(GstSample *sample)
{
    GstCaps *caps = gst_sample_get_caps(sample);
    GstBuffer *buffer = gst_sample_get_buffer(sample);
    GstVideoInfo info;
    gst_video_info_init(&info);
    if (!caps || !buffer || !gst_video_info_from_caps(&info, caps) ||
        GST_VIDEO_INFO_FORMAT(&info) != GST_VIDEO_FORMAT_BGRA)
        return FALSE;
    guint width = GST_VIDEO_INFO_WIDTH(&info), height = GST_VIDEO_INFO_HEIGHT(&info);
    guint64 length = (guint64)width * height * 4;
    if (!width || !height || width > G_MAXINT32 || height > G_MAXINT32 || length > FRAME_LIMIT)
        return FALSE;
    GstVideoFrame frame;
    memset(&frame, 0, sizeof(frame));
    if (!gst_video_frame_map(&frame, &info, buffer, GST_MAP_READ))
        return FALSE;
    gint stride = GST_VIDEO_FRAME_PLANE_STRIDE(&frame, 0);
    if (stride < 0 || (guint)stride < width * 4) {
        gst_video_frame_unmap(&frame);
        return FALSE;
    }
    guint32 header[6] = {GUINT32_TO_LE(0x46434752u),    GUINT32_TO_LE(width), GUINT32_TO_LE(height), 0, 0,
                         GUINT32_TO_LE((guint32)length)};
    gboolean success = write_bytes(header, sizeof(header));
    const guint8 *pixels = GST_VIDEO_FRAME_PLANE_DATA(&frame, 0);
    if (success && (guint)stride == width * 4)
        success = write_bytes(pixels, (size_t)length);
    else
        for (guint y = 0; success && y < height; y++)
            success = write_bytes(pixels + (gsize)y * (gsize)stride, (size_t)width * 4);
    gst_video_frame_unmap(&frame);
    return success;
}

static gboolean run_capture(GDBusConnection *bus, int descriptor, guint node, guint64 serial, int fps)
{
    GstElement *pipeline = gst_pipeline_new(NULL);
    GstElement *source = gst_element_factory_make("pipewiresrc", NULL);
    GstElement *convert = gst_element_factory_make("videoconvert", NULL);
    GstElement *rate = gst_element_factory_make("videorate", NULL);
    GstElement *sink = gst_element_factory_make("appsink", NULL);
    if (!pipeline || !source || !convert || !rate || !sink) {
        if (pipeline)
            gst_object_unref(pipeline);
        if (source)
            gst_object_unref(source);
        if (convert)
            gst_object_unref(convert);
        if (rate)
            gst_object_unref(rate);
        if (sink)
            gst_object_unref(sink);
        fputs("dependency-missing\n", stderr);
        return FALSE;
    }
    g_object_set(source, "fd", descriptor, "do-timestamp", TRUE, NULL);
    char identity[32];
    GObjectClass *source_class = G_OBJECT_GET_CLASS(source);
    if (serial && g_object_class_find_property(source_class, "target-object")) {
        g_snprintf(identity, sizeof(identity), "%" G_GUINT64_FORMAT, serial);
        g_object_set(source, "target-object", identity, NULL);
    } else {
        g_snprintf(identity, sizeof(identity), "%u", node);
        g_object_set(source, "path", identity, NULL);
    }
    if (g_object_class_find_property(source_class, "resend-last"))
        g_object_set(source, "resend-last", FALSE, NULL);
    if (g_object_class_find_property(source_class, "dont-reconnect"))
        g_object_set(source, "dont-reconnect", TRUE, NULL);
    if (g_object_class_find_property(source_class, "always-copy"))
        g_object_set(source, "always-copy", TRUE, NULL);
    if (g_object_class_find_property(source_class, "on-disconnect"))
        g_object_set(source, "on-disconnect", 1, NULL);
    g_object_set(rate, "drop-only", TRUE, NULL);
    g_object_set(sink, "sync", FALSE, "max-buffers", 1u, "drop", TRUE, "wait-on-eos", FALSE,
                 "enable-last-sample", FALSE, NULL);
    gst_bin_add_many(GST_BIN(pipeline), source, convert, rate, sink, NULL);
    GstCaps *output = gst_caps_new_simple("video/x-raw", "format", G_TYPE_STRING, "BGRA", "framerate",
                                          GST_TYPE_FRACTION, fps, 1, NULL);
    gboolean linked = gst_element_link_many(source, rate, convert, NULL) &&
                      gst_element_link_filtered(convert, sink, output);
    gst_caps_unref(output);
    gboolean success = linked;
    GstBus *messages = gst_element_get_bus(pipeline);
    if (!linked || gst_element_set_state(pipeline, GST_STATE_PLAYING) == GST_STATE_CHANGE_FAILURE) {
        fputs("capture-failed\n", stderr);
        success = FALSE;
    } else {
        gint64 first_frame_deadline = g_get_monotonic_time() + 30 * G_TIME_SPAN_SECOND;
        gboolean have_frame = FALSE;
        while (!stopping && !g_dbus_connection_is_closed(bus)) {
            while (g_main_context_iteration(NULL, FALSE)) {
            }
            struct pollfd output_pipe = {.fd = STDOUT_FILENO, .events = POLLOUT};
            if (poll(&output_pipe, 1, 0) > 0 && output_pipe.revents & (POLLERR | POLLHUP | POLLNVAL))
                break;
            GstMessage *message = gst_bus_pop_filtered(messages, GST_MESSAGE_ERROR | GST_MESSAGE_EOS);
            if (message) {
                success = GST_MESSAGE_TYPE(message) != GST_MESSAGE_ERROR;
                if (!success)
                    fputs("source-lost\n", stderr);
                gst_message_unref(message);
                break;
            }
            GstSample *sample = gst_app_sink_try_pull_sample(GST_APP_SINK(sink), 100 * GST_MSECOND);
            if (!sample) {
                if (gst_app_sink_is_eos(GST_APP_SINK(sink)))
                    break;
                if (!have_frame && g_get_monotonic_time() >= first_frame_deadline) {
                    fputs("timeout\n", stderr);
                    success = FALSE;
                    break;
                }
                continue;
            }
            success = write_sample(sample);
            have_frame = TRUE;
            gst_sample_unref(sample);
            if (!success)
                break;
        }
    }
    gst_element_set_state(pipeline, GST_STATE_NULL);
    gst_object_unref(messages);
    gst_object_unref(pipeline);
    return success;
}

int main(int argc, char **argv)
{
    if (argc < 2 || (strcmp(argv[1], "screen") && strcmp(argv[1], "window"))) {
        fputs("unsupported\n", stderr);
        return 2;
    }
    guint source_type = strcmp(argv[1], "window") == 0 ? 2u : 1u;
    int fps = 30, cursor = 1;
    for (int i = 2; i < argc; i += 2) {
        if (i + 1 >= argc) {
            fputs("unsupported\n", stderr);
            return 2;
        }
        char *end = NULL;
        errno = 0;
        long value = strtol(argv[i + 1], &end, 10);
        if (errno || !end || end == argv[i + 1] || *end) {
            fputs("unsupported\n", stderr);
            return 2;
        }
        if (!strcmp(argv[i], "--fps") && value >= 1 && value <= 240)
            fps = (int)value;
        else if (!strcmp(argv[i], "--cursor") && (value == 0 || value == 1))
            cursor = (int)value;
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
    if (output_flags < 0 || fcntl(STDOUT_FILENO, F_SETFL, output_flags | O_NONBLOCK) < 0)
        return 1;
    gst_init(NULL, NULL);
    GDBusConnection *bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, NULL);
    if (!bus) {
        fputs("dependency-missing\n", stderr);
        return 1;
    }
    g_dbus_connection_set_exit_on_close(bus, FALSE);
    int exit_code = 1;
    int descriptor = -1;
    char *session = NULL;
    guint session_subscription = 0;
    GVariant *result = NULL;
    GVariantBuilder options;
    guint version = property_uint(bus, "version", 1);
    if (version < 2 && cursor) {
        fputs("unsupported\n", stderr);
        goto cleanup;
    }
    if (!(property_uint(bus, "AvailableSourceTypes", 1) & source_type)) {
        fputs("unsupported\n", stderr);
        goto cleanup;
    }
    init_options(&options);
    char session_token[64];
    g_snprintf(session_token, sizeof(session_token), "ritsukage_session_%u", (guint)getpid());
    g_variant_builder_add(&options, "{sv}", "session_handle_token", g_variant_new_string(session_token));
    result = portal_request(bus, "CreateSession", g_variant_new("(a{sv})", &options));
    if (!result)
        goto cleanup;
    if (!g_variant_lookup(result, "session_handle", "s", &session) || !g_variant_is_object_path(session)) {
        fputs("capture-failed\n", stderr);
        goto cleanup;
    }
    g_clear_pointer(&result, g_variant_unref);
    session_subscription =
        g_dbus_connection_signal_subscribe(bus, PORTAL, "org.freedesktop.portal.Session", "Closed", session,
                                           NULL, G_DBUS_SIGNAL_FLAGS_NONE, session_closed, NULL, NULL);
    init_options(&options);
    g_variant_builder_add(&options, "{sv}", "types", g_variant_new_uint32(source_type));
    g_variant_builder_add(&options, "{sv}", "multiple", g_variant_new_boolean(FALSE));
    if (version >= 2) {
        guint modes = property_uint(bus, "AvailableCursorModes", 1);
        guint mode = cursor ? 2u : 1u;
        if (!(modes & mode)) {
            g_variant_builder_clear(&options);
            fputs("unsupported\n", stderr);
            goto cleanup;
        }
        g_variant_builder_add(&options, "{sv}", "cursor_mode", g_variant_new_uint32(mode));
    }
    result = portal_request(bus, "SelectSources", g_variant_new("(oa{sv})", session, &options));
    if (!result)
        goto cleanup;
    g_clear_pointer(&result, g_variant_unref);
    init_options(&options);
    result = portal_request(bus, "Start", g_variant_new("(osa{sv})", session, "", &options));
    if (!result)
        goto cleanup;
    GVariant *streams = g_variant_lookup_value(result, "streams", G_VARIANT_TYPE("a(ua{sv})"));
    if (!streams || g_variant_n_children(streams) != 1) {
        if (streams)
            g_variant_unref(streams);
        fputs("source-lost\n", stderr);
        goto cleanup;
    }
    guint node;
    guint64 serial = 0;
    GVariant *properties = NULL;
    g_variant_get_child(streams, 0, "(u@a{sv})", &node, &properties);
    g_variant_lookup(properties, "pipewire-serial", "t", &serial);
    g_variant_unref(properties);
    g_variant_unref(streams);
    g_clear_pointer(&result, g_variant_unref);
    descriptor = open_remote(bus, session);
    if (descriptor < 0) {
        fputs("capture-failed\n", stderr);
        goto cleanup;
    }
    if (run_capture(bus, descriptor, node, serial, fps))
        exit_code = 0;

cleanup:
    if (result)
        g_variant_unref(result);
    if (session_subscription)
        g_dbus_connection_signal_unsubscribe(bus, session_subscription);
    if (session)
        close_portal_object(bus, session, "org.freedesktop.portal.Session");
    g_free(session);
    if (descriptor >= 0)
        close(descriptor);
    g_object_unref(bus);
    return stopping ? 0 : exit_code;
}
