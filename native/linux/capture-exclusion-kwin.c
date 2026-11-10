#define _POSIX_C_SOURCE 200809L
#include <gio/gio.h>
#include <glib-unix.h>
#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <sys/prctl.h>
#include <unistd.h>

#define CALLBACK_PATH "/org/ritsukage/CaptureExclusion"
#define CALLBACK_INTERFACE "org.ritsukage.CaptureExclusion"
#define TITLE_PREFIX "RitsukageGif.CaptureOverlay."

typedef struct {
    GDBusConnection *bus;
    GMainLoop *loop;
    gchar *kwin_owner;
    gchar *window_id;
    gboolean acknowledged, confirmed, previous, prepared, stopping;
    const gchar *phase;
    gchar *reason;
} Lease;

static const gchar introspection[] =
    "<node><interface name='" CALLBACK_INTERFACE "'>"
    "<method name='Acknowledge'><arg type='s' direction='in'/>"
    "<arg type='b' direction='in'/><arg type='b' direction='in'/>"
    "<arg type='s' direction='in'/><arg type='s' direction='in'/></method></interface></node>";

static void acknowledge(GDBusConnection *bus, const gchar *sender, const gchar *path,
                        const gchar *interface, const gchar *method, GVariant *parameters,
                        GDBusMethodInvocation *invocation, gpointer data)
{
    (void)bus; (void)path; (void)interface; (void)method;
    Lease *lease = data;
    if (g_strcmp0(sender, lease->kwin_owner)) {
        g_dbus_method_invocation_return_error_literal(invocation, G_IO_ERROR,
                                                       G_IO_ERROR_PERMISSION_DENIED, "Unexpected sender");
        return;
    }
    const gchar *phase, *reason, *window_id;
    gboolean confirmed, previous;
    g_variant_get(parameters, "(&sbb&s&s)", &phase, &confirmed, &previous, &reason, &window_id);
    if (!g_strcmp0(phase, "prepared")) {
        if (!g_uuid_string_is_valid(window_id)) {
            g_dbus_method_invocation_return_error_literal(invocation, G_IO_ERROR,
                G_IO_ERROR_INVALID_DATA, "The target window identity is invalid.");
            return;
        }
        lease->prepared = TRUE;
        lease->previous = previous;
        g_free(lease->window_id);
        lease->window_id = g_strdup(window_id);
    } else if (!g_strcmp0(phase, lease->phase)) {
        lease->acknowledged = TRUE;
        lease->confirmed = confirmed && g_uuid_string_is_valid(window_id) &&
                           !g_strcmp0(window_id, lease->window_id);
        lease->previous = previous;
        g_free(lease->reason);
        lease->reason = g_strdup(reason);
        g_main_loop_quit(lease->loop);
    }
    g_dbus_method_invocation_return_value(invocation, NULL);
}

static const GDBusInterfaceVTable callback_vtable = {.method_call = acknowledge};

static gboolean quit_loop(gpointer data)
{
    g_main_loop_quit(data);
    return G_SOURCE_REMOVE;
}

static gboolean stdin_closed(GIOChannel *channel, GIOCondition condition, gpointer data)
{
    (void)channel; (void)condition;
    g_main_loop_quit(data);
    return G_SOURCE_CONTINUE;
}

static gboolean request_stop(gpointer data)
{
    Lease *lease = data;
    lease->stopping = TRUE;
    g_main_loop_quit(lease->loop);
    return G_SOURCE_CONTINUE;
}

static GVariant *call(Lease *lease, const gchar *path, const gchar *interface,
                      const gchar *method, GVariant *arguments, const GVariantType *reply)
{
    GError *error = NULL;
    GVariant *result = g_dbus_connection_call_sync(lease->bus, lease->kwin_owner, path, interface,
        method, arguments, reply, G_DBUS_CALL_FLAGS_NONE, 10000, NULL, &error);
    if (error) {
        fprintf(stderr, "KWin capture exclusion %s failed: %s\n", method, error->message);
        g_error_free(error);
    }
    return result;
}

static gboolean write_script(const gchar *content, gchar **path)
{
    GError *error = NULL;
    int fd = g_file_open_tmp("ritsukage-capture-exclusion-XXXXXX", path, &error);
    if (fd < 0) {
        fprintf(stderr, "Capture exclusion temporary script failed: %s\n", error->message);
        g_error_free(error);
        return FALSE;
    }
    size_t left = strlen(content);
    const gchar *next = content;
    while (left) {
        ssize_t count = write(fd, next, left);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) {
            close(fd);
            unlink(*path);
            return FALSE;
        }
        next += count;
        left -= (size_t)count;
    }
    if (close(fd) == 0) return TRUE;
    unlink(*path);
    return FALSE;
}

static gboolean run_script(Lease *lease, const gchar *name, const gchar *content)
{
    gchar *path = NULL;
    if (!write_script(content, &path)) {
        g_free(path);
        return FALSE;
    }
    GVariant *loaded = call(lease, "/Scripting", "org.kde.kwin.Scripting", "loadScript",
                            g_variant_new("(ss)", path, name), G_VARIANT_TYPE("(i)"));
    gboolean ran = FALSE;
    if (loaded) {
        gint id;
        g_variant_get(loaded, "(i)", &id);
        g_variant_unref(loaded);
        if (id >= 0) {
            gchar *object_path = g_strdup_printf("/Scripting/Script%d", id);
            GVariant *result = call(lease, object_path, "org.kde.kwin.Script", "run", NULL,
                                    G_VARIANT_TYPE_UNIT);
            ran = result != NULL;
            if (result) g_variant_unref(result);
            g_free(object_path);
        }
    }
    unlink(path);
    g_free(path);
    return ran;
}

static gboolean unload(Lease *lease, const gchar *name)
{
    GVariant *result = call(lease, "/Scripting", "org.kde.kwin.Scripting", "unloadScript",
                            g_variant_new("(s)", name), G_VARIANT_TYPE("(b)"));
    if (!result) return FALSE;
    g_variant_unref(result);
    return TRUE;
}

static gboolean wait_acknowledgement(Lease *lease)
{
    if (lease->acknowledged) return lease->confirmed;
    if (lease->stopping) return FALSE;
    guint timeout = g_timeout_add_seconds(10, quit_loop, lease->loop);
    g_main_loop_run(lease->loop);
    if (lease->acknowledged || lease->stopping) g_source_remove(timeout);
    return lease->acknowledged && lease->confirmed;
}

int main(int argc, char **argv)
{
    if (argc != 3 || !g_str_has_prefix(argv[1], TITLE_PREFIX) ||
        strlen(argv[1]) != strlen(TITLE_PREFIX) + 32) return 64;
    gchar *pid_end = NULL;
    guint64 producer_pid = g_ascii_strtoull(argv[2], &pid_end, 10);
    if (!g_ascii_isdigit(*argv[2]) || !pid_end || *pid_end || !producer_pid || producer_pid > G_MAXINT)
        return 64;
    const gchar *token = argv[1] + strlen(TITLE_PREFIX);
    for (const gchar *p = token; *p; ++p) if (!g_ascii_isxdigit(*p)) return 64;
    signal(SIGPIPE, SIG_IGN);
    pid_t owner_pid = getppid();
    if (prctl(PR_SET_PDEATHSIG, SIGTERM) || getppid() != owner_pid) return 74;

    Lease lease = {.phase = "installed"};
    const gchar *unavailable_reason = "CompositorUnavailable";
    GError *error = NULL;
    guint term = 0, interrupt = 0;
    lease.bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, &error);
    if (!lease.bus) goto unavailable;
    GVariant *owner = g_dbus_connection_call_sync(lease.bus, "org.freedesktop.DBus",
        "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetNameOwner",
        g_variant_new("(s)", "org.kde.KWin"), G_VARIANT_TYPE("(s)"),
        G_DBUS_CALL_FLAGS_NONE, 10000, NULL, &error);
    if (!owner) goto unavailable;
    g_variant_get(owner, "(s)", &lease.kwin_owner);
    g_variant_unref(owner);
    GVariant *identity = g_dbus_connection_call_sync(lease.bus, "org.freedesktop.DBus",
        "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetConnectionUnixProcessID",
        g_variant_new("(s)", lease.kwin_owner), G_VARIANT_TYPE("(u)"),
        G_DBUS_CALL_FLAGS_NO_AUTO_START, 10000, NULL, &error);
    if (!identity) goto unavailable;
    guint32 current_pid;
    g_variant_get(identity, "(u)", &current_pid);
    g_variant_unref(identity);
    if (current_pid != producer_pid) {
        unavailable_reason = "ProducerChanged";
        goto unavailable;
    }
    lease.loop = g_main_loop_new(NULL, FALSE);
    term = g_unix_signal_add(SIGTERM, request_stop, &lease);
    interrupt = g_unix_signal_add(SIGINT, request_stop, &lease);
    GDBusNodeInfo *info = g_dbus_node_info_new_for_xml(introspection, &error);
    if (!info) goto unavailable;
    guint registration = g_dbus_connection_register_object(lease.bus, CALLBACK_PATH, info->interfaces[0],
        &callback_vtable, &lease, NULL, &error);
    g_dbus_node_info_unref(info);
    if (!registration) goto unavailable;

    const gchar *destination = g_dbus_connection_get_unique_name(lease.bus);
    gchar *name = g_strdup_printf("RitsukageGif.capture-exclusion.%s", token);
    gchar *script = g_strdup_printf(
        "(function(){var ws=workspace.windowList();var found=[];"
        "function ack(phase,ok,old,reason,id){callDBus('%s','" CALLBACK_PATH "','"
        CALLBACK_INTERFACE "','Acknowledge',phase,ok,old,reason,id);}"
        "for(var i=0;i<ws.length;i++)if(ws[i].pid===%u&&ws[i].captionNormal==='%s')found.push(ws[i]);"
        "if(found.length!==1){ack('installed',false,false,found.length?'AmbiguousTarget':'TargetNotFound','');return;}"
        "var w=found[0];if(typeof w.excludeFromCapture!=='boolean'){"
        "ack('installed',false,false,'ExclusionPropertyUnavailable','');return;}"
        "var id=String(w.internalId).replace(/^[{]|[}]$/g,'');"
        "if(!/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(id)){"
        "ack('installed',false,false,'TargetIdentityUnavailable','');return;}"
        "var old=w.excludeFromCapture;ack('prepared',true,old,'',id);w.excludeFromCapture=true;"
        "ack('installed',w.excludeFromCapture===true,old,'ExclusionRejected',id);})();",
        destination, (guint)owner_pid, argv[1]);
    gboolean installed = run_script(&lease, name, script) && wait_acknowledgement(&lease);
    g_free(script);
    if (installed) {
        puts("{\"status\":\"ready\",\"capability\":\"kwin-portal-window-exclusion\"}");
        fflush(stdout);
        GIOChannel *input = g_io_channel_unix_new(STDIN_FILENO);
        guint input_watch = g_io_add_watch(input, G_IO_HUP | G_IO_ERR | G_IO_NVAL, stdin_closed, lease.loop);
        g_main_loop_run(lease.loop);
        g_source_remove(input_watch);
        g_io_channel_unref(input);
    } else {
        fprintf(stderr, "KWin capture exclusion unavailable: %s\n",
                lease.reason ? lease.reason : "AcknowledgementTimedOut");
        puts("{\"status\":\"unavailable\",\"reason\":\"ExclusionNotAcknowledged\"}");
        fflush(stdout);
    }

    gboolean restored = !lease.prepared;
    gboolean unloaded = TRUE;
    if (lease.prepared) {
        lease.stopping = FALSE;
        lease.phase = "restored";
        lease.acknowledged = lease.confirmed = FALSE;
        gchar *cleanup_name = g_strconcat(name, ".cleanup", NULL);
        gchar *cleanup = g_strdup_printf(
            "(function(){var ws=workspace.windowList();var ok=true;for(var i=0;i<ws.length;i++){"
            "var w=ws[i];if(w.pid===%u&&String(w.internalId).replace(/^[{]|[}]$/g,'')==='%s'){w.excludeFromCapture=%s;"
            "ok=ok&&(w.excludeFromCapture===%s);}}callDBus('%s','" CALLBACK_PATH "','"
            CALLBACK_INTERFACE "','Acknowledge','restored',ok,%s,'RestoreRejected','%s');})();",
            (guint)owner_pid, lease.window_id, lease.previous ? "true" : "false",
            lease.previous ? "true" : "false", destination, lease.previous ? "true" : "false", lease.window_id);
        restored = run_script(&lease, cleanup_name, cleanup) && wait_acknowledgement(&lease);
        if (!restored)
            fputs("KWin capture exclusion restoration was not acknowledged.\n", stderr);
        unloaded = unload(&lease, cleanup_name);
        g_free(cleanup);
        g_free(cleanup_name);
    }
    unloaded = unload(&lease, name) && unloaded;
    printf("{\"status\":\"released\",\"restored\":%s,\"unloaded\":%s}\n",
           restored ? "true" : "false", unloaded ? "true" : "false");
    fflush(stdout);
    g_free(name);
    g_dbus_connection_unregister_object(lease.bus, registration);
    g_source_remove(term);
    g_source_remove(interrupt);
    g_main_loop_unref(lease.loop);
    g_free(lease.reason);
    g_free(lease.window_id);
    g_free(lease.kwin_owner);
    g_object_unref(lease.bus);
    return installed ? (restored && unloaded ? 0 : 74) : 69;

unavailable:
    if (error) {
        fprintf(stderr, "KWin capture exclusion service unavailable: %s\n", error->message);
        g_error_free(error);
    }
    printf("{\"status\":\"unavailable\",\"reason\":\"%s\"}\n", unavailable_reason);
    fflush(stdout);
    if (term) g_source_remove(term);
    if (interrupt) g_source_remove(interrupt);
    if (lease.loop) g_main_loop_unref(lease.loop);
    g_free(lease.kwin_owner);
    if (lease.bus) g_object_unref(lease.bus);
    return 69;
}
