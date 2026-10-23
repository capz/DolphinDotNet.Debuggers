package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.util.LinkedHashMap;
import java.util.Map;
import static org.junit.jupiter.api.Assertions.*;

class SwissLaunchTest {
    @Test void optInPreservesAttachAndRequiresUsbTarget() {
        var args = new LinkedHashMap<String, Object>(Map.of("transport", "tcp"));
        SwissLaunch.apply(args, false, 60, 1000);
        assertFalse(args.containsKey("request"));
        assertThrows(IllegalArgumentException.class, () -> SwissLaunch.apply(args, true, 60, 1000));
        args.put("transport", "usbGecko");
        assertThrows(IllegalArgumentException.class, () -> SwissLaunch.apply(args, true, 0, 1000));
        assertThrows(IllegalArgumentException.class, () -> SwissLaunch.apply(args, true, 121, 1000));
        assertThrows(IllegalArgumentException.class, () -> SwissLaunch.apply(args, true, 60, -1));
        assertThrows(IllegalArgumentException.class, () -> SwissLaunch.apply(args, true, 60, 30001));
        SwissLaunch.apply(args, true, 90, 2500);
        assertEquals("launch", args.get("request"));
        assertEquals(90, args.get("uploadTimeoutSeconds"));
        assertEquals(2500, args.get("bootDelayMilliseconds"));
    }
}
