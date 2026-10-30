#include "zlib_decode.h"
#include "puff.h"
#include <stddef.h>
#include <stdint.h>

/* Bounded RFC 1950 wrapper around the standalone RFC 1951 puff decoder.
   Validate the zlib header, exact output length, consumed input and Adler-32. */
int dnd_zlib_decode(uint8_t *output, size_t output_size, const uint8_t *input, size_t input_size) {
    if (!output || !input || input_size < 6 || output_size > 0xffffffffu) return -1;
    unsigned cmf=input[0], flg=input[1];
    if ((cmf & 15u)!=8 || (cmf>>4)>7 || ((cmf<<8)|flg)%31u!=0 || (flg&0x20u)) return -1;
    unsigned long written=(unsigned long)output_size;
    unsigned long consumed=(unsigned long)(input_size-6);
    if (puff(output,&written,input+2,&consumed)!=0 ||
        written!=output_size || consumed!=input_size-6) return -1;
    uint32_t a=1,b=0;
    for (size_t i=0;i<output_size;i++) {
        a=(a+output[i])%65521u;
        b=(b+a)%65521u;
    }
    uint32_t expected=((uint32_t)input[input_size-4]<<24) |
                      ((uint32_t)input[input_size-3]<<16) |
                      ((uint32_t)input[input_size-2]<<8) | input[input_size-1];
    return ((b<<16)|a)==expected ? 0 : -1;
}
