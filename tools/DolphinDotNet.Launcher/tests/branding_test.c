#include "branding.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Distinct adjacent luma values verify byte order and source positioning. */
const uint8_t dnd_logo_pixels[DND_LOGO_BYTES]={16,128,235,128};

static void check_frame(size_t height) {
    const size_t width=640, stride=width*2+32, guard=32;
    size_t bytes=height*stride;
    uint8_t *storage=malloc(bytes+2*guard);
    assert(storage);
    memset(storage,0x5a,bytes+2*guard);
    uint8_t *frame=storage+guard;
    assert(dnd_launcher_logo_footer(width,height)==DND_LOGO_HEIGHT+DND_LOGO_MARGIN);
    assert(dnd_launcher_draw_logo(frame,width,height,stride));
    size_t left=(width-DND_LOGO_MARGIN-DND_LOGO_WIDTH)*2;
    size_t top=height-DND_LOGO_MARGIN-DND_LOGO_HEIGHT;
    for (size_t row=0; row<height; row++)
        for (size_t column=0; column<stride; column++) {
            uint8_t expected=0x5a;
            if (row>=top && row<top+DND_LOGO_HEIGHT &&
                column>=left && column<left+DND_LOGO_WIDTH*2)
                expected=dnd_logo_pixels[(row-top)*DND_LOGO_WIDTH*2+column-left];
            assert(frame[row*stride+column]==expected);
        }
    for (size_t i=0; i<guard; i++)
        assert(storage[i]==0x5a && storage[guard+bytes+i]==0x5a);
    free(storage);
}

int main(void) {
    check_frame(480);
    check_frame(528);
    uint8_t byte=0x5a;
    assert(!dnd_launcher_draw_logo(NULL,640,480,1280));
    assert(!dnd_launcher_draw_logo(&byte,320,480,1280));
    assert(!dnd_launcher_draw_logo(&byte,640,240,1280));
    assert(!dnd_launcher_draw_logo(&byte,641,480,1282));
    assert(!dnd_launcher_draw_logo(&byte,640,480,1279));
    assert(!dnd_launcher_draw_logo(&byte,640,480,SIZE_MAX));
    assert(!dnd_launcher_draw_logo(&byte,SIZE_MAX-1,480,SIZE_MAX));
    assert(byte==0x5a);
    puts("Logo placement: NTSC/PAL footer, padded stride, untouched text/borders and invalid surfaces passed.");
    return 0;
}
