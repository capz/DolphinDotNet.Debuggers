HOST_CC ?= cc
HOST_CFLAGS ?= -std=c11 -O1 -g -Wall -Wextra -Werror
PEER_CFLAGS ?= -std=c11 -O2 -g -Wall -Wextra -Werror
.PHONY: test
test: build/core_test build/stdio_receiver build/branding_test build/usb_transport_test
	./build/core_test
	./build/branding_test
	./build/usb_transport_test
	python3 tests/resident_test.py
build:
	mkdir -p build
build/core_test: tests/core_test.c elf.c protocol.c zlib_decode.c puff.c elf.h protocol.h resident.h | build
	$(HOST_CC) $(HOST_CFLAGS) -I. tests/core_test.c elf.c protocol.c zlib_decode.c puff.c -lz -o $@
build/stdio_receiver: tests/stdio_receiver.c elf.c protocol.c zlib_decode.c puff.c elf.h protocol.h resident.h | build
	$(HOST_CC) $(PEER_CFLAGS) -I. tests/stdio_receiver.c elf.c protocol.c zlib_decode.c puff.c -o $@
build/branding_test: tests/branding_test.c branding.c branding.h | build
	$(HOST_CC) $(HOST_CFLAGS) -I. tests/branding_test.c branding.c -o $@
build/usb_transport_test: tests/usb_transport_test.c usb_transport.c usb_transport.h tests/stubs/ogc/usbgecko.h | build
	$(HOST_CC) $(HOST_CFLAGS) -I. -Itests/stubs tests/usb_transport_test.c usb_transport.c -o $@
