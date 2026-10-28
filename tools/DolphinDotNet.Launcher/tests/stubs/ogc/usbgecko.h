/* Host-test declarations of the libogc2 FIFO API. */
#ifndef DND_TEST_USBGECKO_H
#define DND_TEST_USBGECKO_H
int usb_recvbuffer_safe_ex(int channel, void *buffer, int size, int retries);
int usb_sendbuffer_safe_ex(int channel, const void *buffer, int size, int retries);
#endif
