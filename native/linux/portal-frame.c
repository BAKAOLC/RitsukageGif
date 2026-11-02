#define _POSIX_C_SOURCE 200809L
#include "portal-frame.h"
#include <errno.h>
#include <poll.h>
#include <string.h>
#include <unistd.h>

#define RGC_FRAME_LIMIT (512u * 1024u * 1024u)

void rgc_json_int(JsonBuilder *builder, const char *name, gint64 value)
{
    json_builder_set_member_name(builder, name);
    json_builder_add_int_value(builder, value);
}
void rgc_json_bool(JsonBuilder *builder, const char *name, gboolean value)
{
    json_builder_set_member_name(builder, name);
    json_builder_add_boolean_value(builder, value);
}
void rgc_json_string(JsonBuilder *builder, const char *name, const char *value)
{
    json_builder_set_member_name(builder, name);
    if (value)
        json_builder_add_string_value(builder, value);
    else
        json_builder_add_null_value(builder);
}

void rgc_frame_writer_init(struct rgc_frame_writer *writer, GCancellable *cancel)
{
    g_mutex_init(&writer->lock);
    writer->cancel = cancel;
    writer->sequence = 0;
}
void rgc_frame_writer_clear(struct rgc_frame_writer *writer)
{
    g_mutex_clear(&writer->lock);
}

static gboolean write_bytes(struct rgc_frame_writer *writer, const void *buffer, size_t count)
{
    const uint8_t *bytes = buffer;
    gint64 deadline = g_get_monotonic_time() + 5 * G_TIME_SPAN_SECOND;
    while (count && !g_cancellable_is_cancelled(writer->cancel)) {
        ssize_t written = write(STDOUT_FILENO, bytes, count);
        if (written > 0) {
            bytes += written;
            count -= (size_t)written;
            continue;
        }
        if (written < 0 && errno == EINTR)
            continue;
        if (written < 0 && (errno == EAGAIN || errno == EWOULDBLOCK)) {
            if (g_get_monotonic_time() >= deadline)
                return FALSE;
            struct pollfd output = {.fd = STDOUT_FILENO, .events = POLLOUT};
            int ready = poll(&output, 1, 100);
            if (ready < 0 && errno != EINTR)
                return FALSE;
            if (ready > 0 && output.revents & (POLLERR | POLLHUP | POLLNVAL))
                return FALSE;
            continue;
        }
        return FALSE;
    }
    return count == 0;
}

static void integer32(uint8_t *target, uint32_t value)
{
    uint32_t little = GUINT32_TO_LE(value);
    memcpy(target, &little, sizeof(little));
}

gboolean rgc_write_packet(struct rgc_frame_writer *writer, uint8_t kind, JsonNode *metadata,
                          const uint8_t *sdr, size_t sdr_length, const uint8_t *hdr, size_t hdr_length,
                          const uint8_t *cursor, size_t cursor_length)
{
    if (sdr_length + hdr_length + cursor_length > RGC_FRAME_LIMIT)
        return FALSE;
    JsonGenerator *generator = json_generator_new();
    json_generator_set_root(generator, metadata);
    gsize metadata_length;
    char *json = json_generator_to_data(generator, &metadata_length);
    g_object_unref(generator);
    if (!json || metadata_length == 0 || metadata_length > 65536) {
        g_free(json);
        return FALSE;
    }
    uint8_t header[32] = {0};
    integer32(header, 0x32434752u);
    header[4] = kind;
    header[6] = 32;
    integer32(header + 8, (uint32_t)metadata_length);
    integer32(header + 12, (uint32_t)sdr_length);
    integer32(header + 16, (uint32_t)hdr_length);
    integer32(header + 20, (uint32_t)cursor_length);
    g_mutex_lock(&writer->lock);
    uint64_t sequence = GUINT64_TO_LE(++writer->sequence);
    memcpy(header + 24, &sequence, sizeof(sequence));
    gboolean written = write_bytes(writer, header, sizeof(header)) &&
                       write_bytes(writer, json, metadata_length) && write_bytes(writer, sdr, sdr_length) &&
                       write_bytes(writer, hdr, hdr_length) && write_bytes(writer, cursor, cursor_length);
    g_mutex_unlock(&writer->lock);
    g_free(json);
    return written;
}
