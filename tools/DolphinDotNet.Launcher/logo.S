#include "branding.h"

.section .rodata.dnd_logo,"a",@progbits
.balign 32
.global dnd_logo_pixels
.type dnd_logo_pixels,@object
dnd_logo_pixels:
    .incbin "assets/logo.yuyv"
.size dnd_logo_pixels,.-dnd_logo_pixels
.if (.-dnd_logo_pixels) != DND_LOGO_BYTES
.error "logo.yuyv must contain exactly 300x200 YUV422 pixels"
.endif
