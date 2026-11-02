#include <gio/gio.h>
#include <glib-unix.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

#define ITEM_PATH "/StatusNotifierItem"
#define MENU_PATH "/Menu"
#define MENU_IFACE "com.canonical.dbusmenu"

typedef struct {
    gint id, parent;
    gboolean enabled, checked, separator;
    gint toggle;
    gchar *label, *shortcut;
} MenuItem;

typedef struct {
    const gchar *name;
    gchar *owner;
    guint watch;
} Watcher;

typedef struct { guint generation; } PendingCall;

static GMainLoop *loop;
static GDBusConnection *bus;
static GPtrArray *items, *staging;
static gchar *tooltip, *service_name, *active_owner;
static GBytes *icon;
static gint icon_width, icon_height;
static guint revision = 1, generation, name_id, registrations[3];
static gboolean visible = TRUE, name_acquired, item_registered, host_registered;
static gboolean reported_host, bus_failed;
static gint active_watcher = -1;
static Watcher watchers[] = {
    { "org.kde.StatusNotifierWatcher", NULL, 0 },
    { "org.freedesktop.StatusNotifierWatcher", NULL, 0 }
};

static const gchar item_members[] =
    "<property name='Category' type='s' access='read'/>"
    "<property name='Id' type='s' access='read'/>"
    "<property name='Title' type='s' access='read'/>"
    "<property name='Status' type='s' access='read'/>"
    "<property name='WindowId' type='u' access='read'/>"
    "<property name='IconThemePath' type='s' access='read'/>"
    "<property name='IconName' type='s' access='read'/>"
    "<property name='IconPixmap' type='a(iiay)' access='read'/>"
    "<property name='OverlayIconName' type='s' access='read'/>"
    "<property name='OverlayIconPixmap' type='a(iiay)' access='read'/>"
    "<property name='AttentionIconName' type='s' access='read'/>"
    "<property name='AttentionIconPixmap' type='a(iiay)' access='read'/>"
    "<property name='AttentionMovieName' type='s' access='read'/>"
    "<property name='ToolTip' type='(sa(iiay)ss)' access='read'/>"
    "<property name='ItemIsMenu' type='b' access='read'/>"
    "<property name='Menu' type='o' access='read'/>"
    "<method name='ContextMenu'><arg type='i' direction='in'/><arg type='i' direction='in'/></method>"
    "<method name='Activate'><arg type='i' direction='in'/><arg type='i' direction='in'/></method>"
    "<method name='SecondaryActivate'><arg type='i' direction='in'/><arg type='i' direction='in'/></method>"
    "<method name='Scroll'><arg type='i' direction='in'/><arg type='s' direction='in'/></method>"
    "<signal name='NewTitle'/><signal name='NewIcon'/><signal name='NewToolTip'/>"
    "<signal name='NewStatus'><arg type='s'/></signal>";

static const gchar menu_xml[] =
    "<node><interface name='com.canonical.dbusmenu'>"
    "<property name='Version' type='u' access='read'/>"
    "<property name='TextDirection' type='s' access='read'/>"
    "<property name='Status' type='s' access='read'/>"
    "<property name='IconThemePath' type='as' access='read'/>"
    "<method name='GetLayout'><arg type='i' direction='in'/><arg type='i' direction='in'/>"
    "<arg type='as' direction='in'/><arg type='u' direction='out'/><arg type='(ia{sv}av)' direction='out'/></method>"
    "<method name='GetGroupProperties'><arg type='ai' direction='in'/><arg type='as' direction='in'/>"
    "<arg type='a(ia{sv})' direction='out'/></method>"
    "<method name='GetProperty'><arg type='i' direction='in'/><arg type='s' direction='in'/><arg type='v' direction='out'/></method>"
    "<method name='Event'><arg type='i' direction='in'/><arg type='s' direction='in'/><arg type='v' direction='in'/><arg type='u' direction='in'/></method>"
    "<method name='EventGroup'><arg type='a(isvu)' direction='in'/><arg type='ai' direction='out'/></method>"
    "<method name='AboutToShow'><arg type='i' direction='in'/><arg type='b' direction='out'/></method>"
    "<method name='AboutToShowGroup'><arg type='ai' direction='in'/><arg type='ai' direction='out'/><arg type='ai' direction='out'/></method>"
    "<signal name='LayoutUpdated'><arg type='u'/><arg type='i'/></signal>"
    "<signal name='ItemsPropertiesUpdated'><arg type='a(ia{sv})'/><arg type='a(ias)'/></signal>"
    "<signal name='ItemActivationRequested'><arg type='i'/><arg type='u'/></signal>"
    "</interface></node>";

static void report_error(const gchar *message)
{
    gchar *encoded = g_base64_encode((const guchar *)message, strlen(message));
    g_printerr("Tray integration error: %s\n", message);
    g_print("ERROR %s\n", encoded);
    g_free(encoded);
}

static void free_item(gpointer value)
{
    MenuItem *item = value;
    g_free(item->label);
    g_free(item->shortcut);
    g_free(item);
}

static MenuItem *find_item(gint id)
{
    for (guint i = 0; i < items->len; i++) {
        MenuItem *item = g_ptr_array_index(items, i);
        if (item->id == id) return item;
    }
    return NULL;
}

static gboolean has_children(gint id)
{
    for (guint i = 0; i < items->len; i++)
        if (((MenuItem *)g_ptr_array_index(items, i))->parent == id) return TRUE;
    return FALSE;
}

static GVariant *shortcut_variant(const gchar *shortcut)
{
    GVariantBuilder outer, inner;
    g_variant_builder_init(&outer, G_VARIANT_TYPE("aas"));
    if (*shortcut) {
        g_variant_builder_init(&inner, G_VARIANT_TYPE("as"));
        gchar **tokens = g_strsplit(shortcut, "\t", -1);
        for (gchar **token = tokens; *token; token++) {
            const gchar *name = *token;
            if (g_str_equal(name, "Ctrl")) name = "Control";
            else if (g_str_equal(name, "Meta")) name = "Super";
            if (*name) g_variant_builder_add(&inner, "s", name);
        }
        g_strfreev(tokens);
        g_variant_builder_add_value(&outer, g_variant_builder_end(&inner));
    }
    return g_variant_builder_end(&outer);
}

static GVariant *item_property(MenuItem *item, gint id, const gchar *name)
{
    if (g_str_equal(name, "visible")) return g_variant_new_boolean(TRUE);
    if (g_str_equal(name, "enabled")) return g_variant_new_boolean(item ? item->enabled : TRUE);
    if (g_str_equal(name, "label")) return g_variant_new_string(item ? item->label : "");
    if (g_str_equal(name, "type")) return g_variant_new_string(item && item->separator ? "separator" : "standard");
    if (g_str_equal(name, "children-display")) return g_variant_new_string(has_children(id) ? "submenu" : "");
    if (g_str_equal(name, "shortcut")) return shortcut_variant(item ? item->shortcut : "");
    if (g_str_equal(name, "toggle-type") && item && item->toggle)
        return g_variant_new_string(item->toggle == 2 ? "radio" : "checkmark");
    if (g_str_equal(name, "toggle-state") && item && item->toggle)
        return g_variant_new_int32(item->checked ? 1 : 0);
    return NULL;
}

static GVariant *properties(gint id, const gchar *const *requested)
{
    static const gchar *all[] = { "visible", "enabled", "label", "type", "children-display",
        "shortcut", "toggle-type", "toggle-state", NULL };
    const gchar *const *names = requested && requested[0] ? requested : all;
    MenuItem *item = find_item(id);
    GVariantBuilder result;
    g_variant_builder_init(&result, G_VARIANT_TYPE("a{sv}"));
    for (; *names; names++) {
        GVariant *value = item_property(item, id, *names);
        if (value) g_variant_builder_add(&result, "{sv}", *names, value);
    }
    return g_variant_builder_end(&result);
}

static GVariant *layout(gint id, gint depth, const gchar *const *requested)
{
    GVariantBuilder children;
    g_variant_builder_init(&children, G_VARIANT_TYPE("av"));
    if (depth != 0) {
        for (guint i = 0; i < items->len; i++) {
            MenuItem *item = g_ptr_array_index(items, i);
            if (item->parent == id)
                g_variant_builder_add(&children, "v", layout(item->id, depth < 0 ? -1 : depth - 1, requested));
        }
    }
    return g_variant_new("(i@a{sv}@av)", id, properties(id, requested), g_variant_builder_end(&children));
}

static gboolean event(gint id, const gchar *name)
{
    MenuItem *item = find_item(id);
    if (!item) return FALSE;
    if (g_str_equal(name, "clicked") && item->enabled && !item->separator)
        g_print("CLICK %d\n", id);
    else if (g_str_equal(name, "opened")) g_print("ABOUT %d\n", id);
    return TRUE;
}

static void invalid_id(GDBusMethodInvocation *invocation)
{
    g_dbus_method_invocation_return_dbus_error(invocation, "com.canonical.dbusmenu.Error.InvalidMenuItem",
        "The requested menu item does not exist.");
}

static void menu_method(GDBusConnection *connection, const gchar *sender, const gchar *path,
    const gchar *interface, const gchar *method, GVariant *parameters, GDBusMethodInvocation *invocation,
    gpointer data)
{
    (void)connection; (void)sender; (void)path; (void)interface; (void)data;
    if (g_str_equal(method, "GetLayout")) {
        gint id, depth; gchar **requested;
        g_variant_get(parameters, "(ii^as)", &id, &depth, &requested);
        if (id != 0 && !find_item(id)) invalid_id(invocation);
        else g_dbus_method_invocation_return_value(invocation,
            g_variant_new("(u@(ia{sv}av))", revision, layout(id, depth, (const gchar *const *)requested)));
        g_strfreev(requested);
    } else if (g_str_equal(method, "GetGroupProperties")) {
        GVariant *ids; gchar **requested; GVariantBuilder result;
        g_variant_get(parameters, "(@ai^as)", &ids, &requested);
        g_variant_builder_init(&result, G_VARIANT_TYPE("a(ia{sv})"));
        if (g_variant_n_children(ids) == 0) {
            for (guint i = 0; i < items->len; i++) {
                MenuItem *item = g_ptr_array_index(items, i);
                g_variant_builder_add(&result, "(i@a{sv})", item->id, properties(item->id, (const gchar *const *)requested));
            }
        } else {
            GVariantIter iterator; gint id;
            g_variant_iter_init(&iterator, ids);
            while (g_variant_iter_next(&iterator, "i", &id))
                if (id == 0 || find_item(id))
                    g_variant_builder_add(&result, "(i@a{sv})", id, properties(id, (const gchar *const *)requested));
        }
        g_dbus_method_invocation_return_value(invocation, g_variant_new("(@a(ia{sv}))", g_variant_builder_end(&result)));
        g_variant_unref(ids); g_strfreev(requested);
    } else if (g_str_equal(method, "GetProperty")) {
        gint id; const gchar *name;
        g_variant_get(parameters, "(i&s)", &id, &name);
        MenuItem *item = find_item(id);
        GVariant *value = id == 0 || item ? item_property(item, id, name) : NULL;
        if (value) g_dbus_method_invocation_return_value(invocation, g_variant_new("(v)", value));
        else invalid_id(invocation);
    } else if (g_str_equal(method, "Event")) {
        gint id; const gchar *name; GVariant *value; guint time;
        g_variant_get(parameters, "(i&svu)", &id, &name, &value, &time);
        (void)time; event(id, name); g_variant_unref(value);
        g_dbus_method_invocation_return_value(invocation, NULL);
    } else if (g_str_equal(method, "EventGroup")) {
        GVariant *events; GVariantIter iterator; GVariantBuilder errors;
        gint id; const gchar *name; GVariant *value; guint time;
        g_variant_get(parameters, "(@a(isvu))", &events);
        g_variant_iter_init(&iterator, events);
        g_variant_builder_init(&errors, G_VARIANT_TYPE("ai"));
        while (g_variant_iter_next(&iterator, "(i&svu)", &id, &name, &value, &time)) {
            if (!event(id, name)) g_variant_builder_add(&errors, "i", id);
            g_variant_unref(value);
        }
        g_dbus_method_invocation_return_value(invocation, g_variant_new("(@ai)", g_variant_builder_end(&errors)));
        g_variant_unref(events);
    } else if (g_str_equal(method, "AboutToShow")) {
        gint id; g_variant_get(parameters, "(i)", &id);
        if (id != 0 && !find_item(id)) invalid_id(invocation);
        else {
            g_print("ABOUT %d\n", id);
            g_dbus_method_invocation_return_value(invocation, g_variant_new("(b)", FALSE));
        }
    } else if (g_str_equal(method, "AboutToShowGroup")) {
        GVariant *ids; GVariantIter iterator; GVariantBuilder errors, updates; gint id;
        g_variant_get(parameters, "(@ai)", &ids);
        g_variant_iter_init(&iterator, ids);
        g_variant_builder_init(&errors, G_VARIANT_TYPE("ai"));
        g_variant_builder_init(&updates, G_VARIANT_TYPE("ai"));
        while (g_variant_iter_next(&iterator, "i", &id)) {
            if (id == 0 || find_item(id)) g_print("ABOUT %d\n", id);
            else g_variant_builder_add(&errors, "i", id);
        }
        g_dbus_method_invocation_return_value(invocation,
            g_variant_new("(@ai@ai)", g_variant_builder_end(&updates), g_variant_builder_end(&errors)));
        g_variant_unref(ids);
    }
}

static GVariant *menu_property(GDBusConnection *connection, const gchar *sender, const gchar *path,
    const gchar *interface, const gchar *name, GError **error, gpointer data)
{
    (void)connection; (void)sender; (void)path; (void)interface; (void)error; (void)data;
    if (g_str_equal(name, "Version")) return g_variant_new_uint32(3);
    if (g_str_equal(name, "TextDirection")) return g_variant_new_string("ltr");
    if (g_str_equal(name, "Status")) return g_variant_new_string("normal");
    if (g_str_equal(name, "IconThemePath")) return g_variant_new_strv(NULL, 0);
    return NULL;
}

static GVariant *pixmaps(gboolean include_icon)
{
    GVariantBuilder result;
    g_variant_builder_init(&result, G_VARIANT_TYPE("a(iiay)"));
    if (include_icon && icon) {
        gsize length; const guint8 *bytes = g_bytes_get_data(icon, &length);
        g_variant_builder_add(&result, "(ii@ay)", icon_width, icon_height,
            g_variant_new_fixed_array(G_VARIANT_TYPE_BYTE, bytes, length, 1));
    }
    return g_variant_builder_end(&result);
}

static GVariant *item_get_property(GDBusConnection *connection, const gchar *sender, const gchar *path,
    const gchar *interface, const gchar *name, GError **error, gpointer data)
{
    (void)connection; (void)sender; (void)path; (void)interface; (void)error; (void)data;
    if (g_str_equal(name, "Category")) return g_variant_new_string("ApplicationStatus");
    if (g_str_equal(name, "Id")) return g_variant_new_string("ritsukagegif");
    if (g_str_equal(name, "Title")) return g_variant_new_string(tooltip);
    if (g_str_equal(name, "Status")) return g_variant_new_string(visible ? "Active" : "Passive");
    if (g_str_equal(name, "WindowId")) return g_variant_new_uint32(0);
    if (g_str_equal(name, "Menu")) return g_variant_new_object_path(MENU_PATH);
    if (g_str_equal(name, "ItemIsMenu")) return g_variant_new_boolean(FALSE);
    if (g_str_equal(name, "IconPixmap")) return pixmaps(TRUE);
    if (g_str_equal(name, "OverlayIconPixmap") || g_str_equal(name, "AttentionIconPixmap")) return pixmaps(FALSE);
    if (g_str_equal(name, "ToolTip")) return g_variant_new("(s@a(iiay)ss)", "", pixmaps(FALSE), tooltip, "");
    return g_variant_new_string("");
}

static void item_method(GDBusConnection *connection, const gchar *sender, const gchar *path,
    const gchar *interface, const gchar *method, GVariant *parameters, GDBusMethodInvocation *invocation,
    gpointer data)
{
    (void)connection; (void)sender; (void)path; (void)interface; (void)parameters; (void)data;
    if (visible) {
        if (g_str_equal(method, "Activate")) g_print("ACTIVATE\n");
        else if (g_str_equal(method, "SecondaryActivate")) g_print("SECONDARY\n");
        else if (g_str_equal(method, "ContextMenu")) {
            gint x, y; g_variant_get(parameters, "(ii)", &x, &y);
            g_print("ABOUT 0\nCONTEXT %d %d\n", x, y);
        }
    }
    g_dbus_method_invocation_return_value(invocation, NULL);
}

static void emit_item_signal(const gchar *name, GVariant *parameters)
{
    if (!bus) return;
    if (parameters) g_variant_ref_sink(parameters);
    for (guint i = 0; i < 2; i++) {
        const gchar *interface = i == 0 ? "org.kde.StatusNotifierItem" : "org.freedesktop.StatusNotifierItem";
        g_dbus_connection_emit_signal(bus, NULL, ITEM_PATH, interface, name, parameters, NULL);
    }
    if (parameters) g_variant_unref(parameters);
}

static void report_host(void)
{
    gboolean available = visible && active_watcher >= 0 && item_registered && host_registered;
    if (reported_host == available) return;
    reported_host = available;
    g_print("HOST %d\n", available);
}

static void host_reply(GObject *object, GAsyncResult *result, gpointer data)
{
    PendingCall *pending = data;
    GError *error = NULL;
    GVariant *reply = g_dbus_connection_call_finish(G_DBUS_CONNECTION(object), result, &error);
    if (pending->generation == generation) {
        host_registered = FALSE;
        if (reply) {
            GVariant *value; g_variant_get(reply, "(v)", &value);
            if (g_variant_is_of_type(value, G_VARIANT_TYPE_BOOLEAN)) host_registered = g_variant_get_boolean(value);
            g_variant_unref(value);
        }
        report_host();
    }
    g_clear_error(&error);
    if (reply) g_variant_unref(reply);
    g_free(pending);
}

static void refresh_host(void)
{
    if (active_watcher < 0) return;
    PendingCall *pending = g_new(PendingCall, 1); pending->generation = generation;
    Watcher *watcher = &watchers[active_watcher];
    g_dbus_connection_call(bus, watcher->owner, "/StatusNotifierWatcher", "org.freedesktop.DBus.Properties", "Get",
        g_variant_new("(ss)", watcher->name, "IsStatusNotifierHostRegistered"), G_VARIANT_TYPE("(v)"),
        G_DBUS_CALL_FLAGS_NONE, 5000, NULL, host_reply, pending);
}

static void registered_reply(GObject *object, GAsyncResult *result, gpointer data)
{
    PendingCall *pending = data; GError *error = NULL;
    GVariant *reply = g_dbus_connection_call_finish(G_DBUS_CONNECTION(object), result, &error);
    if (pending->generation == generation) {
        item_registered = reply != NULL;
        if (error) report_error(error->message);
        report_host();
        if (item_registered) refresh_host();
    }
    g_clear_error(&error);
    if (reply) g_variant_unref(reply);
    g_free(pending);
}

static void register_item(void)
{
    if (active_watcher < 0 || !name_acquired) return;
    Watcher *watcher = &watchers[active_watcher];
    PendingCall *pending = g_new(PendingCall, 1); pending->generation = generation;
    g_dbus_connection_call(bus, watcher->owner, "/StatusNotifierWatcher", watcher->name,
        "RegisterStatusNotifierItem", g_variant_new("(s)", service_name), G_VARIANT_TYPE_UNIT,
        G_DBUS_CALL_FLAGS_NONE, 5000, NULL, registered_reply, pending);
}

static void select_watcher(void)
{
    gint selected = watchers[0].owner ? 0 : watchers[1].owner ? 1 : -1;
    const gchar *selected_owner = selected < 0 ? NULL : watchers[selected].owner;
    if (selected == active_watcher && g_strcmp0(selected_owner, active_owner) == 0) return;
    active_watcher = selected;
    g_free(active_owner); active_owner = g_strdup(selected_owner);
    generation++;
    item_registered = host_registered = FALSE;
    report_host();
    register_item();
}

static void watcher_appeared(GDBusConnection *connection, const gchar *name, const gchar *owner, gpointer data)
{
    (void)connection; (void)name;
    Watcher *watcher = data;
    g_free(watcher->owner); watcher->owner = g_strdup(owner);
    select_watcher();
}

static void watcher_vanished(GDBusConnection *connection, const gchar *name, gpointer data)
{
    (void)connection; (void)name;
    Watcher *watcher = data;
    g_clear_pointer(&watcher->owner, g_free);
    select_watcher();
}

static void watcher_signal(GDBusConnection *connection, const gchar *sender, const gchar *path,
    const gchar *interface, const gchar *name, GVariant *parameters, gpointer data)
{
    (void)connection; (void)path; (void)interface; (void)name; (void)parameters; (void)data;
    if (active_watcher >= 0 && g_strcmp0(sender, watchers[active_watcher].owner) == 0) refresh_host();
}

static void acquired_name(GDBusConnection *connection, const gchar *name, gpointer data)
{
    (void)connection; (void)name; (void)data;
    name_acquired = TRUE; register_item();
}

static void lost_name(GDBusConnection *connection, const gchar *name, gpointer data)
{
    (void)connection; (void)name; (void)data;
    name_acquired = item_registered = FALSE; report_host();
    report_error("The tray service bus name could not be acquired.");
}

static gboolean stop(gpointer data)
{
    (void)data; g_main_loop_quit(loop); return G_SOURCE_REMOVE;
}

static void bus_closed(GDBusConnection *connection, gboolean remote_peer_vanished, GError *error, gpointer data)
{
    (void)connection; (void)remote_peer_vanished; (void)data;
    bus_failed = TRUE;
    item_registered = host_registered = FALSE;
    report_host();
    report_error(error ? error->message : "The tray session bus connection was closed.");
    g_main_loop_quit(loop);
}

static gchar *decode_text(const gchar *value)
{
    if (g_str_equal(value, "-")) return g_strdup("");
    gsize length; guchar *bytes = g_base64_decode(value, &length);
    if (!g_utf8_validate((const gchar *)bytes, length, NULL) || memchr(bytes, 0, length)) {
        g_free(bytes); return NULL;
    }
    gchar *text = g_strndup((const gchar *)bytes, length); g_free(bytes); return text;
}

static gboolean parse_int(const gchar *value, gint *result)
{
    if (!value || !*value) return FALSE;
    gchar *end; gint64 parsed = g_ascii_strtoll(value, &end, 10);
    if (*end || parsed < G_MININT || parsed > G_MAXINT) return FALSE;
    *result = (gint)parsed; return TRUE;
}

static gboolean valid_menu(void)
{
    GHashTable *ids = g_hash_table_new(g_direct_hash, g_direct_equal);
    gboolean valid = TRUE;
    for (guint i = 0; i < staging->len; i++) {
        MenuItem *item = g_ptr_array_index(staging, i);
        if (item->id <= 0 || g_hash_table_contains(ids, GINT_TO_POINTER(item->id))) { valid = FALSE; break; }
        if (item->parent != 0 && !g_hash_table_contains(ids, GINT_TO_POINTER(item->parent))) { valid = FALSE; break; }
        g_hash_table_add(ids, GINT_TO_POINTER(item->id));
    }
    g_hash_table_unref(ids);
    return valid;
}

static gboolean command(gchar *line)
{
    gchar **parts = g_strsplit(g_strstrip(line), " ", -1);
    guint count = g_strv_length(parts); gboolean valid = TRUE;
    if (count == 1 && g_str_equal(parts[0], "RESET")) g_ptr_array_set_size(staging, 0);
    else if (count == 1 && g_str_equal(parts[0], "COMMIT")) {
        valid = valid_menu();
        if (valid) {
            GPtrArray *previous = items; items = staging; staging = previous;
            g_ptr_array_set_size(staging, 0); revision++;
            if (bus) g_dbus_connection_emit_signal(bus, NULL, MENU_PATH, MENU_IFACE, "LayoutUpdated",
                g_variant_new("(ui)", revision, 0), NULL);
        }
    } else if (count == 9 && g_str_equal(parts[0], "ITEM")) {
        gint id, parent, enabled, toggle, checked, separator;
        valid = parse_int(parts[1], &id) && parse_int(parts[2], &parent) && parse_int(parts[3], &enabled) &&
            parse_int(parts[4], &toggle) && parse_int(parts[5], &checked) && parse_int(parts[6], &separator) &&
            (enabled == 0 || enabled == 1) && toggle >= 0 && toggle <= 2 &&
            (checked == 0 || checked == 1) && (separator == 0 || separator == 1);
        if (valid) {
            MenuItem *item = g_new0(MenuItem, 1);
            item->id = id; item->parent = parent; item->enabled = enabled; item->toggle = toggle;
            item->checked = checked; item->separator = separator;
            item->label = decode_text(parts[7]); item->shortcut = decode_text(parts[8]);
            valid = item->label && item->shortcut;
            if (valid) g_ptr_array_add(staging, item); else free_item(item);
        }
    } else if (count == 4 && g_str_equal(parts[0], "ICON")) {
        gint width, height;
        valid = parse_int(parts[1], &width) && parse_int(parts[2], &height) && width > 0 && height > 0;
        if (valid) {
            gsize length; guchar *pixels = g_base64_decode(parts[3], &length);
            valid = (guint64)width * (guint64)height * 4 == length;
            if (valid) {
                g_clear_pointer(&icon, g_bytes_unref); icon = g_bytes_new_take(pixels, length);
                icon_width = width; icon_height = height; emit_item_signal("NewIcon", NULL);
            } else g_free(pixels);
        }
    } else if (count == 2 && g_str_equal(parts[0], "TEXT")) {
        gchar *text = decode_text(parts[1]); valid = text != NULL;
        if (valid) { g_free(tooltip); tooltip = text; emit_item_signal("NewTitle", NULL); emit_item_signal("NewToolTip", NULL); }
    } else if (count == 2 && g_str_equal(parts[0], "VISIBLE")) {
        gint value; valid = parse_int(parts[1], &value) && (value == 0 || value == 1);
        if (valid) { visible = value; emit_item_signal("NewStatus", g_variant_new("(s)", visible ? "Active" : "Passive")); report_host(); }
    } else if (count == 1 && g_str_equal(parts[0], "QUIT")) g_main_loop_quit(loop);
    else valid = FALSE;
    g_strfreev(parts); return valid;
}

static gboolean input(GIOChannel *channel, GIOCondition condition, gpointer data)
{
    (void)data;
    if (condition & (G_IO_ERR | G_IO_NVAL)) return stop(NULL);
    while (TRUE) {
        gchar *line = NULL; GError *error = NULL;
        GIOStatus status = g_io_channel_read_line(channel, &line, NULL, NULL, &error);
        if (status == G_IO_STATUS_NORMAL) {
            if (!command(line)) report_error("The tray command is invalid.");
            g_free(line); continue;
        }
        g_free(line);
        if (status == G_IO_STATUS_EOF) return stop(NULL);
        if (status == G_IO_STATUS_ERROR) { report_error(error->message); g_error_free(error); return stop(NULL); }
        return G_SOURCE_CONTINUE;
    }
}

int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    setvbuf(stdout, NULL, _IOLBF, 0);
    g_print("HOST 0\n");
    signal(SIGPIPE, SIG_IGN);
    items = g_ptr_array_new_with_free_func(free_item);
    staging = g_ptr_array_new_with_free_func(free_item);
    tooltip = g_strdup("RitsukageGif");
    loop = g_main_loop_new(NULL, FALSE);
    GError *error = NULL;
    bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, &error);
    if (!bus) { report_error(error->message); g_error_free(error); return 1; }
    g_dbus_connection_set_exit_on_close(bus, FALSE);
    gulong closed_handler = g_signal_connect(bus, "closed", G_CALLBACK(bus_closed), NULL);
    gchar *item_xml = g_strdup_printf("<node><interface name='org.kde.StatusNotifierItem'>%s</interface>"
        "<interface name='org.freedesktop.StatusNotifierItem'>%s</interface></node>", item_members, item_members);
    GDBusNodeInfo *item_info = g_dbus_node_info_new_for_xml(item_xml, &error); g_free(item_xml);
    if (!item_info) { report_error(error->message); g_error_free(error); return 1; }
    GDBusNodeInfo *menu_info = g_dbus_node_info_new_for_xml(menu_xml, &error);
    if (!menu_info) { report_error(error->message); g_error_free(error); g_dbus_node_info_unref(item_info); return 1; }
    const GDBusInterfaceVTable item_vtable = { item_method, item_get_property, NULL, { 0 } };
    const GDBusInterfaceVTable menu_vtable = { menu_method, menu_property, NULL, { 0 } };
    for (guint i = 0; i < 2; i++) {
        registrations[i] = g_dbus_connection_register_object(bus, ITEM_PATH, item_info->interfaces[i], &item_vtable, NULL, NULL, &error);
        if (!registrations[i]) { report_error(error->message); g_error_free(error); return 1; }
    }
    registrations[2] = g_dbus_connection_register_object(bus, MENU_PATH, menu_info->interfaces[0], &menu_vtable, NULL, NULL, &error);
    if (!registrations[2]) { report_error(error->message); g_error_free(error); return 1; }
    g_dbus_node_info_unref(item_info); g_dbus_node_info_unref(menu_info);
    service_name = g_strdup_printf("org.kde.StatusNotifierItem-%d-1", getpid());
    name_id = g_bus_own_name_on_connection(bus, service_name, G_BUS_NAME_OWNER_FLAGS_NONE,
        acquired_name, lost_name, NULL, NULL);
    for (guint i = 0; i < G_N_ELEMENTS(watchers); i++)
        watchers[i].watch = g_bus_watch_name_on_connection(bus, watchers[i].name, G_BUS_NAME_WATCHER_FLAGS_NONE,
            watcher_appeared, watcher_vanished, &watchers[i], NULL);
    guint host_signal = g_dbus_connection_signal_subscribe(bus, NULL, NULL, NULL,
        "/StatusNotifierWatcher", NULL, G_DBUS_SIGNAL_FLAGS_NONE, watcher_signal, NULL, NULL);
    GIOChannel *channel = g_io_channel_unix_new(STDIN_FILENO);
    g_io_channel_set_flags(channel, g_io_channel_get_flags(channel) | G_IO_FLAG_NONBLOCK, NULL);
    guint input_source = g_io_add_watch(channel, G_IO_IN | G_IO_HUP | G_IO_ERR | G_IO_NVAL, input, NULL);
    guint term_source = g_unix_signal_add(SIGTERM, stop, NULL);
    guint int_source = g_unix_signal_add(SIGINT, stop, NULL);
    g_print("READY\n");
    g_main_loop_run(loop);
    if (g_main_context_find_source_by_id(g_main_context_default(), input_source)) g_source_remove(input_source);
    if (g_main_context_find_source_by_id(g_main_context_default(), term_source)) g_source_remove(term_source);
    if (g_main_context_find_source_by_id(g_main_context_default(), int_source)) g_source_remove(int_source);
    g_io_channel_unref(channel);
    for (guint i = 0; i < G_N_ELEMENTS(watchers); i++) { g_bus_unwatch_name(watchers[i].watch); g_free(watchers[i].owner); }
    g_bus_unown_name(name_id);
    g_dbus_connection_signal_unsubscribe(bus, host_signal);
    for (guint i = 0; i < G_N_ELEMENTS(registrations); i++) g_dbus_connection_unregister_object(bus, registrations[i]);
    g_signal_handler_disconnect(bus, closed_handler);
    g_object_unref(bus); g_main_loop_unref(loop);
    g_ptr_array_unref(items); g_ptr_array_unref(staging);
    g_clear_pointer(&icon, g_bytes_unref); g_free(tooltip); g_free(service_name); g_free(active_owner);
    return bus_failed ? 1 : 0;
}
