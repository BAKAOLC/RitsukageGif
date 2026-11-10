#ifndef RGC_PORTAL_VIDEO_H
#define RGC_PORTAL_VIDEO_H
#include "portal-frame.h"
#include "portal-scroll-input.h"
#include <pipewire/pipewire.h>
#include <spa/param/video/raw.h>

struct rgc_portal_source {
    uint32_t node_id, source_type, portal_version, cursor_mode, available_cursor_modes;
    uint64_t serial, generation;
    char *id, *restore_token;
    gboolean has_position, has_size, has_logical_size;
    int position_x, position_y, size_width, size_height, logical_width, logical_height;
};
struct rgc_portal_video;
struct rgc_portal_video *rgc_portal_video_new(int descriptor, const struct rgc_portal_source *source,
                                              struct rgc_frame_writer *writer, int fps, gboolean hdr,
                                              gboolean metadata_cursor, uint32_t granted_devices,
                                              gboolean can_request_pointer, GCancellable *cancel,
                                              GError **error);
void rgc_portal_video_free(struct rgc_portal_video *video);
gboolean rgc_portal_video_failed(struct rgc_portal_video *video, char **reason);
gboolean rgc_portal_video_has_frame(struct rgc_portal_video *video);
void rgc_portal_video_pointer(struct rgc_portal_video *video,
                              struct rgc_portal_pointer_observation *observation);
gboolean rgc_portal_video_cursor(struct rgc_portal_video *video, gint64 request_id);
gboolean rgc_portal_video_reply(struct rgc_portal_video *video, gint64 request_id, const char *result,
                                const char *reason);
gboolean rgc_portal_video_capabilities(struct rgc_portal_video *video, gint64 request_id);
gboolean rgc_portal_video_configure(struct rgc_portal_video *video, gint64 request_id, uint64_t generation,
                                    uint32_t stream_id, int x, int y, int width, int height, int output_width,
                                    int output_height, int fps, gboolean hdr);
gboolean rgc_portal_video_set_active(struct rgc_portal_video *video, gint64 request_id, gboolean active);
#endif
