#ifndef RGC_PORTAL_FRAME_H
#define RGC_PORTAL_FRAME_H
#include <gio/gio.h>
#include <json-glib/json-glib.h>
#include <stdint.h>

struct rgc_frame_writer {
    GMutex lock;
    GCancellable *cancel;
    uint64_t sequence;
};
void rgc_frame_writer_init(struct rgc_frame_writer *writer, GCancellable *cancel);
void rgc_frame_writer_clear(struct rgc_frame_writer *writer);
gboolean rgc_write_packet(struct rgc_frame_writer *writer, uint8_t kind, JsonNode *metadata,
                          const uint8_t *sdr, size_t sdr_length, const uint8_t *hdr, size_t hdr_length,
                          const uint8_t *cursor, size_t cursor_length);
void rgc_json_int(JsonBuilder *builder, const char *name, gint64 value);
void rgc_json_bool(JsonBuilder *builder, const char *name, gboolean value);
void rgc_json_string(JsonBuilder *builder, const char *name, const char *value);
#endif
