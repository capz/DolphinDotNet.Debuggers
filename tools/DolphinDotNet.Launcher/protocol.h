#ifndef DND_LAUNCHER_PROTOCOL_H
#define DND_LAUNCHER_PROTOCOL_H
#include "elf.h"

enum { DND_HELLO=1, DND_READY=2, DND_UPLOAD=3, DND_ACCEPTED=4, DND_EXECUTE=5, DND_EXECUTING=6, DND_ERROR=7, DND_CANCEL=8, DND_COMMIT=9, DND_UPLOAD_ZLIB=10 };
enum { DND_BAD_REQUEST=1, DND_TOO_LARGE=2, DND_BAD_CRC=3, DND_BAD_ELF=4, DND_NO_MEMORY=5, DND_BAD_COMPRESSION=6 };
enum { DND_STATUS_REJECTED=256 }; /* Local status callback; never a wire frame. */
typedef struct {
    void *context;
    /* Return a positive short count, or <=0 on EOF/error/deadline. */
    int (*read)(void *, void *, size_t);
    int (*write)(void *, const void *, size_t);
    uint8_t *(*allocate)(void *, size_t, uintptr_t *, size_t *);
    void (*status)(void *, unsigned);
    size_t maximum_image;
    /* Monotonic bytes actually received; starts at zero, completes only on full input. */
    void (*progress)(void *, uint32_t received, uint32_t total);
    /* On USB only, scan stale bytes for a complete HELLO before replying. */
    bool resynchronize_hello;
} DndLauncherIo;

/* 1: validated, EXECUTING acknowledged and host COMMIT received; chainload.
 * 0: probe cancelled; -1: failed/rejected. No execution on either path.
 * The caller owns the staged image; the returned plan contains no pointers. */
int dnd_launcher_session(DndLauncherIo *io, uint8_t **image, DndElfPlan *plan);
#endif
