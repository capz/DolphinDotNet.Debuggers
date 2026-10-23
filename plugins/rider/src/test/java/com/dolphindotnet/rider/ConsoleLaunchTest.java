package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.util.LinkedHashMap;
import java.util.Map;
import static org.junit.jupiter.api.Assertions.*;

class ConsoleLaunchTest {
    @Test void tcpAndUsbHaveExplicitReceiverModeAndBoundedReadiness() {
        for (var transport : new String[]{"tcp", "usbGecko"}) {
            var args=new LinkedHashMap<String,Object>(Map.of("transport",transport,"port",2166));
            ConsoleLaunch.apply(args,false,4299,30,60,1000);assertFalse(args.containsKey("launcher"));
            assertThrows(IllegalArgumentException.class,()->ConsoleLaunch.apply(args,true,0,30,60,1000));
            assertThrows(IllegalArgumentException.class,()->ConsoleLaunch.apply(args,true,4299,121,60,1000));
            assertThrows(IllegalArgumentException.class,()->ConsoleLaunch.apply(args,true,4299,30,61,1000));
            ConsoleLaunch.apply(args,true,4299,30,60,1000);
            assertEquals("launch",args.get("request"));assertEquals("dolphinDotNet",args.get("launcher"));
            assertEquals(4299,args.get("launcherPort"));assertEquals(30,args.get("readyTimeoutSeconds"));
            assertThrows(IllegalArgumentException.class,()->ConsoleLaunch.apply(args,true,4299,30,60,1000));
        }
        var tcp=new LinkedHashMap<String,Object>(Map.of("transport","tcp","port",4299));
        assertThrows(IllegalArgumentException.class,()->ConsoleLaunch.apply(tcp,true,4299,30,60,1000));
    }
}
