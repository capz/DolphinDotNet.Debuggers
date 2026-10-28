OUTPUT_FORMAT("elf32-powerpc")
OUTPUT_ARCH(powerpc:common)
ENTRY(dnd_launcher_boot)
/* Exclude ELF headers from PT_LOAD: loading them at 0x80000000 destroys the
 * console's boot parameters before the receiver CRT can initialize. */
PHDRS {
    boot PT_LOAD FLAGS(5);
}
SECTIONS {
    . = 0x80003100;
    .text : {
        *(.text.boot)
        *(.rodata.resident)
        . = ALIGN(32);
    } :boot
    /DISCARD/ : { *(.comment) *(.note*) }
}
