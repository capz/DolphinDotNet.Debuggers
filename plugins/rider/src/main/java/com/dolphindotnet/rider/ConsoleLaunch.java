package com.dolphindotnet.rider;

import java.util.Map;

/** Native receiver mode; TCP's upload port is independent of the application's GDB port. */
final class ConsoleLaunch {
    static void apply(Map<String, Object> args, boolean enabled, int port, int ready, int upload, int delay) {
        if (!enabled) return;
        if (args.containsKey("request")) throw new IllegalArgumentException("Choose one launcher mode.");
        if (port < 1 || port > 65535) throw new IllegalArgumentException("Launcher upload port must be between 1 and 65535.");
        if (ready < 1 || ready > 120) throw new IllegalArgumentException("Readiness wait must be between 1 and 120 seconds.");
        if (upload < 1 || upload > 60) throw new IllegalArgumentException("Launcher upload timeout must be between 1 and 60 seconds.");
        if (delay < 0 || delay > 30000) throw new IllegalArgumentException("Boot delay must be between 0 and 30000 milliseconds.");
        if ("tcp".equals(args.get("transport")) && Integer.valueOf(port).equals(args.get("port")))
            throw new IllegalArgumentException("Launcher upload port must differ from the application's GDB port.");
        args.put("request", "launch"); args.put("launcher", "dolphinDotNet");
        args.put("launcherPort", port); args.put("readyTimeoutSeconds", ready);
        args.put("uploadTimeoutSeconds", upload); args.put("bootDelayMilliseconds", delay);
    }
}
