#define _POSIX_C_SOURCE 200809L
#include <atspi/atspi.h>
#include <X11/Xlib.h>
#include <X11/Xatom.h>
#include <errno.h>
#include <limits.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define MAX_DEPTH 64
static int x_error;
static int protocol_error(Display *display, XErrorEvent *event)
{
    (void)display; (void)event; x_error = 1; return 0;
}
static int connection_lost(Display *display)
{
    (void)display; _exit(3);
}
static int number(const char *text, int *result)
{
    char *end = NULL; errno = 0; long value = strtol(text, &end, 10);
    if (errno || end == text || *end || value < INT_MIN || value > INT_MAX) return 0;
    *result = (int)value; return 1;
}
static int property(Display *display, Window window, const char *name, unsigned long *values, unsigned long wanted)
{
    Atom actual = None; int format = 0; unsigned long count = 0, remaining = 0; unsigned char *data = NULL;
    Atom atom = XInternAtom(display, name, True);
    if (atom == None) return 0;
    int status = XGetWindowProperty(display, window, atom, 0, (long)wanted, False, XA_CARDINAL,
        &actual, &format, &count, &remaining, &data);
    int success = status == Success && actual == XA_CARDINAL && format == 32 && count == wanted && data != NULL;
    if (success) memcpy(values, data, wanted * sizeof(unsigned long));
    if (data != NULL) XFree(data);
    return success;
}
static int visible_rect(AtspiAccessible *accessible, AtspiRect *result)
{
    AtspiStateSet *states = atspi_accessible_get_state_set(accessible);
    if (states == NULL) return 0;
    int visible = atspi_state_set_contains(states, ATSPI_STATE_VISIBLE) && atspi_state_set_contains(states, ATSPI_STATE_SHOWING);
    g_object_unref(states);
    if (!visible) return 0;
    AtspiComponent *component = atspi_accessible_get_component_iface(accessible);
    if (component == NULL) return 0;
    GError *error = NULL;
    AtspiRect *rect = atspi_component_get_extents(component, ATSPI_COORD_TYPE_SCREEN, &error);
    g_object_unref(component);
    int success = error == NULL && rect != NULL && rect->width > 0 && rect->height > 0;
    if (success) *result = *rect;
    if (error != NULL) g_error_free(error);
    g_free(rect);
    return success;
}
static AtspiAccessible *find_frame(AtspiAccessible *desktop, unsigned long pid, const char *title)
{
    GError *error = NULL;
    int count = atspi_accessible_get_child_count(desktop, &error);
    if (error != NULL) { g_error_free(error); return NULL; }
    AtspiAccessible *found = NULL;
    for (int index = 0; index < count; index++)
    {
        error = NULL; AtspiAccessible *app = atspi_accessible_get_child_at_index(desktop, index, &error);
        if (error != NULL) { g_error_free(error); continue; }
        if (app == NULL) continue;
        error = NULL; guint app_pid = atspi_accessible_get_process_id(app, &error);
        if (error != NULL) { g_error_free(error); g_object_unref(app); continue; }
        if ((unsigned long)app_pid != pid) { g_object_unref(app); continue; }
        error = NULL; int windows = atspi_accessible_get_child_count(app, &error);
        if (error != NULL) { g_error_free(error); g_object_unref(app); continue; }
        for (int child = 0; child < windows; child++)
        {
            error = NULL; AtspiAccessible *frame = atspi_accessible_get_child_at_index(app, child, &error);
            if (error != NULL) { g_error_free(error); continue; }
            if (frame == NULL) continue;
            error = NULL; char *name = atspi_accessible_get_name(frame, &error); AtspiRect rect;
            int matches = error == NULL && g_strcmp0(name, title) == 0 && visible_rect(frame, &rect);
            if (error != NULL) g_error_free(error);
            g_free(name);
            if (!matches) { g_object_unref(frame); continue; }
            if (found != NULL) { g_object_unref(found); g_object_unref(frame); g_object_unref(app); return NULL; }
            found = frame;
        }
        g_object_unref(app);
    }
    return found;
}
static int hierarchy(AtspiAccessible *frame, int x, int y, AtspiRect *rectangles)
{
    AtspiAccessible *current = g_object_ref(frame);
    GHashTable *visited = g_hash_table_new_full(g_direct_hash, g_direct_equal, g_object_unref, NULL);
    g_hash_table_add(visited, g_object_ref(current));
    for (int depth = 0; depth < MAX_DEPTH; depth++)
    {
        AtspiComponent *component = atspi_accessible_get_component_iface(current);
        if (component == NULL) break;
        GError *error = NULL;
        AtspiAccessible *next = atspi_component_get_accessible_at_point(component, x, y, ATSPI_COORD_TYPE_SCREEN, &error);
        g_object_unref(component);
        if (error != NULL) { g_error_free(error); if (next != NULL) g_object_unref(next); break; }
        if (next == NULL) break;
        if (g_hash_table_contains(visited, next)) { g_object_unref(next); break; }
        g_hash_table_add(visited, g_object_ref(next)); g_object_unref(current); current = next;
    }
    g_hash_table_unref(visited);
    if (current == frame) { g_object_unref(current); return 0; }
    visited = g_hash_table_new_full(g_direct_hash, g_direct_equal, g_object_unref, NULL);
    int count = 0, reaches_frame = 0;
    for (int depth = 0; depth < MAX_DEPTH; depth++)
    {
        if (g_hash_table_contains(visited, current)) break;
        g_hash_table_add(visited, g_object_ref(current));
        if (current == frame) { reaches_frame = 1; break; }
        AtspiRect rect;
        if (visible_rect(current, &rect)) rectangles[count++] = rect;
        GError *error = NULL; AtspiAccessible *parent = atspi_accessible_get_parent(current, &error);
        if (error != NULL) { g_error_free(error); if (parent != NULL) g_object_unref(parent); break; }
        if (parent == NULL) break;
        g_object_unref(current); current = parent;
    }
    g_object_unref(current); g_hash_table_unref(visited);
    return reaches_frame ? count : 0;
}
int main(int argc, char **argv)
{
    int probe = argc == 2 && strcmp(argv[1], "--probe") == 0;
    if (!probe && argc != 5) return 2;
    XSetErrorHandler(protocol_error); XSetIOErrorHandler(connection_lost);
    Display *display = XOpenDisplay(NULL);
    if (display == NULL) return 2;
    if (atspi_init() != 0) { XCloseDisplay(display); return 2; }
    atspi_set_timeout(40, 40);
    AtspiAccessible *desktop = atspi_get_desktop(0);
    if (desktop == NULL) { atspi_exit(); XCloseDisplay(display); return 2; }
    if (probe) { g_object_unref(desktop); atspi_exit(); XCloseDisplay(display); puts("AVAILABLE"); return 0; }
    char *end = NULL; errno = 0; unsigned long id = strtoul(argv[1], &end, 16);
    int point_x, point_y;
    if (errno || end == argv[1] || *end || id == 0 || !number(argv[2], &point_x) || !number(argv[3], &point_y)) return 2;
    unsigned long pid = 0; XWindowAttributes attributes; Window child = None; int cx = 0, cy = 0;
    if (!property(display, (Window)id, "_NET_WM_PID", &pid, 1) || pid == 0 || pid == (unsigned long)getppid() ||
        !XGetWindowAttributes(display, (Window)id, &attributes) || attributes.map_state != IsViewable ||
        !XTranslateCoordinates(display, (Window)id, DefaultRootWindow(display), 0, 0, &cx, &cy, &child) ||
        attributes.width <= 0 || attributes.height <= 0 || x_error) return 3;
    AtspiAccessible *frame = find_frame(desktop, pid, argv[4]);
    g_object_unref(desktop);
    if (frame == NULL) { atspi_exit(); XCloseDisplay(display); puts("NOHIT"); return 0; }
    AtspiRect ax;
    if (!visible_rect(frame, &ax)) { g_object_unref(frame); atspi_exit(); XCloseDisplay(display); puts("NOHIT"); return 0; }
    int bx = cx, by = cy, bw = attributes.width, bh = attributes.height;
    unsigned long extents[4];
    if (property(display, (Window)id, "_NET_FRAME_EXTENTS", extents, 4) && extents[0] < 4096 &&
        extents[1] < 4096 && extents[2] < 4096 && extents[3] < 4096)
    {
        int ow = bw + (int)(extents[0] + extents[1]), oh = bh + (int)(extents[2] + extents[3]);
        double client_error = fabs(((double)bw / ax.width) / ((double)bh / ax.height) - 1);
        double outer_error = fabs(((double)ow / ax.width) / ((double)oh / ax.height) - 1);
        if (outer_error < client_error) { bx -= (int)extents[0]; by -= (int)extents[2]; bw = ow; bh = oh; }
    }
    if (point_x < bx || point_y < by || (long long)point_x >= (long long)bx + bw || (long long)point_y >= (long long)by + bh)
    { g_object_unref(frame); atspi_exit(); XCloseDisplay(display); puts("NOHIT"); return 0; }
    double axx = ax.x + ((double)point_x - bx) * ax.width / bw;
    double axy = ax.y + ((double)point_y - by) * ax.height / bh;
    if (axx < INT_MIN || axx > INT_MAX || axy < INT_MIN || axy > INT_MAX) return 3;
    AtspiRect rectangles[MAX_DEPTH]; int count = hierarchy(frame, (int)floor(axx), (int)floor(axy), rectangles);
    g_object_unref(frame); atspi_exit(); XCloseDisplay(display);
    if (count == 0) { puts("NOHIT"); return 0; }
    printf("FRAME %d %d %d %d %d %d %d %d\n", bx, by, bw, bh, ax.x, ax.y, ax.width, ax.height);
    for (int index = count - 1; index >= 0; index--)
        printf("RECT %d %d %d %d\n", rectangles[index].x, rectangles[index].y, rectangles[index].width, rectangles[index].height);
    return 0;
}
