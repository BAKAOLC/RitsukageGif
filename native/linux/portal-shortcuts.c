#define _POSIX_C_SOURCE 200809L
#include <gio/gio.h>
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <unistd.h>

#define PORTAL "org.freedesktop.portal.Desktop"
#define PATH "/org/freedesktop/portal/desktop"
#define SHORTCUTS "org.freedesktop.portal.GlobalShortcuts"

static volatile sig_atomic_t stopping;
static gboolean refresh;
static const char *session;
static int binding_count;
static char **arguments;
static gboolean *active;

typedef struct {
    GVariant *reply;
    GError *error;
    gboolean done;
} Call;

typedef struct {
    gchar *expected;
    GVariant *result;
    guint status;
    gboolean received;
} Request;

static void stop_signal(int number)
{
    (void)number;
    stopping = 1;
}

static gboolean pump(void)
{
    unsigned char command;
    ssize_t count = read(STDIN_FILENO, &command, 1);
    if (count == 0 || count > 0 || (count < 0 && errno != EAGAIN && errno != EWOULDBLOCK && errno != EINTR))
        stopping = 1;
    for (unsigned iteration = 0; iteration < 64 && !stopping; iteration++)
        if (!g_main_context_iteration(NULL, FALSE)) break;
    if (!stopping) g_usleep(5000);
    return !stopping;
}

static void call_finished(GObject *object, GAsyncResult *result, gpointer data)
{
    Call *call = data;
    call->reply = g_dbus_connection_call_finish(G_DBUS_CONNECTION(object), result, &call->error);
    call->done = TRUE;
}

static GVariant *invoke(GDBusConnection *bus, const char *path, const char *interface,
                        const char *method, GVariant *parameters, const GVariantType *type)
{
    Call call = {0};
    GCancellable *cancel = g_cancellable_new();
    g_dbus_connection_call(bus, PORTAL, path, interface, method, parameters, type,
                           G_DBUS_CALL_FLAGS_NONE, 10000, cancel, call_finished, &call);
    while (!call.done) {
        if (!pump()) g_cancellable_cancel(cancel);
    }
    if (call.error) {
        if (!stopping) fprintf(stderr, "Portal %s failed: %s\n", method, call.error->message);
        g_error_free(call.error);
    }
    g_object_unref(cancel);
    return call.reply;
}

static void close_object(GDBusConnection *bus, const char *path, const char *interface)
{
    if (!path || g_dbus_connection_is_closed(bus)) return;
    GVariant *reply = g_dbus_connection_call_sync(bus, PORTAL, path, interface, "Close", NULL,
                                                  NULL, G_DBUS_CALL_FLAGS_NONE, 1500, NULL, NULL);
    if (reply) g_variant_unref(reply);
}

static void response(GDBusConnection *bus, const gchar *sender, const gchar *path,
                     const gchar *interface, const gchar *name, GVariant *parameters, gpointer data)
{
    (void)bus; (void)sender; (void)interface; (void)name;
    Request *request = data;
    if (request->received || strcmp(path, request->expected) != 0 ||
        !g_variant_is_of_type(parameters, G_VARIANT_TYPE("(ua{sv})"))) return;
    g_variant_get(parameters, "(u@a{sv})", &request->status, &request->result);
    request->received = TRUE;
}

static void options(GVariantBuilder *builder, const char *token)
{
    g_variant_builder_init(builder, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(builder, "{sv}", "handle_token", g_variant_new_string(token));
}

static GVariant *request(GDBusConnection *bus, const char *method, GVariant *parameters, const char *token)
{
    const char *unique = g_dbus_connection_get_unique_name(bus);
    char *sender = g_strdup(unique + 1);
    for (char *cursor = sender; *cursor; ++cursor) if (*cursor == '.') *cursor = '_';
    Request state = {0};
    state.expected = g_strdup_printf(PATH "/request/%s/%s", sender, token);
    g_free(sender);
    guint subscription = g_dbus_connection_signal_subscribe(bus, PORTAL,
        "org.freedesktop.portal.Request", "Response", NULL, NULL, G_DBUS_SIGNAL_FLAGS_NONE,
        response, &state, NULL);
    GVariant *reply = invoke(bus, PATH, SHORTCUTS, method, parameters, G_VARIANT_TYPE("(o)"));
    if (reply) {
        const char *actual;
        g_variant_get(reply, "(&o)", &actual);
        if (strcmp(actual, state.expected) != 0) {
            g_free(state.expected);
            state.expected = g_strdup(actual);
        }
        gint64 deadline = g_get_monotonic_time() + 120 * G_TIME_SPAN_SECOND;
        while (!state.received && !stopping && !g_dbus_connection_is_closed(bus) &&
               g_get_monotonic_time() < deadline) pump();
        if (!state.received) close_object(bus, actual, "org.freedesktop.portal.Request");
        g_variant_unref(reply);
    }
    g_dbus_connection_signal_unsubscribe(bus, subscription);
    g_free(state.expected);
    if (!state.received || state.status != 0) {
        if (state.result) g_variant_unref(state.result);
        if (!stopping) fputs(state.received && state.status == 1 ? "Portal request cancelled.\n" :
                              "Portal request rejected or unavailable.\n", stderr);
        return NULL;
    }
    return state.result;
}

static int index_of(const char *id)
{
    for (int index = 0; index < binding_count; index++)
        if (strcmp(arguments[1 + index * 3], id) == 0) return index;
    return -1;
}

static void bindings_changed(GVariant *list)
{
    GVariantIter iterator;
    const char *id;
    GVariant *properties;
    gboolean *present = g_new0(gboolean, binding_count);
    g_variant_iter_init(&iterator, list);
    while (g_variant_iter_next(&iterator, "(&s@a{sv})", &id, &properties)) {
        int index = index_of(id);
        if (index >= 0) {
            const char *trigger = "";
            g_variant_lookup(properties, "trigger_description", "&s", &trigger);
            present[index] = trigger[0] != '\0';
            if (present[index]) {
                gchar *encoded = g_base64_encode((const guchar *)trigger, strlen(trigger));
                printf("B\t%s\t%s\n", id, encoded);
                g_free(encoded);
            }
        }
        g_variant_unref(properties);
    }
    for (int index = 0; index < binding_count; index++) {
        if (!present[index]) printf("F\t%s\n", arguments[1 + index * 3]);
        active[index] = present[index];
    }
    g_free(present);
    fputs("R\n", stdout);
    fflush(stdout);
}

static void signal_received(GDBusConnection *bus, const gchar *sender, const gchar *path,
                            const gchar *interface, const gchar *name, GVariant *parameters, gpointer data)
{
    (void)bus; (void)sender; (void)path; (void)interface; (void)data;
    if (strcmp(name, "Closed") == 0) { stopping = 1; return; }
    const char *source;
    if (strcmp(name, "ShortcutsChanged") == 0) {
        if (g_variant_is_of_type(parameters, G_VARIANT_TYPE("(oa(sa{sv}))"))) {
            g_variant_get_child(parameters, 0, "&o", &source);
            if (strcmp(source, session) == 0) refresh = TRUE;
        }
        return;
    }
    if (!g_variant_is_of_type(parameters, G_VARIANT_TYPE("(osta{sv})"))) return;
    const char *id;
    g_variant_get_child(parameters, 0, "&o", &source);
    g_variant_get_child(parameters, 1, "&s", &id);
    int index = index_of(id);
    if (strcmp(source, session) != 0 || index < 0 || !active[index]) return;
    printf("%c\t%s\n", strcmp(name, "Activated") == 0 ? 'A' : 'D', id);
    fflush(stdout);
}

int main(int argc, char **argv)
{
    if (argc < 4 || (argc - 1) % 3 != 0) return 2;
    binding_count = (argc - 1) / 3;
    arguments = argv;
    active = g_new0(gboolean, binding_count);
    setvbuf(stdout, NULL, _IOLBF, 0);
    signal(SIGTERM, stop_signal);
    signal(SIGINT, stop_signal);
    signal(SIGPIPE, stop_signal);
    prctl(PR_SET_PDEATHSIG, SIGTERM);
    if (getppid() == 1) return 1;
    int flags = fcntl(STDIN_FILENO, F_GETFL);
    if (flags < 0 || fcntl(STDIN_FILENO, F_SETFL, flags | O_NONBLOCK) < 0) return 1;
    GError *error = NULL;
    GDBusConnection *bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, &error);
    if (!bus) {
        fprintf(stderr, "Session bus unavailable: %s\n", error->message);
        g_error_free(error); g_free(active); return 1;
    }
    GVariantBuilder identity;
    g_variant_builder_init(&identity, G_VARIANT_TYPE_VARDICT);
    GVariant *registered = invoke(bus, PATH, "org.freedesktop.host.portal.Registry", "Register",
        g_variant_new("(sa{sv})", "ritsukagegif", &identity), G_VARIANT_TYPE("()"));
    if (registered) g_variant_unref(registered);
    /* Older portals may infer the installed desktop ID instead. Binding errors
       remain observable; no registration is claimed from this optional step. */
    GVariantBuilder opts;
    options(&opts, "shortcuts_create");
    g_variant_builder_add(&opts, "{sv}", "session_handle_token", g_variant_new_string("ritsukage_shortcuts"));
    GVariant *created = request(bus, "CreateSession", g_variant_new("(a{sv})", &opts), "shortcuts_create");
    char *owned_session = NULL;
    guint signal_subscription = 0, closed_subscription = 0;
    int exit_code = 1;
    if (!created) goto cleanup;
    const char *handle = NULL;
    if (!g_variant_lookup(created, "session_handle", "&s", &handle)) {
        g_variant_unref(created); goto cleanup;
    }
    owned_session = g_strdup(handle);
    session = owned_session;
    g_variant_unref(created);
    signal_subscription = g_dbus_connection_signal_subscribe(bus, PORTAL, SHORTCUTS, NULL, PATH,
        NULL, G_DBUS_SIGNAL_FLAGS_NONE, signal_received, NULL, NULL);
    closed_subscription = g_dbus_connection_signal_subscribe(bus, PORTAL,
        "org.freedesktop.portal.Session", "Closed", session, NULL,
        G_DBUS_SIGNAL_FLAGS_NONE, signal_received, NULL, NULL);
    GVariantBuilder shortcuts;
    g_variant_builder_init(&shortcuts, G_VARIANT_TYPE("a(sa{sv})"));
    for (int index = 0; index < binding_count; index++) {
        GVariantBuilder properties;
        g_variant_builder_init(&properties, G_VARIANT_TYPE_VARDICT);
        g_variant_builder_add(&properties, "{sv}", "preferred_trigger", g_variant_new_string(argv[2 + index * 3]));
        g_variant_builder_add(&properties, "{sv}", "description", g_variant_new_string(argv[3 + index * 3]));
        g_variant_builder_add(&shortcuts, "(sa{sv})", argv[1 + index * 3], &properties);
    }
    options(&opts, "shortcuts_bind");
    GVariant *bound = request(bus, "BindShortcuts", g_variant_new("(oa(sa{sv})sa{sv})",
        session, &shortcuts, "", &opts), "shortcuts_bind");
    if (!bound) goto cleanup;
    GVariant *list = g_variant_lookup_value(bound, "shortcuts", G_VARIANT_TYPE("a(sa{sv})"));
    if (!list) { g_variant_unref(bound); goto cleanup; }
    bindings_changed(list);
    g_variant_unref(list); g_variant_unref(bound);
    exit_code = 0;
    while (!stopping && !g_dbus_connection_is_closed(bus)) {
        pump();
        if (refresh && !stopping) {
            refresh = FALSE;
            options(&opts, "shortcuts_list");
            GVariant *listed = request(bus, "ListShortcuts", g_variant_new("(oa{sv})", session, &opts), "shortcuts_list");
            if (!listed) { exit_code = 1; break; }
            list = g_variant_lookup_value(listed, "shortcuts", G_VARIANT_TYPE("a(sa{sv})"));
            if (list) { bindings_changed(list); g_variant_unref(list); }
            else exit_code = 1;
            g_variant_unref(listed);
            if (exit_code) break;
        }
    }
cleanup:
    if (signal_subscription) g_dbus_connection_signal_unsubscribe(bus, signal_subscription);
    if (closed_subscription) g_dbus_connection_signal_unsubscribe(bus, closed_subscription);
    close_object(bus, session, "org.freedesktop.portal.Session");
    g_free(owned_session); g_object_unref(bus); g_free(active);
    return exit_code;
}
