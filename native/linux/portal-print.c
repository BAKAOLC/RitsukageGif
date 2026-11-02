#define _POSIX_C_SOURCE 200809L
#include <errno.h>
#include <fcntl.h>
#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <math.h>
#include <poll.h>
#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/stat.h>
#include <unistd.h>

#define PORTAL "org.freedesktop.portal.Desktop"
#define PORTAL_PATH "/org/freedesktop/portal/desktop"
#define PRINT_INTERFACE "org.freedesktop.portal.Print"
#define REQUEST_INTERFACE "org.freedesktop.portal.Request"
#define POINTS_PER_MM (72.0 / 25.4)
#define MAXIMUM_PDF_BYTES (512LL * 1024 * 1024)

typedef struct {
    const char *path;
    GVariant *result;
    guint status;
    gboolean received;
} PrintRequest;

typedef struct {
    double width, height, x, y, printable_width, printable_height;
} PageGeometry;

static volatile sig_atomic_t stopping;
static pid_t parent_pid;
static char command_buffer[8192];
static size_t command_length;
static char *pdf_command;
static bool accepting_pdf;
static bool invalid_command;

static void stop_signal(int value)
{
    (void)value;
    stopping = 1;
}

static void read_commands(void)
{
    struct pollfd input = {.fd = STDIN_FILENO, .events = POLLIN | POLLHUP};
    int ready = poll(&input, 1, 0);
    if (ready <= 0)
        return;
    char bytes[1024];
    ssize_t count = read(STDIN_FILENO, bytes, sizeof(bytes));
    if (count == 0) {
        stopping = 1;
        return;
    }
    if (count < 0) {
        if (errno != EINTR && errno != EAGAIN && errno != EWOULDBLOCK)
            stopping = 1;
        return;
    }
    for (ssize_t i = 0; i < count; i++) {
        if (bytes[i] == '\n') {
            if (command_length && command_buffer[command_length - 1] == '\r')
                command_length--;
            command_buffer[command_length] = 0;
            if (strcmp(command_buffer, "cancel") == 0)
                stopping = 1;
            else if (accepting_pdf && !pdf_command && command_length)
                pdf_command = g_strdup(command_buffer);
            else
                invalid_command = true;
            command_length = 0;
        } else if (command_length < sizeof(command_buffer) - 1) {
            command_buffer[command_length++] = bytes[i];
        } else {
            invalid_command = true;
            stopping = 1;
        }
    }
}

static void pump(void)
{
    while (g_main_context_iteration(NULL, FALSE)) {
    }
    read_commands();
    if (parent_pid > 1 && (getppid() != parent_pid || (kill(parent_pid, 0) < 0 && errno == ESRCH)))
        stopping = 1;
    if (!stopping)
        g_usleep(10000);
}

static void response_received(GDBusConnection *connection, const gchar *sender, const gchar *path,
                              const gchar *interface, const gchar *name, GVariant *parameters, gpointer data)
{
    (void)connection;
    (void)sender;
    (void)interface;
    (void)name;
    PrintRequest *request = data;
    if (request->received || !request->path || strcmp(request->path, path) != 0 ||
        !g_variant_is_of_type(parameters, G_VARIANT_TYPE("(ua{sv})")))
        return;
    g_variant_get(parameters, "(u@a{sv})", &request->status, &request->result);
    request->received = TRUE;
}

static void close_request(GDBusConnection *bus, const char *path)
{
    if (!path || g_dbus_connection_is_closed(bus))
        return;
    GVariant *reply = g_dbus_connection_call_sync(bus, PORTAL, path, REQUEST_INTERFACE, "Close", NULL, NULL,
                                                 G_DBUS_CALL_FLAGS_NONE, 1000, NULL, NULL);
    if (reply)
        g_variant_unref(reply);
}

static GVariant *request(GDBusConnection *bus, const char *method, GVariant *parameters,
                         GUnixFDList *descriptors, bool *canceled)
{
    PrintRequest state = {0};
    guint subscription = g_dbus_connection_signal_subscribe(bus, PORTAL, REQUEST_INTERFACE, "Response", NULL,
                                                            NULL, G_DBUS_SIGNAL_FLAGS_NONE, response_received,
                                                            &state, NULL);
    GError *error = NULL;
    GVariant *reply = g_dbus_connection_call_with_unix_fd_list_sync(
        bus, PORTAL, PORTAL_PATH, PRINT_INTERFACE, method, parameters, G_VARIANT_TYPE("(o)"),
        G_DBUS_CALL_FLAGS_NONE, 10000, descriptors, NULL, NULL, &error);
    if (!reply) {
        if (error)
            fprintf(stderr, "Print portal method failed: %s\n", error->message);
        g_clear_error(&error);
        g_dbus_connection_signal_unsubscribe(bus, subscription);
        return NULL;
    }
    g_variant_get(reply, "(&o)", &state.path);
    while (!state.received && !stopping && !invalid_command && !g_dbus_connection_is_closed(bus))
        pump();
    if (!state.received)
        close_request(bus, state.path);
    g_dbus_connection_signal_unsubscribe(bus, subscription);
    g_variant_unref(reply);
    *canceled = stopping || (state.received && state.status == 1);
    if (!state.received || state.status != 0) {
        if (state.result)
            g_variant_unref(state.result);
        return NULL;
    }
    return state.result;
}

static guint portal_version(GDBusConnection *bus)
{
    GVariant *reply = g_dbus_connection_call_sync(
        bus, PORTAL, PORTAL_PATH, "org.freedesktop.DBus.Properties", "Get",
        g_variant_new("(ss)", PRINT_INTERFACE, "version"), G_VARIANT_TYPE("(v)"),
        G_DBUS_CALL_FLAGS_NONE, 3000, NULL, NULL);
    if (!reply)
        return 0;
    GVariant *boxed = NULL;
    g_variant_get(reply, "(@v)", &boxed);
    GVariant *value = g_variant_get_variant(boxed);
    guint version = g_variant_is_of_type(value, G_VARIANT_TYPE_UINT32) ? g_variant_get_uint32(value) : 0;
    g_variant_unref(value);
    g_variant_unref(boxed);
    g_variant_unref(reply);
    return version;
}

static void init_options(GVariantBuilder *options, guint version)
{
    static guint sequence;
    char handle[80];
    g_snprintf(handle, sizeof(handle), "ritsukage_print_%u_%u", (guint)getpid(), ++sequence);
    g_variant_builder_init(options, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(options, "{sv}", "handle_token", g_variant_new_string(handle));
    g_variant_builder_add(options, "{sv}", "modal", g_variant_new_boolean(TRUE));
    if (version >= 3) {
        const gchar *formats[] = {"pdf", NULL};
        g_variant_builder_add(options, "{sv}", "supported_output_file_formats", g_variant_new_strv(formats, -1));
    }
}

static bool read_geometry(GVariant *result, PageGeometry *geometry)
{
    GVariant *setup = g_variant_lookup_value(result, "page-setup", G_VARIANT_TYPE_VARDICT);
    if (!setup)
        return false;
    double width = 0, height = 0, top = 0, bottom = 0, left = 0, right = 0;
    const gchar *orientation = NULL;
    bool valid = g_variant_lookup(setup, "Width", "d", &width) &&
                 g_variant_lookup(setup, "Height", "d", &height) &&
                 g_variant_lookup(setup, "MarginTop", "d", &top) &&
                 g_variant_lookup(setup, "MarginBottom", "d", &bottom) &&
                 g_variant_lookup(setup, "MarginLeft", "d", &left) &&
                 g_variant_lookup(setup, "MarginRight", "d", &right) &&
                 g_variant_lookup(setup, "Orientation", "&s", &orientation);
    if (valid && (strcmp(orientation, "landscape") == 0 || strcmp(orientation, "reverse-landscape") == 0 ||
                  strcmp(orientation, "reverse_landscape") == 0)) {
        double temporary = width;
        width = height;
        height = temporary;
    } else if (valid && strcmp(orientation, "portrait") != 0 && strcmp(orientation, "reverse-portrait") != 0 &&
               strcmp(orientation, "reverse_portrait") != 0) {
        valid = false;
    }
    valid = valid && isfinite(width) && isfinite(height) && isfinite(top) && isfinite(bottom) &&
            isfinite(left) && isfinite(right) && width > 0 && height > 0 && top >= 0 && bottom >= 0 &&
            left >= 0 && right >= 0 && left + right < width && top + bottom < height &&
            width * POINTS_PER_MM <= 14400 && height * POINTS_PER_MM <= 14400;
    if (valid) {
        *geometry = (PageGeometry){width * POINTS_PER_MM, height * POINTS_PER_MM, left * POINTS_PER_MM,
                                  top * POINTS_PER_MM, (width - left - right) * POINTS_PER_MM,
                                  (height - top - bottom) * POINTS_PER_MM};
    }
    g_variant_unref(setup);
    return valid;
}

static void write_geometry(PageGeometry geometry)
{
    char values[6][G_ASCII_DTOSTR_BUF_SIZE];
    double numbers[] = {geometry.width, geometry.height, geometry.x, geometry.y,
                        geometry.printable_width, geometry.printable_height};
    for (size_t i = 0; i < 6; i++)
        g_ascii_dtostr(values[i], sizeof(values[i]), numbers[i]);
    printf("{\"PaperWidthPoints\":%s,\"PaperHeightPoints\":%s,\"PrintableXPoints\":%s,"
           "\"PrintableYPoints\":%s,\"PrintableWidthPoints\":%s,\"PrintableHeightPoints\":%s}\n",
           values[0], values[1], values[2], values[3], values[4], values[5]);
    fflush(stdout);
}

static int print_document(GDBusConnection *bus, guint version, const char *directory,
                          const char *parent_window, const char *title)
{
    GVariantBuilder settings, setup, options;
    g_variant_builder_init(&settings, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_init(&setup, G_VARIANT_TYPE_VARDICT);
    init_options(&options, version);
    bool canceled = false;
    GVariant *prepared = request(bus, "PreparePrint", g_variant_new("(ssa{sv}a{sv}a{sv})", parent_window, title,
                                                                  &settings, &setup, &options), NULL, &canceled);
    if (!prepared) {
        if (canceled) {
            puts("canceled");
            return 0;
        }
        fputs("The native print dialog did not return print settings.\n", stderr);
        return 1;
    }
    guint token = 0;
    PageGeometry geometry;
    bool valid = g_variant_lookup(prepared, "token", "u", &token) && read_geometry(prepared, &geometry);
    g_variant_unref(prepared);
    if (!valid) {
        fputs("The native print dialog returned invalid paper geometry.\n", stderr);
        return 1;
    }
    accepting_pdf = true;
    write_geometry(geometry);
    while (!pdf_command && !stopping && !invalid_command)
        pump();
    if (stopping) {
        puts("canceled");
        return 0;
    }
    char *expected = g_build_filename(directory, "document.pdf", NULL);
    if (invalid_command || !pdf_command || strcmp(pdf_command, expected) != 0) {
        g_free(expected);
        fputs("The print session received an unexpected document path.\n", stderr);
        return 1;
    }
    int descriptor = open(expected, O_RDONLY | O_CLOEXEC | O_NOFOLLOW);
    g_free(expected);
    if (descriptor < 0) {
        fputs("The prepared print document could not be opened.\n", stderr);
        return 1;
    }
    struct stat info;
    char header[5];
    valid = fstat(descriptor, &info) == 0 && S_ISREG(info.st_mode) && info.st_size > 5 &&
            info.st_size <= MAXIMUM_PDF_BYTES && pread(descriptor, header, sizeof(header), 0) == (ssize_t)sizeof(header) &&
            memcmp(header, "%PDF-", sizeof(header)) == 0;
    if (!valid) {
        close(descriptor);
        fputs("The prepared print document is not a supported PDF file.\n", stderr);
        return 1;
    }
    GUnixFDList *descriptors = g_unix_fd_list_new();
    GError *error = NULL;
    gint fd_index = g_unix_fd_list_append(descriptors, descriptor, &error);
    close(descriptor);
    if (fd_index < 0) {
        g_clear_error(&error);
        g_object_unref(descriptors);
        fputs("The print document descriptor could not be transferred.\n", stderr);
        return 1;
    }
    init_options(&options, version);
    g_variant_builder_add(&options, "{sv}", "token", g_variant_new_uint32(token));
    GVariant *printed = request(bus, "Print", g_variant_new("(ssha{sv})", parent_window, title, fd_index, &options),
                                descriptors, &canceled);
    g_object_unref(descriptors);
    if (printed) {
        g_variant_unref(printed);
        puts("printed");
        return 0;
    }
    if (canceled) {
        puts("canceled");
        return 0;
    }
    fputs("The desktop print service rejected the document.\n", stderr);
    return 1;
}

int main(int argc, char **argv)
{
    signal(SIGPIPE, SIG_IGN);
    signal(SIGTERM, stop_signal);
    signal(SIGINT, stop_signal);
    bool capabilities = argc == 2 && strcmp(argv[1], "capabilities") == 0;
    if (!capabilities && (argc != 6 || strcmp(argv[1], "--native-print-worker") != 0)) {
        fputs("Invalid print helper arguments.\n", stderr);
        return 2;
    }
    if (!capabilities) {
        char *end = NULL;
        long parsed = strtol(argv[4], &end, 10);
        if (!end || *end || parsed <= 1 || parsed > INT32_MAX || !g_path_is_absolute(argv[2]) ||
            !g_utf8_validate(argv[3], -1, NULL) || !g_utf8_validate(argv[5], -1, NULL)) {
            fputs("Invalid print session arguments.\n", stderr);
            return 2;
        }
        parent_pid = (pid_t)parsed;
        if (getppid() != parent_pid)
            return 0;
        (void)prctl(PR_SET_PDEATHSIG, SIGTERM);
        int flags = fcntl(STDIN_FILENO, F_GETFL, 0);
        if (flags < 0 || fcntl(STDIN_FILENO, F_SETFL, flags | O_NONBLOCK) < 0)
            return 1;
    }
    GError *error = NULL;
    GDBusConnection *bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, &error);
    guint version = bus ? portal_version(bus) : 0;
    if (capabilities) {
        puts(version ? "{\"Available\":true,\"FailureReason\":null}"
                     : "{\"Available\":false,\"FailureReason\":\"No desktop print portal is available.\"}");
        if (bus)
            g_object_unref(bus);
        g_clear_error(&error);
        return 0;
    }
    if (!version) {
        fputs("No desktop print portal is available.\n", stderr);
        if (bus)
            g_object_unref(bus);
        g_clear_error(&error);
        return 1;
    }
    int result = print_document(bus, version, argv[2], argv[3], argv[5]);
    g_object_unref(bus);
    g_free(pdf_command);
    return result;
}
