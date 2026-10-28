#ifndef DND_LAUNCHER_USB_TRANSPORT_H
#define DND_LAUNCHER_USB_TRANSPORT_H
#include <stdbool.h>
#include <stddef.h>

/* One bounded FIFO poll; returns a short byte count, zero when idle, or error.
 * Only memory-card slots A/B (channels 0/1) and nonnull buffers are accepted.
 * The caller owns the session deadline and yields after an idle poll. */
int dnd_launcher_usb_poll(int channel, void *buffer, size_t size, bool sending);
#endif
