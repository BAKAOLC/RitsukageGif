#define _POSIX_C_SOURCE 200809L
#include <X11/Xlib.h>
#include <X11/Xatom.h>
#include <X11/XKBlib.h>
#include <X11/keysym.h>
#include <X11/extensions/record.h>
#include <errno.h>
#include <poll.h>
#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>
#include <time.h>

typedef struct {
    Display *control;
    XRecordContext context;
    Window root;
    pthread_mutex_t gate;
    int stop_requested;
    int started;
    int wake[2];
    int xkb_event;
    int initialized;
    int failed;
    int header_sent;
    int pointer_known;
    int x, y;
    unsigned int buttons;
    unsigned int modifiers;
    unsigned int shift_mask, control_mask, alt_mask, meta_mask;
    uint16_t barrier_sequence;
    int recording_input;
    int record_keyboard;
    int record_mouse;
    uint32_t key_names[256];
    uint32_t clock_server;
    uint64_t clock_monotonic;
} Observer;

static atomic_int x_error;

static int write_bytes(const void *value, size_t count) {
    const unsigned char *bytes = value;
    while (count != 0) {
        ssize_t written = write(STDOUT_FILENO, bytes, count);
        if (written < 0 && errno == EINTR) continue;
        if (written <= 0) return 0;
        bytes += written;
        count -= (size_t)written;
    }
    return 1;
}

static void put32(unsigned char *bytes, uint32_t value) {
    bytes[0] = (unsigned char)value;
    bytes[1] = (unsigned char)(value >> 8);
    bytes[2] = (unsigned char)(value >> 16);
    bytes[3] = (unsigned char)(value >> 24);
}

static void put64(unsigned char *bytes, uint64_t value) {
    put32(bytes, (uint32_t)value); put32(bytes + 4, (uint32_t)(value >> 32));
}

static uint64_t monotonic_time(void) {
    struct timespec value;
    if (clock_gettime(CLOCK_MONOTONIC, &value) != 0) return 0;
    return (uint64_t)value.tv_sec * 1000000000u + (uint64_t)value.tv_nsec;
}

static int recording_emit(Observer *observer, uint32_t kind, uint32_t code, uint32_t physical,
                          int x, int y, int value, uint32_t modifiers, uint32_t time) {
    unsigned char bytes[40];
    put32(bytes, kind); put32(bytes + 4, code); put32(bytes + 8, physical);
    put32(bytes + 12, modifiers); put32(bytes + 16, (uint32_t)x); put32(bytes + 20, (uint32_t)y);
    put32(bytes + 24, (uint32_t)value); put32(bytes + 28, 0);
    int64_t delta = (int64_t)(int32_t)(time - observer->clock_server) * 1000000;
    put64(bytes + 32, (uint64_t)((int64_t)observer->clock_monotonic + delta));
    return write_bytes(bytes, sizeof(bytes));
}

static uint16_t get16(const unsigned char *bytes, int swapped) {
    uint16_t value;
    memcpy(&value, bytes, sizeof(value));
    return swapped ? (uint16_t)((value << 8) | (value >> 8)) : value;
}

static uint32_t get32(const unsigned char *bytes, int swapped) {
    uint32_t value;
    memcpy(&value, bytes, sizeof(value));
    return swapped ? ((value >> 24) | ((value >> 8) & 0x0000ff00u) |
                      ((value << 8) & 0x00ff0000u) | (value << 24)) : value;
}

static int emit(uint32_t message, int x, int y, uint32_t data, uint32_t state, uint32_t time) {
    unsigned char bytes[24];
    put32(bytes, message); put32(bytes + 4, (uint32_t)x); put32(bytes + 8, (uint32_t)y);
    put32(bytes + 12, data); put32(bytes + 16, state); put32(bytes + 20, time);
    return write_bytes(bytes, sizeof(bytes));
}

static void request_stop(Observer *observer) {
    pthread_mutex_lock(&observer->gate);
    observer->stop_requested = 1;
    int started = observer->started;
    pthread_mutex_unlock(&observer->gate);
    if (started) {
        XRecordDisableContext(observer->control, observer->context);
        XFlush(observer->control);
    }
}

static int stopping(Observer *observer) {
    pthread_mutex_lock(&observer->gate);
    int stop = observer->stop_requested;
    pthread_mutex_unlock(&observer->gate);
    return stop;
}

static void *watch_parent(void *value) {
    Observer *observer = value;
    struct pollfd inputs[2] = {{STDIN_FILENO, POLLIN, 0}, {observer->wake[0], POLLIN, 0}};
    for (;;) {
        int result = poll(inputs, 2, -1);
        if (result < 0 && errno == EINTR) continue;
        if (result < 0) { request_stop(observer); return NULL; }
        if (inputs[1].revents != 0) return NULL;
        if (inputs[0].revents != 0) { request_stop(observer); return NULL; }
    }
}

static int read_modifier_roles(Observer *observer) {
    if (observer->recording_input) {
        XkbDescPtr keyboard = XkbGetMap(observer->control, 0, XkbUseCoreKbd);
        if (!keyboard) return 0;
        int valid = XkbGetNames(observer->control, XkbKeyNamesMask, keyboard) == Success &&
                    keyboard->names && keyboard->names->keys;
        if (valid) {
            memset(observer->key_names, 0, sizeof(observer->key_names));
            for (unsigned int code = keyboard->min_key_code; code <= keyboard->max_key_code && code < 256; ++code) {
                const unsigned char *name = (const unsigned char *)keyboard->names->keys[code].name;
                observer->key_names[code] = (uint32_t)name[0] | ((uint32_t)name[1] << 8) |
                                           ((uint32_t)name[2] << 16) | ((uint32_t)name[3] << 24);
            }
        }
        XkbFreeKeyboard(keyboard, XkbAllComponentsMask, True);
        if (!valid) return 0;
    }
    int first = 0, last = 0, symbols_per_key = 0;
    XDisplayKeycodes(observer->control, &first, &last);
    KeySym *symbols = XGetKeyboardMapping(observer->control, (KeyCode)first, last - first + 1, &symbols_per_key);
    XModifierKeymap *map = XGetModifierMapping(observer->control);
    observer->shift_mask = ShiftMask; observer->control_mask = ControlMask;
    observer->alt_mask = observer->meta_mask = 0;
    unsigned int fallback_meta = 0;
    int success = symbols && map && symbols_per_key > 0;
    if (success) {
        for (int modifier = 0; modifier < 8; modifier++) {
            for (int slot = 0; slot < map->max_keypermod; slot++) {
                int code = map->modifiermap[modifier * map->max_keypermod + slot];
                if (code < first || code > last) continue;
                KeySym key = symbols[(code - first) * symbols_per_key];
                unsigned int mask = 1u << modifier;
                if (key == XK_Alt_L || key == XK_Alt_R) observer->alt_mask |= mask;
                if (key == XK_Super_L || key == XK_Super_R) observer->meta_mask |= mask;
                if (key == XK_Meta_L || key == XK_Meta_R) fallback_meta |= mask;
            }
        }
    }
    if (!observer->meta_mask) observer->meta_mask = fallback_meta & ~observer->alt_mask;
    if (symbols) XFree(symbols);
    if (map) XFreeModifiermap(map);
    return success;
}

static uint32_t canonical_modifiers(Observer *observer) {
    uint32_t value = 0;
    if (observer->modifiers & observer->shift_mask) value |= 1;
    if (observer->modifiers & observer->control_mask) value |= 4;
    if (observer->modifiers & observer->alt_mask) value |= 8;
    if (observer->modifiers & observer->meta_mask) value |= 64;
    return value;
}

static void synchronize_pointer(Observer *observer) {
    if (stopping(observer)) return;
    if (observer->initialized && !observer->recording_input && !emit(0x001f, 0, 0, 0, 0, 0)) { request_stop(observer); return; }
    observer->initialized = 0;
    if (!read_modifier_roles(observer)) {
        observer->failed = 1;
        fprintf(stderr, "X11 global mouse observation could not read keyboard modifier mappings.\n");
        request_stop(observer);
        return;
    }
    Window root, child;
    int root_x, root_y, window_x, window_y;
    unsigned int state;
    observer->barrier_sequence = (uint16_t)NextRequest(observer->control);
    /* The synchronous return is deliberately ignored. Only this reply's position in
       the RECORD stream establishes the event-state baseline. */
    XQueryPointer(observer->control, observer->root, &root, &child,
        &root_x, &root_y, &window_x, &window_y, &state);
    if (atomic_load(&x_error)) {
        observer->failed = 1;
        request_stop(observer);
    }
}

static void pointer_reply(Observer *observer, const unsigned char *event, int swapped) {
    if (get16(event + 2, swapped) != observer->barrier_sequence) return;
    unsigned int state = get16(event + 24, swapped);
    observer->modifiers = state & 255;
    observer->buttons = 0;
    if (state & Button1Mask) observer->buttons |= 1u << 1;
    if (state & Button3Mask) observer->buttons |= 1u << 2;
    if (state & Button2Mask) observer->buttons |= 1u << 3;
    observer->pointer_known = event[1] && get32(event + 8, swapped) == (uint32_t)observer->root;
    observer->x = get16(event + 16, swapped);
    observer->y = get16(event + 18, swapped);
    observer->initialized = 1;
    if (!observer->header_sent) {
        observer->header_sent = 1;
        if (observer->recording_input) {
            unsigned char header[24];
            memcpy(header, "RGINPUT1", 8); put64(header + 8, monotonic_time());
            put32(header + 16, (observer->record_keyboard ? 1u : 0u) | (observer->record_mouse ? 2u : 0u));
            put32(header + 20, 40);
            if (!write_bytes(header, sizeof(header))) request_stop(observer);
        } else if (!write_bytes("RGMOUSE1", 8)) request_stop(observer);
    }
}

static void device_event(Observer *observer, const unsigned char *event) {
    if (!observer->initialized) return;
    unsigned int type = event[0];
    if (type & 0x80) return;
    if (observer->recording_input) {
        uint32_t modifiers_before = observer->modifiers;
        observer->modifiers = get16(event + 28, 0) & 255;
        uint32_t state = canonical_modifiers(observer);
        observer->modifiers = modifiers_before;
        uint32_t time = get32(event + 4, 0);
        if (type == KeyPress || type == KeyRelease) {
            if (observer->record_keyboard && !recording_emit(observer, type == KeyPress ? 1 : 2,
                event[1], observer->key_names[event[1]], 0, 0, 0, state, time)) request_stop(observer);
            return;
        }
        if (!observer->record_mouse || (type != ButtonPress && type != ButtonRelease)) return;
        int x = (int16_t)get16(event + 20, 0), y = (int16_t)get16(event + 22, 0);
        int button = event[1], pressed = type == ButtonPress;
        if (button >= 4 && button <= 7) {
            if (pressed && !recording_emit(observer, 5, button <= 5 ? 0x020a : 0x020e, 0,
                x, y, button == 4 || button == 7 ? 120 : -120, state, time)) request_stop(observer);
        } else {
            int mask = button == 1 ? 1 : button == 3 ? 2 : button == 2 ? 4 :
                       button == 8 ? 8 : button == 9 ? 16 : 0;
            if (mask && !recording_emit(observer, pressed ? 3 : 4, 0, 0,
                x, y, mask, state, time)) request_stop(observer);
        }
        return;
    }
    uint32_t message = 0, data = 0;
    if (type == MotionNotify) {
        observer->pointer_known = get32(event + 8, 0) == (uint32_t)observer->root;
        observer->x = get16(event + 20, 0);
        observer->y = get16(event + 22, 0);
        if (!observer->pointer_known) message = 0x001f;
        else if (observer->buttons != 0) message = 0x0200;
    } else if (type == ButtonPress || type == ButtonRelease) {
        int pressed = type == ButtonPress;
        int button = 0;
        switch (event[1]) {
            case 1: button = 1; message = pressed ? 0x0201 : 0x0202; break;
            case 3: button = 2; message = pressed ? 0x0204 : 0x0205; break;
            case 2: button = 3; message = pressed ? 0x0207 : 0x0208; break;
            case 8: button = 4; message = pressed ? 0x020b : 0x020c; data = 1u << 16; break;
            case 9: button = 5; message = pressed ? 0x020b : 0x020c; data = 2u << 16; break;
            default: break;
        }
        if (button != 0) {
            unsigned int prior = observer->buttons;
            if (pressed) observer->buttons |= 1u << button;
            else observer->buttons &= ~(1u << button);
            if (!observer->pointer_known || (pressed && prior != 0)) message = 0x001f;
        }
    }
    if (message && !emit(message, observer->x, observer->y, data,
        canonical_modifiers(observer), get32(event + 4, 0))) request_stop(observer);
}

static void record_events(XPointer closure, XRecordInterceptData *record) {
    Observer *observer = (Observer *)closure;
    if (record->category == XRecordStartOfData) {
        pthread_mutex_lock(&observer->gate);
        observer->started = 1;
        int stop = observer->stop_requested;
        pthread_mutex_unlock(&observer->gate);
        if (stop) request_stop(observer);
        else synchronize_pointer(observer);
    } else if (record->category == XRecordFromServer) {
        unsigned long length = record->data_len * 4;
        for (unsigned long offset = 0; offset + 32 <= length;) {
            const unsigned char *event = record->data + offset;
            unsigned long size = 32;
            if (record->id_base == 0) device_event(observer, event);
            else if (event[0] == 1) {
                size += (unsigned long)get32(event + 4, record->client_swapped) * 4;
                pointer_reply(observer, event, record->client_swapped);
            } else if (event[0] == observer->xkb_event) {
                if (event[1] == XkbStateNotify && observer->initialized) {
                    observer->modifiers = event[9];
                    if (observer->recording_input && !recording_emit(observer, 6, 0, 0, 0, 0, 0,
                        canonical_modifiers(observer), get32(event + 4, record->client_swapped))) request_stop(observer);
                }
                else if (event[1] == XkbNewKeyboardNotify || event[1] == XkbMapNotify)
                    synchronize_pointer(observer);
            }
            if (size > length - offset) {
                observer->failed = 1;
                fprintf(stderr, "X11 global mouse observation received an incomplete RECORD protocol element.\n");
                request_stop(observer);
                break;
            }
            offset += size;
        }
    }
    XRecordFreeData(record);
}

static int record_error(Display *display, XErrorEvent *error) {
    atomic_store(&x_error, 1);
    char message[160];
    XGetErrorText(display, error->error_code, message, sizeof(message));
    fprintf(stderr, "X11 global mouse observation failed: %s (request %u/%u).\n",
        message, error->request_code, error->minor_code);
    return 0;
}

int main(int argc, char **argv) {
    int recording = 0, record_keyboard = 0, record_mouse = 0;
    for (int index = 1; index < argc; ++index) {
        if (strcmp(argv[index], "--recording-input") == 0) recording = 1;
        else if (strcmp(argv[index], "--keyboard") == 0) record_keyboard = 1;
        else if (strcmp(argv[index], "--mouse") == 0) record_mouse = 1;
        else { fprintf(stderr, "Unknown input observation option.\n"); return 64; }
    }
    if ((!recording && (record_keyboard || record_mouse)) || (recording && !record_keyboard && !record_mouse)) {
        fprintf(stderr, "Recording input observation requires an enabled keyboard or mouse channel.\n"); return 64;
    }
    signal(SIGPIPE, SIG_IGN);
    if (!XInitThreads()) { fprintf(stderr, "X11 global mouse observation requires thread-safe Xlib.\n"); return 69; }
    XSetErrorHandler(record_error);
    Display *control = XOpenDisplay(NULL), *data = XOpenDisplay(NULL);
    if (!control || !data) {
        fprintf(stderr, "X11 global mouse observation could not connect to the display.\n");
        if (data) XCloseDisplay(data);
        if (control) XCloseDisplay(control);
        return 69;
    }
    int major = 0, minor = 0, opcode = 0, xkb_event = 0, xkb_error = 0;
    int xkb_major = XkbMajorVersion, xkb_minor = XkbMinorVersion;
    if (!XRecordQueryVersion(control, &major, &minor) ||
        !XkbQueryExtension(control, &opcode, &xkb_event, &xkb_error, &xkb_major, &xkb_minor)) {
        fprintf(stderr, "The X11 server must provide RECORD and XKB for passive global mouse observation.\n");
        XCloseDisplay(data); XCloseDisplay(control); return 69;
    }
    unsigned int xkb_mask = XkbNewKeyboardNotifyMask | XkbMapNotifyMask | XkbStateNotifyMask;
    if (!XkbSelectEvents(control, XkbUseCoreKbd, xkb_mask, xkb_mask)) {
        fprintf(stderr, "X11 global mouse observation could not subscribe to keyboard modifier state.\n");
        XCloseDisplay(data); XCloseDisplay(control); return 69;
    }
    XRecordRange *device = XRecordAllocRange(), *reply = XRecordAllocRange(), *keyboard = XRecordAllocRange();
    if (!device || !reply || !keyboard) {
        fprintf(stderr, "X11 mouse observation ranges could not be allocated.\n");
        if (device) XFree(device);
        if (reply) XFree(reply);
        if (keyboard) XFree(keyboard);
        XCloseDisplay(data); XCloseDisplay(control); return 70;
    }
    memset(device, 0, sizeof(*device)); memset(reply, 0, sizeof(*reply)); memset(keyboard, 0, sizeof(*keyboard));
    device->device_events.first = recording && record_keyboard ? KeyPress : ButtonPress;
    device->device_events.last = recording ? (record_mouse ? ButtonRelease : KeyRelease) : MotionNotify;
    reply->core_replies.first = 38; reply->core_replies.last = 38;
    keyboard->delivered_events.first = (unsigned char)xkb_event; keyboard->delivered_events.last = (unsigned char)xkb_event;
    XRecordRange *ranges[] = {device, reply, keyboard};
    Window identity = XCreateSimpleWindow(control, DefaultRootWindow(control), 0, 0, 1, 1, 0, 0, 0);
    XRecordClientSpec client = identity;
    XRecordContext context = XRecordCreateContext(control, 0, &client, 1, ranges, 3);
    XFree(device); XFree(reply); XFree(keyboard);
    XSync(control, False);
    if (!context || atomic_load(&x_error)) {
        fprintf(stderr, "X11 global mouse observation could not create a RECORD context.\n");
        XDestroyWindow(control, identity); XCloseDisplay(data); XCloseDisplay(control); return 70;
    }
    Observer observer = {.control = control, .context = context, .root = DefaultRootWindow(control), .xkb_event = xkb_event,
                         .recording_input = recording, .record_keyboard = record_keyboard, .record_mouse = record_mouse};
    if (recording) {
        XSelectInput(control, identity, PropertyChangeMask);
        unsigned long value = 1;
        uint64_t before = monotonic_time();
        XChangeProperty(control, identity, XA_WM_NAME, XA_INTEGER, 32, PropModeReplace, (unsigned char *)&value, 1);
        XEvent event;
        XWindowEvent(control, identity, PropertyChangeMask, &event);
        uint64_t after = monotonic_time();
        if (event.type != PropertyNotify || !before || !after || atomic_load(&x_error)) {
            fprintf(stderr, "X11 recording input observation could not calibrate its event clock.\n");
            XRecordFreeContext(control, context); XDestroyWindow(control, identity);
            XCloseDisplay(data); XCloseDisplay(control); return 70;
        }
        observer.clock_server = (uint32_t)event.xproperty.time;
        observer.clock_monotonic = before + (after - before) / 2;
    }
    pthread_mutex_init(&observer.gate, NULL);
    pthread_t parent;
    int watcher = 0, result = 70;
    if (pipe(observer.wake) != 0) fprintf(stderr, "X11 mouse observation cancellation pipe could not be created.\n");
    else {
        if (pthread_create(&parent, NULL, watch_parent, &observer) != 0)
            fprintf(stderr, "X11 mouse observation parent monitor could not be started.\n");
        else {
            watcher = 1;
            int enabled = XRecordEnableContext(data, context, record_events, (XPointer)&observer);
            if (enabled && stopping(&observer) && !observer.failed) result = 0;
            else fprintf(stderr, "X11 global mouse observation ended unexpectedly.\n");
        }
        if (watcher) {
            char stop = 1;
            if (write(observer.wake[1], &stop, 1) < 0) fprintf(stderr, "X11 mouse observer shutdown signal failed.\n");
            pthread_join(parent, NULL);
        }
        close(observer.wake[0]); close(observer.wake[1]);
    }
    XRecordFreeContext(control, context); XDestroyWindow(control, identity); XSync(control, False);
    XCloseDisplay(data); XCloseDisplay(control); pthread_mutex_destroy(&observer.gate);
    return result;
}
