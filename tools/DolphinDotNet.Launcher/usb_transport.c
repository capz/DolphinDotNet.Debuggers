#include "usb_transport.h"
#include <ogc/usbgecko.h>

#define USB_MAX_BATCH 3968
#define USB_POLL_ATTEMPTS 4096

int dnd_launcher_usb_poll(int channel, void *buffer, size_t size, bool sending) {
    if (!size) return 0;
    if (!buffer || channel<0 || channel>1) return -1;
    int count=size>USB_MAX_BATCH ? USB_MAX_BATCH : (int)size;
    /* libogc2 decrements retries on EVERY loop, including successful bytes.
     * Passing 1 therefore transfers at most one byte, not one full buffer.
     * Batch ready bytes under one EXI lock, while keeping empty FIFO polls finite. */
    return sending ? usb_sendbuffer_safe_ex(channel,buffer,count,USB_POLL_ATTEMPTS)
                   : usb_recvbuffer_safe_ex(channel,buffer,count,USB_POLL_ATTEMPTS);
}
