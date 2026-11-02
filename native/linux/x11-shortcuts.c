#define _POSIX_C_SOURCE 200809L
#include <X11/Xlib.h>
#include <X11/XKBlib.h>
#include <X11/extensions/Xfixes.h>
#include <glib.h>
#include <errno.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <unistd.h>

static volatile sig_atomic_t stopping;
static int registration_error;

static void stop_signal(int number)
{
    (void)number;
    stopping = 1;
}

static int protocol_error(Display *display, XErrorEvent *event)
{
    (void)display;
    registration_error = event->error_code;
    return 0;
}

static int connection_lost(Display *display)
{
    (void)display;
    fputs("The X11 shortcut connection was lost.\n", stderr);
    _Exit(74);
}

typedef struct {
    const char *id;
    int code;
    unsigned int mask;
    gboolean registered;
    gboolean held;
} Binding;

static int input_capability(void)
{
    XSetErrorHandler(protocol_error);
    XSetIOErrorHandler(connection_lost);
    Display *display = XOpenDisplay(NULL);
    if (display == NULL) return 2;
    int event = 0, error = 0, major = 2, minor = 0;
    int available = XFixesQueryExtension(display, &event, &error) &&
        XFixesQueryVersion(display, &major, &minor) && major >= 2;
    XSync(display, False);
    available = available && !registration_error;
    XCloseDisplay(display);
    if (!available) return 2;
    puts("OK");
    return 0;
}
int main(int argc, char **argv)
{
    if (argc == 2 && strcmp(argv[1], "--xfixes-input-capability") == 0) return input_capability();
    if (argc < 4 || (argc - 1) % 3 != 0) return 2;
    signal(SIGTERM, stop_signal);
    signal(SIGINT, stop_signal);
    signal(SIGPIPE, stop_signal);
    prctl(PR_SET_PDEATHSIG, SIGTERM);
    if (getppid() == 1) return 1;
    setvbuf(stdout, NULL, _IOLBF, 0);
    XSetErrorHandler(protocol_error);
    XSetIOErrorHandler(connection_lost);
    Display *display = XOpenDisplay(NULL);
    if (!display) { fputs("The X11 display is unavailable.\n", stderr); return 74; }
    Window root = DefaultRootWindow(display);
    Bool repeat_supported = False;
    XkbSetDetectableAutoRepeat(display, True, &repeat_supported);
    int count = (argc - 1) / 3;
    Binding *bindings = g_new0(Binding, count);
    const unsigned int locks[] = {0, LockMask, Mod2Mask, LockMask | Mod2Mask};
    for (int index = 0; index < count; index++) {
        Binding *binding = &bindings[index];
        binding->id = argv[1 + index * 3];
        const char *trigger = argv[2 + index * 3];
        const char *name = strrchr(trigger, '+');
        name = name ? name + 1 : trigger;
        unsigned int modifiers, key;
        if (sscanf(binding->id, "key_%x_%x", &modifiers, &key) != 2) { stopping = 1; break; }
        (void)key;
        binding->mask = ((modifiers & 1) ? Mod1Mask : 0) | ((modifiers & 2) ? ControlMask : 0) |
                        ((modifiers & 4) ? ShiftMask : 0) | ((modifiers & 8) ? Mod4Mask : 0);
        binding->code = XKeysymToKeycode(display, XStringToKeysym(name));
        if (binding->code != 0) {
            registration_error = 0;
            for (unsigned int variant = 0; variant < G_N_ELEMENTS(locks); variant++)
                XGrabKey(display, binding->code, binding->mask | locks[variant], root, False,
                         GrabModeAsync, GrabModeAsync);
            XSync(display, False);
            binding->registered = registration_error == 0;
            if (!binding->registered)
                for (unsigned int variant = 0; variant < G_N_ELEMENTS(locks); variant++)
                    XUngrabKey(display, binding->code, binding->mask | locks[variant], root);
        }
        if (binding->registered) {
            gchar *encoded = g_base64_encode((const guchar *)trigger, strlen(trigger));
            printf("B\t%s\t%s\n", binding->id, encoded);
            g_free(encoded);
        } else {
            const char *message = binding->code == 0
                ? "The key is not available in the current X11 keyboard layout."
                : registration_error == BadAccess
                    ? "The shortcut is already registered by another application."
                    : "X11 shortcut registration failed.";
            gchar *encoded = g_base64_encode((const guchar *)message, strlen(message));
            printf("F\t%s\t%s\n", binding->id, encoded);
            g_free(encoded);
        }
    }
    XSync(display, False);
    fputs("R\n", stdout);
    int exit_code = 0;
    while (!stopping) {
        while (!stopping && XPending(display)) {
            XEvent event;
            XNextEvent(display, &event);
            if (event.type != KeyPress && event.type != KeyRelease) continue;
            if (event.type == KeyRelease && !repeat_supported && XPending(display)) {
                XEvent next;
                XPeekEvent(display, &next);
                if (next.type == KeyPress && next.xkey.keycode == event.xkey.keycode &&
                    next.xkey.time == event.xkey.time) continue;
            }
            for (int index = 0; index < count; index++) {
                Binding *binding = &bindings[index];
                if (!binding->registered) continue;
                if (event.type == KeyRelease) {
                    if (binding->held) printf("D\t%s\n", binding->id);
                    binding->held = FALSE;
                } else if (binding->code == (int)event.xkey.keycode &&
                           binding->mask == (event.xkey.state & (ShiftMask | ControlMask | Mod1Mask | Mod3Mask | Mod4Mask | Mod5Mask)) && !binding->held) {
                    binding->held = TRUE;
                    printf("A\t%s\n", binding->id);
                }
            }
        }
        if (stopping) break;
        struct pollfd inputs[2] = {{ConnectionNumber(display), POLLIN, 0}, {STDIN_FILENO, POLLIN, 0}};
        int result = poll(inputs, 2, -1);
        if (result < 0) {
            if (errno == EINTR) continue;
            exit_code = 74; break;
        }
        if (inputs[1].revents & (POLLIN | POLLHUP | POLLERR | POLLNVAL)) break;
        if (inputs[0].revents & (POLLHUP | POLLERR | POLLNVAL)) { exit_code = 74; break; }
    }
    for (int index = 0; index < count; index++)
        if (bindings[index].registered)
            for (unsigned int variant = 0; variant < G_N_ELEMENTS(locks); variant++)
                XUngrabKey(display, bindings[index].code, bindings[index].mask | locks[variant], root);
    XSync(display, False);
    XCloseDisplay(display);
    g_free(bindings);
    return exit_code;
}
