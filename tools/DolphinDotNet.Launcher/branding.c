#include "branding.h"
#include <string.h>

unsigned dnd_launcher_logo_footer(size_t width, size_t height) {
    if (width < DND_LOGO_WIDTH + 2 * DND_LOGO_MARGIN ||
        height < DND_LOGO_HEIGHT + 3 * DND_LOGO_MARGIN + 16 || (width & 1)) return 0;
    return DND_LOGO_HEIGHT + DND_LOGO_MARGIN;
}

bool dnd_launcher_draw_logo(void *framebuffer, size_t width, size_t height, size_t stride) {
    if (!framebuffer || !dnd_launcher_logo_footer(width,height) ||
        width > SIZE_MAX / 2 || stride < width * 2 || stride > SIZE_MAX / height) return false;
    /* XFB chroma is shared by horizontal pixel pairs, so start on an even x. */
    size_t x=(width-DND_LOGO_MARGIN-DND_LOGO_WIDTH) & ~(size_t)1;
    size_t y=height-DND_LOGO_MARGIN-DND_LOGO_HEIGHT;
    for (unsigned row=0; row<DND_LOGO_HEIGHT; row++)
        memcpy((uint8_t *)framebuffer + (y+row)*stride + x*2,
               dnd_logo_pixels + row*DND_LOGO_WIDTH*2, DND_LOGO_WIDTH*2);
    return true;
}
