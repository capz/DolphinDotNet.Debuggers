#include "branding.h"
#include <string.h>

unsigned dnd_launcher_logo_footer(size_t width, size_t height) {
    if (width < DND_LOGO_WIDTH + 2 * DND_LOGO_MARGIN ||
        height < DND_LOGO_HEIGHT + DND_LOGO_MARGIN + 2 * DND_CONSOLE_MARGIN + 16 || (width & 1)) return 0;
    return DND_LOGO_HEIGHT + DND_LOGO_MARGIN;
}

static bool drawable_surface(void *framebuffer, size_t width, size_t height, size_t stride) {
    if (!framebuffer || !dnd_launcher_logo_footer(width,height)) return false;
    return width<=SIZE_MAX/2 && stride>=width*2 && stride<=SIZE_MAX/height;
}

bool dnd_launcher_draw_logo(void *framebuffer, size_t width, size_t height, size_t stride) {
    if (!drawable_surface(framebuffer,width,height,stride)) return false;
    /* XFB chroma is shared by horizontal pixel pairs, so start on an even x. */
    size_t x=(width-DND_LOGO_MARGIN-DND_LOGO_WIDTH) & ~(size_t)1;
    size_t y=height-DND_LOGO_MARGIN-DND_LOGO_HEIGHT;
    for (unsigned row=0; row<DND_LOGO_HEIGHT; row++)
        memcpy((uint8_t *)framebuffer + (y+row)*stride + x*2,
               dnd_logo_pixels + row*DND_LOGO_WIDTH*2, DND_LOGO_WIDTH*2);
    return true;
}

bool dnd_launcher_draw_progress(void *framebuffer, size_t width, size_t height,
                                size_t stride, uint32_t received, uint32_t total) {
    if (!total || !drawable_surface(framebuffer,width,height,stride) ||
        width < DND_LOGO_WIDTH + 3 * DND_LOGO_MARGIN + 64) return false;
    size_t bar_width=(width-DND_LOGO_WIDTH-3*DND_LOGO_MARGIN) & ~(size_t)1;
    size_t inner_width=bar_width-4;
    uint32_t count=received<total ? received : total;
    /* Divide first so scaling cannot overflow even on a 64-bit host surface. */
    size_t filled=(inner_width/total)*count +
        (size_t)((uint64_t)(inner_width%total)*count/total);
    size_t top=height-DND_LOGO_MARGIN-DND_PROGRESS_HEIGHT;
    /* Monochrome YUV422 pairs match the logo: white fill, grey border, dark track. */
    for (size_t row=0; row<DND_PROGRESS_HEIGHT; row++) {
        uint8_t *destination=(uint8_t *)framebuffer+(top+row)*stride+DND_LOGO_MARGIN*2;
        for (size_t column=0; column<bar_width; column+=2) {
            bool border=row<2 || row>=DND_PROGRESS_HEIGHT-2 || column<2 || column>=bar_width-2;
            uint8_t luma=border ? 160 : (column-2<filled ? 235 : 40);
            destination[0]=luma; destination[1]=128;
            destination[2]=luma; destination[3]=128;
            destination+=4;
        }
    }
    return true;
}
