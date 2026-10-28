#ifndef DND_LAUNCHER_BRANDING_H
#define DND_LAUNCHER_BRANDING_H

#define DND_LOGO_WIDTH 300
#define DND_LOGO_HEIGHT 200
#define DND_LOGO_MARGIN 20
#define DND_LOGO_BYTES (DND_LOGO_WIDTH * DND_LOGO_HEIGHT * 2)

#ifndef __ASSEMBLER__
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

extern const uint8_t dnd_logo_pixels[DND_LOGO_BYTES];

/* The console excludes this footer so scrolling cannot overwrite the logo. */
unsigned dnd_launcher_logo_footer(size_t width, size_t height);
bool dnd_launcher_draw_logo(void *framebuffer, size_t width, size_t height, size_t stride);
#endif
#endif
