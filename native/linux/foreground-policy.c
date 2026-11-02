#include <X11/Xlib.h>
#include <glib.h>
#include <errno.h>
#include <poll.h>
#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

static int protocol_error;

static int on_protocol_error(Display *display, XErrorEvent *event) {
    (void)display;
    (void)event;
    protocol_error = 1;
    return 0;
}

static int on_io_error(Display *display) {
    (void)display;
    fputs("Foreground policy X11 connection was lost.\n", stderr);
    _Exit(74);
}

static unsigned long *property(Display *display, Window window, Atom name, unsigned long *count) {
    Atom actual_type;
    int format;
    unsigned long remaining;
    unsigned char *data = NULL;
    *count = 0;
    if (XGetWindowProperty(display, window, name, 0, 128, False, AnyPropertyType,
                           &actual_type, &format, count, &remaining, &data) != Success || format != 32) {
        if (data) XFree(data);
        *count = 0;
        return NULL;
    }
    return (unsigned long *)data;
}

static bool contains(const unsigned long *items, unsigned long count, Atom value) {
    for (unsigned long i = 0; i < count; ++i)
        if (items[i] == value) return true;
    return false;
}

static void append_json_string(GString *json, const char *text) {
    if (!text) { g_string_append(json, "null"); return; }
    g_string_append_c(json, '"');
    for (const unsigned char *p = (const unsigned char *)text; *p; ++p) {
        if (*p == '"' || *p == '\\') { g_string_append_c(json, '\\'); g_string_append_c(json, (char)*p); }
        else if (*p < 0x20) g_string_append_printf(json, "\\u%04x", *p);
        else g_string_append_c(json, (char)*p);
    }
    g_string_append_c(json, '"');
}

static void publish(const char *program, bool fullscreen, bool program_available, bool fullscreen_available,
                    GString *previous) {
    GString *json = g_string_new("{\"program\":");
    append_json_string(json, program);
    g_string_append_printf(json, ",\"fullscreen\":%s,\"programAvailable\":%s,\"fullscreenAvailable\":%s}",
                           fullscreen ? "true" : "false", program_available ? "true" : "false",
                           fullscreen_available ? "true" : "false");
    if (strcmp(json->str, previous->str) != 0) {
        if (printf("%s\n", json->str) < 0 || fflush(stdout) != 0) _Exit(74);
        g_string_assign(previous, json->str);
    }
    g_string_free(json, TRUE);
}

int main(void) {
    signal(SIGPIPE, SIG_IGN);
    XSetErrorHandler(on_protocol_error);
    XSetIOErrorHandler(on_io_error);
    Display *display = XOpenDisplay(NULL);
    if (!display) { fputs("Foreground policy X11 display unavailable.\n", stderr); return 69; }
    Window root = DefaultRootWindow(display);
    Window selected = None;
    Atom active_atom = XInternAtom(display, "_NET_ACTIVE_WINDOW", False);
    Atom state_atom = XInternAtom(display, "_NET_WM_STATE", False);
    Atom fullscreen_atom = XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", False);
    Atom pid_atom = XInternAtom(display, "_NET_WM_PID", False);
    Atom supported_atom = XInternAtom(display, "_NET_SUPPORTED", False);
    XSelectInput(display, root, PropertyChangeMask);
    GString *previous = g_string_new(NULL);
    int exit_code = 0;
    for (;;) {
        protocol_error = 0;
        unsigned long count;
        unsigned long *values = property(display, root, active_atom, &count);
        Window active = count ? values[0] : None;
        if (values) XFree(values);
        if (selected != active) {
            if (selected != None) {
                XSelectInput(display, selected, NoEventMask);
                XSync(display, False);
                protocol_error = 0;
            }
            selected = active;
            if (selected != None) XSelectInput(display, selected, PropertyChangeMask | StructureNotifyMask);
        }
        values = property(display, root, supported_atom, &count);
        bool program_available = contains(values, count, active_atom);
        bool fullscreen_available = program_available && contains(values, count, state_atom) &&
                                    contains(values, count, fullscreen_atom);
        if (values) XFree(values);
        char *program = NULL;
        bool fullscreen = false;
        if (active != None) {
            values = property(display, active, pid_atom, &count);
            unsigned long pid = count ? values[0] : 0;
            if (values) XFree(values);
            if (program_available && pid > 0) {
                char link[64];
                snprintf(link, sizeof(link), "/proc/%lu/exe", pid);
                GError *error = NULL;
                program = g_file_read_link(link, &error);
                if (error) g_error_free(error);
                if (!program || !g_utf8_validate(program, -1, NULL)) program_available = false;
            } else program_available = false;
            values = property(display, active, state_atom, &count);
            fullscreen = contains(values, count, fullscreen_atom);
            if (values) XFree(values);
        }
        XSync(display, False);
        if (protocol_error) publish(NULL, false, false, false, previous);
        else publish(program_available ? program : NULL, fullscreen, program_available, fullscreen_available, previous);
        g_free(program);
        bool refresh = false;
        while (!refresh) {
            while (XPending(display)) {
                XEvent event;
                XNextEvent(display, &event);
                if (event.type == PropertyNotify || event.type == DestroyNotify ||
                    event.type == UnmapNotify || event.type == ConfigureNotify) refresh = true;
            }
            if (refresh) break;
            struct pollfd descriptors[2] = {{ConnectionNumber(display), POLLIN, 0}, {STDIN_FILENO, POLLIN, 0}};
            int result;
            do { result = poll(descriptors, 2, -1); } while (result < 0 && errno == EINTR);
            if (result < 0) { exit_code = 74; goto finished; }
            if (descriptors[1].revents & (POLLIN | POLLHUP | POLLERR | POLLNVAL)) goto finished;
            if (descriptors[0].revents & (POLLHUP | POLLERR | POLLNVAL)) { exit_code = 74; goto finished; }
        }
    }
finished:
    g_string_free(previous, TRUE);
    XCloseDisplay(display);
    return exit_code;
}
