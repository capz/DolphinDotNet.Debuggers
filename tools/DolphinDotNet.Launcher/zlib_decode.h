#ifndef DND_ZLIB_DECODE_H
#define DND_ZLIB_DECODE_H
#include <stddef.h>
#include <stdint.h>
int dnd_zlib_decode(uint8_t *output, size_t output_size, const uint8_t *input, size_t input_size);
#endif
