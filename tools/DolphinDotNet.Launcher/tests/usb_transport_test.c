/* Model libogc2 retry semantics, including decrement after successful bytes. */
#include "usb_transport.h"
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

static uint8_t fifo[64*1024+23], output[sizeof(fifo)];
static size_t position;
static int calls, short_limit, idle, failure;

static int poll(void *buffer,int size,int retries,bool sending) {
    assert(size>0 && size<=3968);
    assert(retries>0 && retries<=4096); /* No unbounded busy wait. */
    calls++;
    if (failure) return -1;
    if (idle) return 0;
    int n=size<retries ? size : retries;
    if (short_limit && n>short_limit) n=short_limit;
    if ((size_t)n>sizeof(fifo)-position) n=(int)(sizeof(fifo)-position);
    if (sending) memcpy(output+position,buffer,(size_t)n);
    else memcpy(buffer,fifo+position,(size_t)n);
    position+=(size_t)n;
    return n;
}
int usb_recvbuffer_safe_ex(int channel,void *buffer,int size,int retries) {
    assert(channel==1); return poll(buffer,size,retries,false);
}
int usb_sendbuffer_safe_ex(int channel,const void *buffer,int size,int retries) {
    assert(channel==1); return poll((void *)buffer,size,retries,true);
}
int main(void) {
    uint8_t buffer[sizeof(fifo)];
    for (size_t i=0;i<sizeof(fifo);i++) fifo[i]=(uint8_t)(i*31u+7u);
    size_t received=0;
    while (received<sizeof(buffer)) {
        int n=dnd_launcher_usb_poll(1,buffer+received,sizeof(buffer)-received,false);
        assert(n>0); received+=(size_t)n;
    }
    assert(memcmp(buffer,fifo,sizeof(fifo))==0);
    assert(calls<20); /* A one-byte poll would require over 65,000 EXI locks. */
    position=0; calls=0;
    size_t sent=0;
    while (sent<sizeof(buffer)) {
        int n=dnd_launcher_usb_poll(1,buffer+sent,sizeof(buffer)-sent,true);
        assert(n>0); sent+=(size_t)n;
    }
    assert(memcmp(output,fifo,sizeof(fifo))==0 && calls<20);
    position=0; calls=0; short_limit=7;
    assert(dnd_launcher_usb_poll(1,buffer,16,false)==7);
    assert(dnd_launcher_usb_poll(1,buffer+7,9,false)==7);
    assert(dnd_launcher_usb_poll(1,buffer+14,2,false)==2);
    assert(memcmp(buffer,fifo,16)==0);
    idle=1;
    assert(dnd_launcher_usb_poll(1,buffer,16,false)==0);
    assert(dnd_launcher_usb_poll(1,buffer,16,true)==0);
    int previous=calls;
    assert(dnd_launcher_usb_poll(1,buffer,0,false)==0 && calls==previous);
    assert(dnd_launcher_usb_poll(1,NULL,16,false)==-1 && calls==previous);
    assert(dnd_launcher_usb_poll(-1,buffer,16,false)==-1 && calls==previous);
    assert(dnd_launcher_usb_poll(2,buffer,16,true)==-1 && calls==previous);
    failure=1;
    assert(dnd_launcher_usb_poll(1,buffer,16,false)==-1);
    assert(dnd_launcher_usb_poll(1,buffer,16,true)==-1);
    puts("USB FIFO batching: bulk read/write, bounded idle polls and short I/O passed.");
    return 0;
}
