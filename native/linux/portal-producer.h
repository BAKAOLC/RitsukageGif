#ifndef RGC_PORTAL_PRODUCER_H
#define RGC_PORTAL_PRODUCER_H
#include <gio/gio.h>
#include <stdint.h>

uint32_t rgc_portal_kwin_producer(uint32_t server_cookie, uint32_t node_id, uint64_t node_serial,
                                 GCancellable *cancel);
#endif
