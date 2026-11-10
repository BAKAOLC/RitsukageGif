#define _POSIX_C_SOURCE 200809L
#include "portal-producer.h"
#include <pipewire/pipewire.h>
#include <spa/utils/dict.h>
#include <string.h>

struct identity_probe {
    struct pw_main_loop *loop;
    struct pw_context *context;
    struct pw_core *core;
    struct pw_registry *registry;
    struct pw_client *client;
    struct spa_hook core_listener, registry_listener, client_listener;
    struct spa_source *timer;
    GHashTable *clients;
    uint32_t expected_cookie, kwin_pid, node_id, client_id;
    uint64_t node_serial;
    gboolean core_seen, target_seen, client_seen, matched;
};

static gboolean number(const gchar *value, uint64_t *result)
{
    if (!value || !g_ascii_isdigit(*value)) return FALSE;
    gchar *end = NULL;
    *result = g_ascii_strtoull(value, &end, 10);
    return end && !*end;
}

static void finish_if_ready(struct identity_probe *probe)
{
    if (probe->core_seen && probe->client_seen) pw_main_loop_quit(probe->loop);
}

static void client_info(void *data, const struct pw_client_info *info)
{
    struct identity_probe *probe = data;
    uint64_t pid;
    probe->matched = info->props && number(spa_dict_lookup(info->props, PW_KEY_SEC_PID), &pid) &&
                     pid == probe->kwin_pid;
    probe->client_seen = TRUE;
    finish_if_ready(probe);
}

static const struct pw_client_events client_events = {PW_VERSION_CLIENT_EVENTS, .info = client_info};

static void bind_client(struct identity_probe *probe)
{
    if (!probe->target_seen || probe->client) return;
    guint version = GPOINTER_TO_UINT(g_hash_table_lookup(probe->clients, GUINT_TO_POINTER(probe->client_id)));
    if (!version) return;
    probe->client = pw_registry_bind(probe->registry, probe->client_id, PW_TYPE_INTERFACE_Client,
                                     MIN(version, PW_VERSION_CLIENT), 0);
    if (probe->client) pw_client_add_listener(probe->client, &probe->client_listener, &client_events, probe);
}

static void registry_global(void *data, uint32_t id, uint32_t permissions, const char *type,
                             uint32_t version, const struct spa_dict *props)
{
    (void)permissions;
    struct identity_probe *probe = data;
    if (!strcmp(type, PW_TYPE_INTERFACE_Client)) {
        g_hash_table_insert(probe->clients, GUINT_TO_POINTER(id), GUINT_TO_POINTER(version));
        bind_client(probe);
        return;
    }
    if (strcmp(type, PW_TYPE_INTERFACE_Node) || !props || probe->target_seen) return;
    uint64_t serial, client;
    if (!number(spa_dict_lookup(props, PW_KEY_OBJECT_SERIAL), &serial) ||
        (probe->node_serial ? serial != probe->node_serial : id != probe->node_id) ||
        !number(spa_dict_lookup(props, PW_KEY_CLIENT_ID), &client) || client > UINT32_MAX) return;
    probe->target_seen = TRUE;
    probe->client_id = (uint32_t)client;
    bind_client(probe);
}

static const struct pw_registry_events registry_events = {
    PW_VERSION_REGISTRY_EVENTS, .global = registry_global};

static void core_info(void *data, const struct pw_core_info *info)
{
    struct identity_probe *probe = data;
    probe->core_seen = TRUE;
    if (info->cookie != probe->expected_cookie) {
        probe->matched = FALSE;
        pw_main_loop_quit(probe->loop);
        return;
    }
    finish_if_ready(probe);
}

static void core_error(void *data, uint32_t id, int seq, int result, const char *message)
{
    (void)id; (void)seq; (void)result; (void)message;
    struct identity_probe *probe = data;
    probe->matched = FALSE;
    pw_main_loop_quit(probe->loop);
}

static const struct pw_core_events core_events = {
    PW_VERSION_CORE_EVENTS, .info = core_info, .error = core_error};

static void timed_out(void *data, uint64_t expirations)
{
    (void)expirations;
    struct identity_probe *probe = data;
    probe->matched = FALSE;
    pw_main_loop_quit(probe->loop);
}

static void cancelled(GCancellable *cancel, gpointer data)
{
    (void)cancel;
    struct identity_probe *probe = data;
    pw_main_loop_quit(probe->loop);
}

static uint32_t kwin_process(GCancellable *cancel)
{
    GError *error = NULL;
    GDBusConnection *bus = g_bus_get_sync(G_BUS_TYPE_SESSION, cancel, &error);
    if (!bus) { g_clear_error(&error); return 0; }
    GVariant *owner = g_dbus_connection_call_sync(bus, "org.freedesktop.DBus", "/org/freedesktop/DBus",
        "org.freedesktop.DBus", "GetNameOwner", g_variant_new("(s)", "org.kde.KWin"),
        G_VARIANT_TYPE("(s)"), G_DBUS_CALL_FLAGS_NO_AUTO_START, 3000, cancel, &error);
    uint32_t pid = 0;
    if (owner) {
        const gchar *name;
        g_variant_get(owner, "(&s)", &name);
        GVariant *identity = g_dbus_connection_call_sync(bus, "org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "GetConnectionUnixProcessID", g_variant_new("(s)", name),
            G_VARIANT_TYPE("(u)"), G_DBUS_CALL_FLAGS_NO_AUTO_START, 3000, cancel, &error);
        if (identity) { g_variant_get(identity, "(u)", &pid); g_variant_unref(identity); }
        g_variant_unref(owner);
    }
    g_clear_error(&error);
    g_object_unref(bus);
    return pid;
}

uint32_t rgc_portal_kwin_producer(uint32_t server_cookie, uint32_t node_id, uint64_t node_serial,
                                 GCancellable *cancel)
{
    struct identity_probe probe = {.expected_cookie = server_cookie, .node_id = node_id,
                                    .node_serial = node_serial, .kwin_pid = kwin_process(cancel)};
    if (!probe.kwin_pid || g_cancellable_is_cancelled(cancel)) return 0;
    probe.loop = pw_main_loop_new(NULL);
    if (!probe.loop) return 0;
    probe.clients = g_hash_table_new(g_direct_hash, g_direct_equal);
    probe.context = pw_context_new(pw_main_loop_get_loop(probe.loop), NULL, 0);
    if (!probe.context) goto cleanup;
    probe.core = pw_context_connect(probe.context, NULL, 0);
    if (!probe.core) goto cleanup;
    pw_core_add_listener(probe.core, &probe.core_listener, &core_events, &probe);
    probe.registry = pw_core_get_registry(probe.core, PW_VERSION_REGISTRY, 0);
    if (!probe.registry) goto cleanup;
    pw_registry_add_listener(probe.registry, &probe.registry_listener, &registry_events, &probe);
    probe.timer = pw_loop_add_timer(pw_main_loop_get_loop(probe.loop), timed_out, &probe);
    if (!probe.timer) goto cleanup;
    struct timespec deadline = {.tv_sec = 3};
    struct timespec interval = {0};
    pw_loop_update_timer(pw_main_loop_get_loop(probe.loop), probe.timer, &deadline, &interval, false);
    gulong cancel_handler = g_cancellable_connect(cancel, G_CALLBACK(cancelled), &probe, NULL);
    if (!g_cancellable_is_cancelled(cancel)) pw_main_loop_run(probe.loop);
    if (cancel_handler) g_cancellable_disconnect(cancel, cancel_handler);
cleanup:
    if (probe.timer) pw_loop_destroy_source(pw_main_loop_get_loop(probe.loop), probe.timer);
    if (probe.client) pw_proxy_destroy((struct pw_proxy *)probe.client);
    if (probe.registry) pw_proxy_destroy((struct pw_proxy *)probe.registry);
    if (probe.core) pw_core_disconnect(probe.core);
    if (probe.context) pw_context_destroy(probe.context);
    g_hash_table_unref(probe.clients);
    pw_main_loop_destroy(probe.loop);
    return probe.core_seen && probe.client_seen && probe.matched && !g_cancellable_is_cancelled(cancel)
        ? probe.kwin_pid : 0;
}
