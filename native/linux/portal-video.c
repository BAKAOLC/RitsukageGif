#define _POSIX_C_SOURCE 200809L
#include "portal-video.h"
#include <math.h>
#include <spa/buffer/meta.h>
#include <spa/param/buffers.h>
#include <spa/param/video/format-utils.h>
#include <spa/pod/builder.h>
#include <spa/utils/result.h>
#include <string.h>
#include <unistd.h>

#define FRAME_LIMIT (512u * 1024u * 1024u)
#define CURSOR_LIMIT 1024u

/* Published SPA wire IDs are independent of distribution header age. */
enum rgc_video_color_id {
    RGC_TRANSFER_BT2020_10 = 13,
    RGC_TRANSFER_PQ = 14,
    RGC_TRANSFER_HLG = 15,
    RGC_TRANSFER_BT601 = 16,
    RGC_PRIMARIES_P3_D65 = 11
};

struct rgc_cursor {
    gboolean valid, visible;
    uint32_t bitmap_id;
    int x, y, width, height, hotspot_x, hotspot_y;
    gint64 observed;
    GBytes *pixels;
};
struct rgc_capture_configuration {
    gint64 id;
    int x, y, width, height, output_width, output_height;
};
struct rgc_portal_video {
    GMutex lock;
    struct pw_thread_loop *loop;
    struct pw_context *context;
    struct pw_core *core;
    struct pw_stream *stream;
    struct spa_hook listener, core_listener;
    struct spa_video_info_raw format;
    struct rgc_portal_source source;
    struct rgc_frame_writer *writer;
    GCancellable *cancel;
    struct rgc_cursor cursor;
    struct rgc_capture_configuration configuration;
    gboolean hdr_requested, metadata_cursor, can_request_pointer, has_frame, suspended;
    uint32_t granted_devices;
    int fps;
    gint64 last_frame;
    char *failure;
};
struct rgc_plane {
    GBytes *pixels;
    const char *format, *alpha, *transfer, *primaries;
    gboolean high, hdr;
    int stride;
    const char *reason;
};

static void failed(struct rgc_portal_video *video, const char *reason)
{
    g_mutex_lock(&video->lock);
    if (!video->failure)
        video->failure = g_strdup(reason);
    g_mutex_unlock(&video->lock);
}

static const char *transfer_name(uint32_t transfer)
{
    switch ((uint32_t)transfer) {
    case SPA_VIDEO_TRANSFER_GAMMA10:
        return "linear";
    case RGC_TRANSFER_PQ:
        return "pq";
    case RGC_TRANSFER_HLG:
        return "hlg";
    case SPA_VIDEO_TRANSFER_GAMMA22:
        return "gamma22";
    case SPA_VIDEO_TRANSFER_GAMMA28:
        return "gamma28";
    case SPA_VIDEO_TRANSFER_ADOBERGB:
        return "adobergb";
    case SPA_VIDEO_TRANSFER_SRGB:
        return "srgb";
    case SPA_VIDEO_TRANSFER_BT709:
    case RGC_TRANSFER_BT2020_10:
    case SPA_VIDEO_TRANSFER_BT2020_12:
    case RGC_TRANSFER_BT601:
        return "bt709";
    default:
        return "unknown";
    }
}
static const char *primaries_name(uint32_t primaries)
{
    switch ((uint32_t)primaries) {
    case SPA_VIDEO_COLOR_PRIMARIES_BT709:
        return "bt709";
    case SPA_VIDEO_COLOR_PRIMARIES_BT2020:
        return "bt2020";
    case RGC_PRIMARIES_P3_D65:
        return "display-p3";
    default:
        return "unknown";
    }
}
static unsigned input_pixel_size(uint32_t format)
{
    switch (format) {
    case SPA_VIDEO_FORMAT_RGBA_F16:
        return 8;
    case SPA_VIDEO_FORMAT_RGBA_F32:
        return 16;
    case SPA_VIDEO_FORMAT_BGRA:
    case SPA_VIDEO_FORMAT_RGBA:
    case SPA_VIDEO_FORMAT_BGRx:
    case SPA_VIDEO_FORMAT_RGBx:
    case SPA_VIDEO_FORMAT_xRGB:
    case SPA_VIDEO_FORMAT_xBGR:
    case SPA_VIDEO_FORMAT_ARGB:
    case SPA_VIDEO_FORMAT_ABGR:
    case SPA_VIDEO_FORMAT_xRGB_210LE:
    case SPA_VIDEO_FORMAT_xBGR_210LE:
    case SPA_VIDEO_FORMAT_RGBx_102LE:
    case SPA_VIDEO_FORMAT_BGRx_102LE:
    case SPA_VIDEO_FORMAT_ARGB_210LE:
    case SPA_VIDEO_FORMAT_ABGR_210LE:
    case SPA_VIDEO_FORMAT_RGBA_102LE:
    case SPA_VIDEO_FORMAT_BGRA_102LE:
        return 4;
    default:
        return 0;
    }
}
static gboolean packed10(uint32_t format)
{
    return format >= SPA_VIDEO_FORMAT_xRGB_210LE && format <= SPA_VIDEO_FORMAT_BGRA_102LE;
}
static gboolean has_alpha(uint32_t format)
{
    switch (format) {
    case SPA_VIDEO_FORMAT_RGBA:
    case SPA_VIDEO_FORMAT_BGRA:
    case SPA_VIDEO_FORMAT_ARGB:
    case SPA_VIDEO_FORMAT_ABGR:
    case SPA_VIDEO_FORMAT_RGBA_F16:
    case SPA_VIDEO_FORMAT_RGBA_F32:
    case SPA_VIDEO_FORMAT_ARGB_210LE:
    case SPA_VIDEO_FORMAT_ABGR_210LE:
    case SPA_VIDEO_FORMAT_RGBA_102LE:
    case SPA_VIDEO_FORMAT_BGRA_102LE:
        return TRUE;
    default:
        return FALSE;
    }
}

static void rgba8(const uint8_t *source, uint32_t format, uint8_t *rgba)
{
    switch (format) {
    case SPA_VIDEO_FORMAT_RGBA:
    case SPA_VIDEO_FORMAT_RGBx:
        rgba[0] = source[0];
        rgba[1] = source[1];
        rgba[2] = source[2];
        rgba[3] = source[3];
        break;
    case SPA_VIDEO_FORMAT_BGRA:
    case SPA_VIDEO_FORMAT_BGRx:
        rgba[0] = source[2];
        rgba[1] = source[1];
        rgba[2] = source[0];
        rgba[3] = source[3];
        break;
    case SPA_VIDEO_FORMAT_ARGB:
    case SPA_VIDEO_FORMAT_xRGB:
        rgba[0] = source[1];
        rgba[1] = source[2];
        rgba[2] = source[3];
        rgba[3] = source[0];
        break;
    case SPA_VIDEO_FORMAT_ABGR:
    case SPA_VIDEO_FORMAT_xBGR:
        rgba[0] = source[3];
        rgba[1] = source[2];
        rgba[2] = source[1];
        rgba[3] = source[0];
        break;
    default:
        memset(rgba, 0, 4);
        break;
    }
    if (!has_alpha(format))
        rgba[3] = 255;
}
static void unpack10(uint32_t value, uint32_t format, uint16_t *rgba)
{
    gboolean low_alpha = format == SPA_VIDEO_FORMAT_RGBx_102LE || format == SPA_VIDEO_FORMAT_BGRx_102LE ||
                         format == SPA_VIDEO_FORMAT_RGBA_102LE || format == SPA_VIDEO_FORMAT_BGRA_102LE;
    gboolean blue_first = format == SPA_VIDEO_FORMAT_xBGR_210LE || format == SPA_VIDEO_FORMAT_ABGR_210LE ||
                          format == SPA_VIDEO_FORMAT_BGRx_102LE || format == SPA_VIDEO_FORMAT_BGRA_102LE;
    uint32_t first = low_alpha ? (value >> 22) & 1023 : (value >> 20) & 1023;
    uint32_t green = low_alpha ? (value >> 12) & 1023 : (value >> 10) & 1023;
    uint32_t third = low_alpha ? (value >> 2) & 1023 : value & 1023;
    uint32_t alpha = has_alpha(format) ? (low_alpha ? value & 3 : value >> 30) : 3;
    rgba[0] = (uint16_t)(((blue_first ? third : first) * 65535u + 511u) / 1023u);
    rgba[1] = (uint16_t)((green * 65535u + 511u) / 1023u);
    rgba[2] = (uint16_t)(((blue_first ? first : third) * 65535u + 511u) / 1023u);
    rgba[3] = (uint16_t)(alpha * 21845u);
}

static float encoded_to_linear(float value, uint32_t transfer)
{
    switch (transfer) {
    case SPA_VIDEO_TRANSFER_GAMMA10:
        return value;
    case SPA_VIDEO_TRANSFER_GAMMA18:
        return powf(value, 1.8f);
    case SPA_VIDEO_TRANSFER_GAMMA20:
        return value * value;
    case SPA_VIDEO_TRANSFER_GAMMA22:
        return powf(value, 2.2f);
    case SPA_VIDEO_TRANSFER_GAMMA28:
        return powf(value, 2.8f);
    case SPA_VIDEO_TRANSFER_ADOBERGB:
        return powf(value, 2.19921875f);
    case SPA_VIDEO_TRANSFER_BT709:
    case SPA_VIDEO_TRANSFER_BT2020_12:
    case RGC_TRANSFER_BT2020_10:
    case RGC_TRANSFER_BT601:
        return value < .081f ? value / 4.5f : powf((value + .099f) / 1.099f, 1.f / .45f);
    default:
        return value <= .04045f ? value / 12.92f : powf((value + .055f) / 1.055f, 2.4f);
    }
}
static void sdr_color(uint8_t *rgba, const struct spa_video_info_raw *info)
{
    if ((info->transfer_function == SPA_VIDEO_TRANSFER_UNKNOWN ||
         info->transfer_function == SPA_VIDEO_TRANSFER_SRGB) &&
        (info->color_primaries == SPA_VIDEO_COLOR_PRIMARIES_UNKNOWN ||
         info->color_primaries == SPA_VIDEO_COLOR_PRIMARIES_BT709))
        return;
    float r = encoded_to_linear(rgba[0] / 255.f, info->transfer_function);
    float g = encoded_to_linear(rgba[1] / 255.f, info->transfer_function);
    float b = encoded_to_linear(rgba[2] / 255.f, info->transfer_function);
    float output[3] = {r, g, b};
    if (info->color_primaries == SPA_VIDEO_COLOR_PRIMARIES_BT2020) {
        output[0] = 1.660491f * r - .587641f * g - .07285f * b;
        output[1] = -.12455f * r + 1.1329f * g - .00835f * b;
        output[2] = -.018151f * r - .100579f * g + 1.11873f * b;
    } else if ((uint32_t)info->color_primaries == RGC_PRIMARIES_P3_D65) {
        output[0] = 1.224745f * r - .224904f * g;
        output[1] = -.042058f * r + 1.042081f * g;
        output[2] = -.019642f * r - .078655f * g + 1.098537f * b;
    }
    for (unsigned channel = 0; channel < 3; channel++) {
        float linear = fmaxf(0, output[channel]);
        float encoded = linear <= .0031308f ? linear * 12.92f : 1.055f * powf(linear, 1.f / 2.4f) - .055f;
        rgba[channel] = (uint8_t)lrintf(fminf(1, encoded) * 255.f);
    }
}

static uint16_t full_range16(uint16_t value, unsigned bits)
{
    gint64 numerator = (gint64)value * ((1u << bits) - 1u) - (gint64)(16u << (bits - 8)) * 65535;
    gint64 denominator = 219u << (bits - 8);
    return (uint16_t)CLAMP((numerator + denominator / 2) / denominator, 0, 65535);
}
static float full_range_float(float value)
{
    return fminf(1, fmaxf(0, (value - 16.f / 255.f) / (219.f / 255.f)));
}
static void describe_plane(const struct spa_video_info_raw *info, struct rgc_plane *plane)
{
    plane->transfer = transfer_name(info->transfer_function);
    plane->primaries = primaries_name(info->color_primaries);
    gboolean float_pixels =
        info->format == SPA_VIDEO_FORMAT_RGBA_F16 || info->format == SPA_VIDEO_FORMAT_RGBA_F32;
    gboolean encoded_hdr = (uint32_t)info->transfer_function == RGC_TRANSFER_PQ ||
                           (uint32_t)info->transfer_function == RGC_TRANSFER_HLG;
    plane->high = float_pixels || packed10(info->format) || encoded_hdr;
    plane->hdr = encoded_hdr || (float_pixels && info->transfer_function == SPA_VIDEO_TRANSFER_GAMMA10 &&
                                 info->color_primaries != SPA_VIDEO_COLOR_PRIMARIES_UNKNOWN);
    plane->format = plane->high ? (float_pixels ? "rgba16f" : "rgba16") : "bgra8";
    plane->alpha = !has_alpha(info->format)                           ? "opaque"
                   : info->flags & SPA_VIDEO_FLAG_PREMULTIPLIED_ALPHA ? "premultiplied"
                                                                      : "straight";
    if (plane->high && (!strcmp(plane->transfer, "unknown") || !strcmp(plane->primaries, "unknown")))
        plane->reason = "The high-depth source did not provide supported color encoding metadata.";
    else if (!plane->hdr)
        plane->reason = "The selected source does not provide HDR pixels.";
    plane->stride = (int)info->size.width * (plane->high ? 8 : 4);
}

static gboolean plane_from_buffer(const struct spa_video_info_raw *info, const struct spa_data *data,
                                  const struct rgc_capture_configuration *configuration,
                                  struct rgc_plane *plane, GError **error)
{
    unsigned pixel_size = input_pixel_size(info->format);
    if (!pixel_size || !data->data || !data->chunk || !info->size.width || !info->size.height) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_NOT_SUPPORTED,
                            "The source did not provide a supported mapped video buffer.");
        return FALSE;
    }
    size_t row = (size_t)info->size.width * pixel_size;
    int stride = data->chunk->stride == 0 ? (int)row : data->chunk->stride;
    uint64_t required = (uint64_t)(info->size.height - 1) * (unsigned)MAX(stride, 0) + row;
    if (stride < 0 || (size_t)stride < row || data->chunk->offset > data->maxsize ||
        required > data->maxsize - data->chunk->offset || required > data->chunk->size) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_INVALID_DATA,
                            "The source video buffer dimensions or stride are invalid.");
        return FALSE;
    }
    const int x0 = configuration->x, y0 = configuration->y;
    const int crop_width = configuration->width ? configuration->width : (int)info->size.width;
    const int crop_height = configuration->height ? configuration->height : (int)info->size.height;
    const int output_width = configuration->output_width ? configuration->output_width : crop_width;
    const int output_height = configuration->output_height ? configuration->output_height : crop_height;
    if (x0 < 0 || y0 < 0 || crop_width <= 0 || crop_height <= 0 || output_width <= 0 || output_height <= 0 ||
        (uint64_t)x0 + crop_width > info->size.width || (uint64_t)y0 + crop_height > info->size.height) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_INVALID_DATA,
                            "The configured capture region no longer fits the authorized source.");
        return FALSE;
    }
    describe_plane(info, plane);
    uint64_t bytes = (uint64_t)output_width * output_height * (plane->high ? 8 : 4);
    if (bytes > FRAME_LIMIT) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_NO_SPACE,
                            "The source frame exceeds the capture memory limit.");
        return FALSE;
    }
    uint8_t *output = g_try_malloc((gsize)bytes);
    if (!output) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_NO_SPACE,
                            "There is not enough memory for the captured frame.");
        return FALSE;
    }
    const uint8_t *input = (const uint8_t *)data->data + data->chunk->offset;
    for (int y = 0; y < output_height; y++) {
        int source_y = y0 + (int)(((uint64_t)y * 2 + 1) * crop_height / ((uint64_t)output_height * 2));
        const uint8_t *source = input + (size_t)source_y * (unsigned)stride + (size_t)x0 * pixel_size;
        uint8_t *target = output + (size_t)y * output_width * (plane->high ? 8 : 4);
        gboolean limited = info->color_range == SPA_VIDEO_COLOR_RANGE_16_235 &&
                           info->transfer_function != SPA_VIDEO_TRANSFER_GAMMA10;
        gboolean plain_sdr = !plane->high && !limited &&
                             !(info->flags & SPA_VIDEO_FLAG_PREMULTIPLIED_ALPHA) &&
                             (info->transfer_function == SPA_VIDEO_TRANSFER_UNKNOWN ||
                              info->transfer_function == SPA_VIDEO_TRANSFER_SRGB) &&
                             (info->color_primaries == SPA_VIDEO_COLOR_PRIMARIES_UNKNOWN ||
                              info->color_primaries == SPA_VIDEO_COLOR_PRIMARIES_BT709);
        if (plain_sdr && output_width == crop_width &&
            (info->format == SPA_VIDEO_FORMAT_BGRA || info->format == SPA_VIDEO_FORMAT_BGRx)) {
            memcpy(target, source, (size_t)output_width * 4);
            if (info->format == SPA_VIDEO_FORMAT_BGRx)
                for (int x = 0; x < output_width; x++)
                    target[x * 4 + 3] = 255;
            continue;
        }
        if (info->format == SPA_VIDEO_FORMAT_RGBA_F16 && !limited && output_width == crop_width) {
            memcpy(target, source, (size_t)output_width * 8);
            continue;
        }
        for (int x = 0; x < output_width; x++) {
            size_t sample_x = ((uint64_t)x * 2 + 1) * crop_width / ((uint64_t)output_width * 2);
            if (info->format == SPA_VIDEO_FORMAT_RGBA_F32 || info->format == SPA_VIDEO_FORMAT_RGBA_F16) {
                for (unsigned channel = 0; channel < 4; channel++) {
                    float component;
                    if (info->format == SPA_VIDEO_FORMAT_RGBA_F32)
                        memcpy(&component, source + sample_x * 16 + channel * 4, 4);
                    else {
                        _Float16 original;
                        memcpy(&original, source + sample_x * 8 + channel * 2, 2);
                        component = (float)original;
                    }
                    if (limited && channel < 3)
                        component = full_range_float(component);
                    _Float16 half =
                        (_Float16)(isfinite(component) ? fminf(65504, fmaxf(-65504, component)) : 0);
                    uint16_t bits;
                    memcpy(&bits, &half, 2);
                    bits = GUINT16_TO_LE(bits);
                    memcpy(target + (size_t)x * 8 + channel * 2, &bits, 2);
                }
            } else if (packed10(info->format)) {
                uint32_t packed;
                uint16_t rgba[4];
                memcpy(&packed, source + sample_x * 4, 4);
                unpack10(GUINT32_FROM_LE(packed), info->format, rgba);
                for (unsigned channel = 0; channel < 4; channel++) {
                    uint16_t component =
                        limited && channel < 3 ? full_range16(rgba[channel], 10) : rgba[channel];
                    uint16_t value = GUINT16_TO_LE(component);
                    memcpy(target + (size_t)x * 8 + channel * 2, &value, 2);
                }
            } else {
                uint8_t rgba[4];
                rgba8(source + sample_x * 4, info->format, rgba);
                if (plane->high) {
                    for (unsigned channel = 0; channel < 4; channel++) {
                        uint16_t component = (uint16_t)(rgba[channel] * 257u);
                        if (limited && channel < 3)
                            component = full_range16(component, 8);
                        uint16_t value = GUINT16_TO_LE(component);
                        memcpy(target + (size_t)x * 8 + channel * 2, &value, 2);
                    }
                } else {
                    if (limited)
                        for (unsigned channel = 0; channel < 3; channel++)
                            rgba[channel] =
                                (uint8_t)((full_range16((uint16_t)(rgba[channel] * 257u), 8) + 128u) / 257u);
                    if (info->flags & SPA_VIDEO_FLAG_PREMULTIPLIED_ALPHA)
                        for (unsigned channel = 0; channel < 3; channel++)
                            rgba[channel] =
                                rgba[3] ? (uint8_t)MIN(255u, (rgba[channel] * 255u + rgba[3] / 2u) / rgba[3])
                                        : 0;
                    sdr_color(rgba, info);
                    target[x * 4] = rgba[2];
                    target[x * 4 + 1] = rgba[1];
                    target[x * 4 + 2] = rgba[0];
                    target[x * 4 + 3] = rgba[3];
                }
            }
        }
    }
    if (!plane->high) {
        plane->alpha = "straight";
        plane->transfer = "srgb";
        plane->primaries = "bt709";
    }
    plane->stride = output_width * (plane->high ? 8 : 4);
    plane->pixels = g_bytes_new_take(output, (gsize)bytes);
    return TRUE;
}
static void portal_metadata(JsonBuilder *builder, const struct rgc_portal_source *source,
                            gboolean include_token)
{
    json_builder_set_member_name(builder, "portal");
    json_builder_begin_object(builder);
    rgc_json_string(builder, "id", source->id);
    rgc_json_int(builder, "serial", (gint64)source->serial);
    rgc_json_int(builder, "type", source->source_type);
    rgc_json_int(builder, "version", source->portal_version);
    const char *names[] = {"position", "size", "logicalSize"};
    gboolean present[] = {source->has_position, source->has_size, source->has_logical_size};
    int first[] = {source->position_x, source->size_width, source->logical_width};
    int second[] = {source->position_y, source->size_height, source->logical_height};
    for (unsigned index = 0; index < 3; index++)
        if (present[index]) {
            json_builder_set_member_name(builder, names[index]);
            json_builder_begin_array(builder);
            json_builder_add_int_value(builder, first[index]);
            json_builder_add_int_value(builder, second[index]);
            json_builder_end_array(builder);
        }
    if (include_token)
        rgc_json_string(builder, "restoreToken", source->restore_token);
    json_builder_end_object(builder);
}
static void geometry_metadata(JsonBuilder *builder, struct rgc_portal_video *video, const char *name,
                              uint32_t source_width, uint32_t source_height,
                              const struct rgc_capture_configuration *configuration)
{
    char stream[32];
    g_snprintf(stream, sizeof(stream), "%u", video->source.node_id);
    json_builder_set_member_name(builder, name);
    json_builder_begin_object(builder);
    rgc_json_string(builder, "sourceId", video->source.id);
    rgc_json_bool(builder, "stableIdentity", video->source.id && *video->source.id);
    rgc_json_string(builder, "streamId", stream);
    rgc_json_int(builder, "generation", (gint64)video->source.generation);
    rgc_json_string(builder, "space", "local");
    rgc_json_int(builder, "x", configuration ? configuration->x : 0);
    rgc_json_int(builder, "y", configuration ? configuration->y : 0);
    rgc_json_int(builder, "width",
                 configuration && configuration->width ? (uint32_t)configuration->width : source_width);
    rgc_json_int(builder, "height",
                 configuration && configuration->height ? (uint32_t)configuration->height : source_height);
    rgc_json_int(builder, "pixelWidth",
                 configuration && configuration->output_width ? (uint32_t)configuration->output_width : source_width);
    rgc_json_int(builder, "pixelHeight",
                 configuration && configuration->output_height ? (uint32_t)configuration->output_height
                                                               : source_height);
    rgc_json_int(builder, "backingScale", 1);
    json_builder_end_object(builder);
}
static void encoding_metadata(JsonBuilder *builder, const struct spa_video_info_raw *info)
{
    json_builder_set_member_name(builder, "encoding");
    json_builder_begin_object(builder);
    rgc_json_int(builder, "format", info->format);
    rgc_json_int(builder, "range", info->color_range);
    rgc_json_int(builder, "matrix", info->color_matrix);
    rgc_json_int(builder, "transfer", info->transfer_function);
    rgc_json_int(builder, "primaries", info->color_primaries);
    rgc_json_int(builder, "chromaSite", info->chroma_site);
    json_builder_end_object(builder);
}
static void plane_metadata(JsonBuilder *builder, const struct spa_video_info_raw *info,
                           const struct rgc_plane *plane)
{
    json_builder_set_member_name(builder, plane->high ? "hdr" : "sdr");
    json_builder_begin_object(builder);
    rgc_json_int(builder, "width", info->size.width);
    rgc_json_int(builder, "height", info->size.height);
    rgc_json_int(builder, "stride", plane->stride);
    rgc_json_string(builder, "format", plane->format);
    rgc_json_string(builder, "alpha", plane->alpha);
    json_builder_set_member_name(builder, "color");
    json_builder_begin_object(builder);
    rgc_json_bool(builder, "hdr", plane->hdr);
    rgc_json_string(builder, "transfer", plane->transfer);
    rgc_json_string(builder, "primaries", plane->primaries);
    rgc_json_int(builder, "sdrWhiteNits", 80);
    rgc_json_int(builder, "linearReferenceNits", 80);
    rgc_json_int(builder, "peakNits", 1000);
    json_builder_end_object(builder);
    json_builder_end_object(builder);
}
static void cursor_metadata(JsonBuilder *builder, const struct rgc_cursor *cursor)
{
    json_builder_set_member_name(builder, "cursor");
    json_builder_begin_object(builder);
    rgc_json_bool(builder, "valid", cursor->valid);
    rgc_json_bool(builder, "visible", cursor->visible);
    rgc_json_int(builder, "bitmapId", cursor->bitmap_id);
    rgc_json_int(builder, "x", cursor->x);
    rgc_json_int(builder, "y", cursor->y);
    rgc_json_int(builder, "width", cursor->width);
    rgc_json_int(builder, "height", cursor->height);
    rgc_json_int(builder, "hotspotX", cursor->hotspot_x);
    rgc_json_int(builder, "hotspotY", cursor->hotspot_y);
    rgc_json_int(builder, "pixelScale", 1);
    rgc_json_int(builder, "observedNanoseconds", cursor->observed * 1000);
    json_builder_end_object(builder);
}
static void capabilities_metadata(JsonBuilder *builder, struct rgc_portal_video *video,
                                  const struct rgc_plane *plane, const char *reason)
{
    json_builder_set_member_name(builder, "capabilities");
    json_builder_begin_object(builder);
    rgc_json_bool(builder, "screen", video->source.source_type == 1);
    rgc_json_bool(builder, "window", video->source.source_type == 2);
    rgc_json_bool(builder, "hdr", plane && plane->hdr && !plane->reason);
    rgc_json_bool(builder, "separateCursor", video->metadata_cursor);
    rgc_json_bool(builder, "pointerInput", video->metadata_cursor && (video->granted_devices & 2));
    rgc_json_bool(builder, "canRequestPointerInput", video->metadata_cursor && video->can_request_pointer);
    rgc_json_string(builder, "reason", reason ? reason : plane ? plane->reason : NULL);
    json_builder_end_object(builder);
}

static struct rgc_cursor cursor_copy(struct rgc_portal_video *video)
{
    g_mutex_lock(&video->lock);
    struct rgc_cursor cursor = video->cursor;
    if (cursor.pixels)
        g_bytes_ref(cursor.pixels);
    g_mutex_unlock(&video->lock);
    return cursor;
}
static void update_cursor(struct rgc_portal_video *video, struct spa_buffer *buffer, gint64 now)
{
    struct spa_meta *meta = spa_buffer_find_meta(buffer, SPA_META_Cursor);
    if (!video->metadata_cursor || !meta || meta->size < sizeof(struct spa_meta_cursor))
        return;
    const struct spa_meta_cursor *cursor = meta->data;
    if (!spa_meta_cursor_is_valid(cursor)) {
        g_mutex_lock(&video->lock);
        video->cursor.valid = FALSE;
        video->cursor.visible = FALSE;
        video->cursor.observed = now;
        g_mutex_unlock(&video->lock);
        return;
    }
    g_mutex_lock(&video->lock);
    video->cursor.valid = TRUE;
    video->cursor.visible = video->cursor.pixels != NULL;
    video->cursor.x = cursor->position.x;
    video->cursor.y = cursor->position.y;
    video->cursor.hotspot_x = cursor->hotspot.x;
    video->cursor.hotspot_y = cursor->hotspot.y;
    video->cursor.observed = now;
    if (cursor->bitmap_offset >= sizeof(*cursor) &&
        cursor->bitmap_offset <= meta->size - sizeof(struct spa_meta_bitmap)) {
        const struct spa_meta_bitmap *bitmap =
            SPA_PTROFF(cursor, cursor->bitmap_offset, const struct spa_meta_bitmap);
        if (spa_meta_bitmap_is_valid(bitmap)) {
            size_t location = (size_t)cursor->bitmap_offset + bitmap->offset;
            unsigned bpp = input_pixel_size(bitmap->format);
            uint64_t extent = bitmap->size.height
                                  ? (uint64_t)(bitmap->size.height - 1) * (unsigned)MAX(bitmap->stride, 0) +
                                        bitmap->size.width * 4u
                                  : 0;
            if (bitmap->offset == 0 || bitmap->size.width == 0 || bitmap->size.height == 0) {
                video->cursor.visible = FALSE;
            } else if (bpp == 4 && !packed10(bitmap->format) && bitmap->size.width <= CURSOR_LIMIT &&
                       bitmap->size.height <= CURSOR_LIMIT && bitmap->stride > 0 &&
                       (unsigned)bitmap->stride >= bitmap->size.width * 4u && location <= meta->size &&
                       extent <= meta->size - location) {
                size_t length = (size_t)bitmap->size.width * bitmap->size.height * 4;
                uint8_t *pixels = g_malloc(length);
                const uint8_t *input = SPA_PTROFF(cursor, location, const uint8_t);
                for (uint32_t y = 0; y < bitmap->size.height; y++)
                    for (uint32_t x = 0; x < bitmap->size.width; x++) {
                        uint8_t rgba[4];
                        rgba8(input + (size_t)y * bitmap->stride + x * 4, bitmap->format, rgba);
                        for (unsigned channel = 0; channel < 3; channel++)
                            rgba[channel] =
                                rgba[3] ? (uint8_t)MIN(255u, (rgba[channel] * 255u + rgba[3] / 2u) / rgba[3])
                                        : 0;
                        uint8_t *pixel = pixels + ((size_t)y * bitmap->size.width + x) * 4;
                        pixel[0] = rgba[2];
                        pixel[1] = rgba[1];
                        pixel[2] = rgba[0];
                        pixel[3] = rgba[3];
                    }
                g_clear_pointer(&video->cursor.pixels, g_bytes_unref);
                video->cursor.pixels = g_bytes_new_take(pixels, length);
                video->cursor.bitmap_id++;
                video->cursor.width = (int)bitmap->size.width;
                video->cursor.height = (int)bitmap->size.height;
                video->cursor.visible = TRUE;
            } else {
                video->cursor.valid = FALSE;
                video->cursor.visible = FALSE;
            }
        }
    }
    g_mutex_unlock(&video->lock);
}

static gboolean write_metadata_packet(struct rgc_portal_video *video, uint8_t kind, gint64 request_id,
                                      const char *result, const char *reason, gboolean include_cursor)
{
    struct rgc_cursor cursor = cursor_copy(video);
    struct spa_video_info_raw info;
    struct rgc_capture_configuration configuration;
    g_mutex_lock(&video->lock);
    info = video->format;
    configuration = video->configuration;
    g_mutex_unlock(&video->lock);
    JsonBuilder *builder = json_builder_new();
    json_builder_begin_object(builder);
    rgc_json_int(builder, "timestampNanoseconds", g_get_monotonic_time() * 1000);
    rgc_json_int(builder, "requestId", request_id);
    rgc_json_string(builder, "result", result);
    rgc_json_string(builder, "reason", reason);
    geometry_metadata(builder, video, "sourceGeometry", info.size.width, info.size.height, NULL);
    geometry_metadata(builder, video, "geometry", info.size.width, info.size.height, &configuration);
    rgc_json_int(builder, "configurationId", configuration.id);
    portal_metadata(builder, &video->source, kind == 3);
    if (kind == 3) {
        struct rgc_plane plane = {0};
        describe_plane(&info, &plane);
        capabilities_metadata(builder, video, &plane, reason);
    }
    if (include_cursor)
        cursor_metadata(builder, &cursor);
    json_builder_end_object(builder);
    JsonNode *metadata = json_builder_get_root(builder);
    gsize length = 0;
    const uint8_t *pixels =
        include_cursor && cursor.valid && cursor.pixels ? g_bytes_get_data(cursor.pixels, &length) : NULL;
    gboolean success = rgc_write_packet(video->writer, kind, metadata, NULL, 0, NULL, 0, pixels, length);
    json_node_free(metadata);
    g_object_unref(builder);
    if (cursor.pixels)
        g_bytes_unref(cursor.pixels);
    return success;
}
gboolean rgc_portal_video_cursor(struct rgc_portal_video *video, gint64 request_id)
{
    return write_metadata_packet(video, 4, request_id, NULL, NULL, TRUE);
}
gboolean rgc_portal_video_reply(struct rgc_portal_video *video, gint64 request_id, const char *result,
                                const char *reason)
{
    return write_metadata_packet(video, 2, request_id, result, reason, FALSE);
}
gboolean rgc_portal_video_capabilities(struct rgc_portal_video *video, gint64 request_id)
{
    return write_metadata_packet(video, 3, request_id, NULL, NULL, FALSE);
}
static void on_process(void *userdata)
{
    struct rgc_portal_video *video = userdata;
    struct pw_buffer *latest = NULL, *next;
    while ((next = pw_stream_dequeue_buffer(video->stream))) {
        if (latest)
            pw_stream_queue_buffer(video->stream, latest);
        latest = next;
    }
    if (!latest)
        return;
    struct spa_buffer *buffer = latest->buffer;
    gint64 now = g_get_monotonic_time();
    update_cursor(video, buffer, now);
    if (video->suspended) {
        pw_stream_queue_buffer(video->stream, latest);
        return;
    }
    struct spa_meta_header *header = spa_buffer_find_meta_data(buffer, SPA_META_Header, sizeof(*header));
    if ((header && header->flags & SPA_META_HEADER_FLAG_CORRUPTED) || !buffer->n_datas ||
        !buffer->datas[0].chunk || !buffer->datas[0].chunk->size) {
        pw_stream_queue_buffer(video->stream, latest);
        if (!rgc_portal_video_cursor(video, 0))
            failed(video, "The capture output pipe closed.");
        return;
    }
    if (now - video->last_frame < G_TIME_SPAN_SECOND / video->fps) {
        pw_stream_queue_buffer(video->stream, latest);
        return;
    }
    struct spa_video_info_raw info;
    struct rgc_capture_configuration configuration;
    g_mutex_lock(&video->lock);
    info = video->format;
    configuration = video->configuration;
    g_mutex_unlock(&video->lock);
    struct rgc_plane plane = {0};
    GError *error = NULL;
    gboolean converted = plane_from_buffer(&info, &buffer->datas[0], &configuration, &plane, &error);
    gint64 timestamp = header && header->pts >= 0 ? header->pts : now * 1000;
    pw_stream_queue_buffer(video->stream, latest);
    if (!converted) {
        failed(video, error ? error->message : "The source video format is unsupported.");
        g_clear_error(&error);
        return;
    }
    g_mutex_lock(&video->lock);
    gboolean current_configuration = configuration.id == video->configuration.id;
    g_mutex_unlock(&video->lock);
    if (!current_configuration) {
        g_bytes_unref(plane.pixels);
        return;
    }
    struct rgc_cursor cursor = cursor_copy(video);
    JsonBuilder *builder = json_builder_new();
    json_builder_begin_object(builder);
    rgc_json_int(builder, "timestampNanoseconds", timestamp);
    geometry_metadata(builder, video, "sourceGeometry", info.size.width, info.size.height, NULL);
    geometry_metadata(builder, video, "geometry", info.size.width, info.size.height, &configuration);
    rgc_json_int(builder, "configurationId", configuration.id);
    portal_metadata(builder, &video->source, FALSE);
    encoding_metadata(builder, &info);
    struct spa_video_info_raw output_info = info;
    if (configuration.output_width)
        output_info.size.width = (uint32_t)configuration.output_width;
    if (configuration.output_height)
        output_info.size.height = (uint32_t)configuration.output_height;
    plane_metadata(builder, &output_info, &plane);
    if (video->metadata_cursor)
        cursor_metadata(builder, &cursor);
    const char *reason = plane.reason;
    if (!video->metadata_cursor && !reason)
        reason = "The portal does not support independent cursor metadata for this source.";
    capabilities_metadata(builder, video, &plane, reason);
    json_builder_end_object(builder);
    JsonNode *metadata = json_builder_get_root(builder);
    gsize length = 0, cursor_length = 0;
    const uint8_t *pixels = g_bytes_get_data(plane.pixels, &length);
    const uint8_t *cursor_pixels =
        cursor.valid && cursor.pixels ? g_bytes_get_data(cursor.pixels, &cursor_length) : NULL;
    gboolean written =
        rgc_write_packet(video->writer, 1, metadata, plane.high ? NULL : pixels, plane.high ? 0 : length,
                         plane.high ? pixels : NULL, plane.high ? length : 0, cursor_pixels, cursor_length);
    json_node_free(metadata);
    g_object_unref(builder);
    g_bytes_unref(plane.pixels);
    if (cursor.pixels)
        g_bytes_unref(cursor.pixels);
    if (!written) {
        failed(video, "The capture output pipe closed.");
        return;
    }
    video->last_frame = now;
    g_mutex_lock(&video->lock);
    video->has_frame = TRUE;
    g_mutex_unlock(&video->lock);
}

static void on_state_changed(void *userdata, enum pw_stream_state old_state, enum pw_stream_state state,
                             const char *error)
{
    struct rgc_portal_video *video = userdata;
    if (state == PW_STREAM_STATE_ERROR ||
        (state == PW_STREAM_STATE_UNCONNECTED && old_state != PW_STREAM_STATE_UNCONNECTED))
        failed(video, error ? error : "The authorized capture stream disconnected.");
}
static void on_core_error(void *userdata, uint32_t id, int sequence, int result, const char *message)
{
    (void)sequence;
    (void)result;
    if (id == PW_ID_CORE)
        failed(userdata, message ? message : "The authorized PipeWire connection failed.");
}

static void on_param_changed(void *userdata, uint32_t id, const struct spa_pod *param)
{
    struct rgc_portal_video *video = userdata;
    if (id != SPA_PARAM_Format || !param)
        return;
    struct spa_video_info_raw info = {0};
    if (spa_format_video_raw_parse(param, &info) < 0 || !input_pixel_size(info.format) || !info.size.width ||
        !info.size.height || info.size.width > 32768 || info.size.height > 32768 ||
        (uint64_t)info.size.width * info.size.height * MAX(8u, input_pixel_size(info.format)) > FRAME_LIMIT) {
        failed(video, "The selected source video format or dimensions are unsupported.");
        return;
    }
    if (!video->hdr_requested && ((uint32_t)info.transfer_function == RGC_TRANSFER_PQ ||
                                  (uint32_t)info.transfer_function == RGC_TRANSFER_HLG)) {
        failed(video, "The source cannot provide the requested SDR encoding without HDR conversion.");
        return;
    }
    g_mutex_lock(&video->lock);
    video->format = info;
    g_mutex_unlock(&video->lock);
    uint8_t storage[1024];
    struct spa_pod_builder builder = SPA_POD_BUILDER_INIT(storage, sizeof(storage));
    const struct spa_pod *params[3];
    int stride = (int)info.size.width * (int)input_pixel_size(info.format);
    params[0] = spa_pod_builder_add_object(
        &builder, SPA_TYPE_OBJECT_ParamBuffers, SPA_PARAM_Buffers, SPA_PARAM_BUFFERS_buffers,
        SPA_POD_CHOICE_RANGE_Int(4, 2, 8), SPA_PARAM_BUFFERS_blocks, SPA_POD_Int(1), SPA_PARAM_BUFFERS_size,
        SPA_POD_Int((int)((uint64_t)stride * info.size.height)), SPA_PARAM_BUFFERS_stride,
        SPA_POD_Int(stride), SPA_PARAM_BUFFERS_dataType,
        SPA_POD_CHOICE_FLAGS_Int((1u << SPA_DATA_MemPtr) | (1u << SPA_DATA_MemFd)));
    params[1] = spa_pod_builder_add_object(&builder, SPA_TYPE_OBJECT_ParamMeta, SPA_PARAM_Meta,
                                           SPA_PARAM_META_type, SPA_POD_Id(SPA_META_Header),
                                           SPA_PARAM_META_size, SPA_POD_Int(sizeof(struct spa_meta_header)));
    uint32_t count = 2;
    if (video->metadata_cursor)
        params[count++] = spa_pod_builder_add_object(
            &builder, SPA_TYPE_OBJECT_ParamMeta, SPA_PARAM_Meta, SPA_PARAM_META_type,
            SPA_POD_Id(SPA_META_Cursor), SPA_PARAM_META_size,
            SPA_POD_CHOICE_RANGE_Int(sizeof(struct spa_meta_cursor) + sizeof(struct spa_meta_bitmap) +
                                         256 * 256 * 4,
                                     sizeof(struct spa_meta_cursor) + sizeof(struct spa_meta_bitmap),
                                     sizeof(struct spa_meta_cursor) + sizeof(struct spa_meta_bitmap) +
                                         CURSOR_LIMIT * CURSOR_LIMIT * 4));
    int result = pw_stream_update_params(video->stream, params, count);
    if (result < 0)
        failed(video, spa_strerror(result));
    else if (!rgc_portal_video_capabilities(video, 0))
        failed(video, "The capture output pipe closed.");
}
static const struct pw_stream_events stream_events = {
    PW_VERSION_STREAM_EVENTS, .state_changed = on_state_changed, .param_changed = on_param_changed,
    .process = on_process};
static const struct pw_core_events core_events = {PW_VERSION_CORE_EVENTS, .error = on_core_error};

struct rgc_portal_video *rgc_portal_video_new(int descriptor, const struct rgc_portal_source *source,
                                              struct rgc_frame_writer *writer, int fps, gboolean hdr,
                                              gboolean metadata_cursor, uint32_t granted_devices,
                                              gboolean can_request_pointer, GCancellable *cancel,
                                              GError **error)
{
    struct rgc_portal_video *video = g_new0(struct rgc_portal_video, 1);
    g_mutex_init(&video->lock);
    video->source = *source;
    video->writer = writer;
    video->fps = fps;
    video->hdr_requested = hdr;
    video->metadata_cursor = metadata_cursor;
    video->granted_devices = granted_devices;
    video->can_request_pointer = can_request_pointer;
    video->cancel = cancel;
    video->loop = pw_thread_loop_new("Ritsukage capture", NULL);
    if (!video->loop) {
        close(descriptor);
        goto failure;
    }
    video->context = pw_context_new(pw_thread_loop_get_loop(video->loop), NULL, 0);
    if (!video->context) {
        close(descriptor);
        goto failure;
    }
    video->core = pw_context_connect_fd(video->context, descriptor, NULL, 0);
    if (!video->core)
        goto failure;
    pw_core_add_listener(video->core, &video->core_listener, &core_events, video);
    char identity[32];
    g_snprintf(identity, sizeof(identity), "%" G_GUINT64_FORMAT,
               source->serial ? source->serial : source->node_id);
    video->stream =
        pw_stream_new(video->core, "Ritsukage capture",
                      pw_properties_new(PW_KEY_MEDIA_TYPE, "Video", PW_KEY_MEDIA_CATEGORY, "Capture",
                                        PW_KEY_MEDIA_ROLE, "Screen", PW_KEY_TARGET_OBJECT, identity, NULL));
    if (!video->stream)
        goto failure;
    pw_stream_add_listener(video->stream, &video->listener, &stream_events, video);
    uint8_t storage[8192];
    struct spa_pod_builder builder = SPA_POD_BUILDER_INIT(storage, sizeof(storage));
    enum spa_video_format high_formats[] = {SPA_VIDEO_FORMAT_RGBA_F16,   SPA_VIDEO_FORMAT_RGBA_F32,
                                            SPA_VIDEO_FORMAT_ABGR_210LE, SPA_VIDEO_FORMAT_xBGR_210LE,
                                            SPA_VIDEO_FORMAT_ARGB_210LE, SPA_VIDEO_FORMAT_xRGB_210LE,
                                            SPA_VIDEO_FORMAT_RGBA_102LE, SPA_VIDEO_FORMAT_RGBx_102LE,
                                            SPA_VIDEO_FORMAT_BGRA_102LE, SPA_VIDEO_FORMAT_BGRx_102LE};
    enum spa_video_format low_formats[] = {
        SPA_VIDEO_FORMAT_BGRA, SPA_VIDEO_FORMAT_BGRx, SPA_VIDEO_FORMAT_RGBA, SPA_VIDEO_FORMAT_RGBx,
        SPA_VIDEO_FORMAT_ARGB, SPA_VIDEO_FORMAT_ABGR, SPA_VIDEO_FORMAT_xRGB, SPA_VIDEO_FORMAT_xBGR};
    const struct spa_pod *params[G_N_ELEMENTS(high_formats) + G_N_ELEMENTS(low_formats)];
    uint32_t count = 0;
    for (unsigned pass = 0; pass < 2; pass++) {
        if (pass == 0 && !hdr)
            continue;
        enum spa_video_format *formats = pass == 0 ? high_formats : low_formats;
        unsigned n = pass == 0 ? G_N_ELEMENTS(high_formats) : G_N_ELEMENTS(low_formats);
        for (unsigned index = 0; index < n; index++) {
            if (hdr)
                params[count++] = spa_pod_builder_add_object(
                    &builder, SPA_TYPE_OBJECT_Format, SPA_PARAM_EnumFormat, SPA_FORMAT_mediaType,
                    SPA_POD_Id(SPA_MEDIA_TYPE_video), SPA_FORMAT_mediaSubtype,
                    SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw), SPA_FORMAT_VIDEO_format, SPA_POD_Id(formats[index]),
                    SPA_FORMAT_VIDEO_size,
                    SPA_POD_CHOICE_RANGE_Rectangle(&SPA_RECTANGLE(320, 240), &SPA_RECTANGLE(1, 1),
                                                   &SPA_RECTANGLE(32768, 32768)),
                    SPA_FORMAT_VIDEO_framerate,
                    SPA_POD_CHOICE_RANGE_Fraction(&SPA_FRACTION(fps, 1), &SPA_FRACTION(0, 1),
                                                  &SPA_FRACTION(fps, 1)));
            else
                params[count++] = spa_pod_builder_add_object(
                    &builder, SPA_TYPE_OBJECT_Format, SPA_PARAM_EnumFormat, SPA_FORMAT_mediaType,
                    SPA_POD_Id(SPA_MEDIA_TYPE_video), SPA_FORMAT_mediaSubtype,
                    SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw), SPA_FORMAT_VIDEO_format, SPA_POD_Id(formats[index]),
                    SPA_FORMAT_VIDEO_size,
                    SPA_POD_CHOICE_RANGE_Rectangle(&SPA_RECTANGLE(320, 240), &SPA_RECTANGLE(1, 1),
                                                   &SPA_RECTANGLE(32768, 32768)),
                    SPA_FORMAT_VIDEO_framerate,
                    SPA_POD_CHOICE_RANGE_Fraction(&SPA_FRACTION(fps, 1), &SPA_FRACTION(0, 1),
                                                  &SPA_FRACTION(fps, 1)),
                    SPA_FORMAT_VIDEO_transferFunction, SPA_POD_Id(SPA_VIDEO_TRANSFER_SRGB),
                    SPA_FORMAT_VIDEO_colorPrimaries, SPA_POD_Id(SPA_VIDEO_COLOR_PRIMARIES_BT709));
        }
    }
    int result = pw_stream_connect(video->stream, PW_DIRECTION_INPUT, source->node_id,
                                   PW_STREAM_FLAG_AUTOCONNECT | PW_STREAM_FLAG_MAP_BUFFERS |
                                       PW_STREAM_FLAG_DONT_RECONNECT,
                                   params, count);
    if (result < 0) {
        g_set_error(error, G_IO_ERROR, G_IO_ERROR_FAILED, "PipeWire stream connection failed: %s",
                    spa_strerror(result));
        goto failure;
    }
    result = pw_thread_loop_start(video->loop);
    if (result < 0) {
        g_set_error(error, G_IO_ERROR, G_IO_ERROR_FAILED, "PipeWire capture loop failed: %s",
                    spa_strerror(result));
        goto failure;
    }
    return video;
failure:
    if (error && !*error)
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_FAILED,
                            "The authorized PipeWire capture stream could not be initialized.");
    rgc_portal_video_free(video);
    return NULL;
}
void rgc_portal_video_free(struct rgc_portal_video *video)
{
    if (!video)
        return;
    if (video->loop)
        pw_thread_loop_stop(video->loop);
    if (video->stream)
        pw_stream_destroy(video->stream);
    if (video->core)
        pw_core_disconnect(video->core);
    if (video->context)
        pw_context_destroy(video->context);
    if (video->loop)
        pw_thread_loop_destroy(video->loop);
    g_clear_pointer(&video->cursor.pixels, g_bytes_unref);
    g_free(video->failure);
    g_mutex_clear(&video->lock);
    g_free(video);
}
gboolean rgc_portal_video_failed(struct rgc_portal_video *video, char **reason)
{
    g_mutex_lock(&video->lock);
    gboolean failed_now = video->failure != NULL;
    if (reason)
        *reason = g_strdup(video->failure);
    g_mutex_unlock(&video->lock);
    return failed_now;
}
gboolean rgc_portal_video_has_frame(struct rgc_portal_video *video)
{
    g_mutex_lock(&video->lock);
    gboolean available = video->has_frame;
    g_mutex_unlock(&video->lock);
    return available;
}
void rgc_portal_video_pointer(struct rgc_portal_video *video,
                              struct rgc_portal_pointer_observation *observation)
{
    g_mutex_lock(&video->lock);
    *observation = (struct rgc_portal_pointer_observation){.generation = video->source.generation,
                                                           .stream_id = video->source.node_id,
                                                           .width = (int)video->format.size.width,
                                                           .height = (int)video->format.size.height,
                                                           .cursor_x = video->cursor.x,
                                                           .cursor_y = video->cursor.y,
                                                           .cursor_valid = video->cursor.valid,
                                                           .observed_monotonic_us = video->cursor.observed};
    g_mutex_unlock(&video->lock);
}

gboolean rgc_portal_video_configure(struct rgc_portal_video *video, gint64 request_id, uint64_t generation,
                                    uint32_t stream_id, int x, int y, int width, int height, int output_width,
                                    int output_height)
{
    g_mutex_lock(&video->lock);
    gboolean valid = request_id > 0 && generation == video->source.generation &&
                     stream_id == video->source.node_id && x >= 0 && y >= 0 && width > 0 && height > 0 &&
                     (uint64_t)x + width <= video->format.size.width &&
                     (uint64_t)y + height <= video->format.size.height && output_width > 0 &&
                     output_height > 0 && output_width <= 32768 && output_height <= 32768 &&
                     (uint64_t)output_width * output_height * (video->hdr_requested ? 8u : 4u) <= FRAME_LIMIT;
    if (valid)
        video->configuration =
            (struct rgc_capture_configuration){request_id, x, y, width, height, output_width, output_height};
    g_mutex_unlock(&video->lock);
    return rgc_portal_video_reply(video, request_id, valid ? "configured" : "unavailable",
                                  valid ? NULL
                                        : "The capture configuration does not fit the authorized source.");
}

gboolean rgc_portal_video_set_active(struct rgc_portal_video *video, gint64 request_id, gboolean active)
{
    pw_thread_loop_lock(video->loop);
    int result = pw_stream_set_active(video->stream, active);
    if (result >= 0) {
        video->suspended = !active;
        if (active)
            video->last_frame = 0;
    }
    pw_thread_loop_unlock(video->loop);
    return rgc_portal_video_reply(video, request_id,
                                  result < 0 ? "failed"
                                  : active   ? "resumed"
                                             : "suspended",
                                  result < 0 ? spa_strerror(result) : NULL);
}
