package com.dolphindotnet.rider;

import java.util.Map;

/** Opt-in upload settings; old configurations continue to attach to running targets. */
final class SwissLaunch {
    static void apply(Map<String, Object> args, boolean enabled, int timeout, int delay) {
        if (!enabled) return;
        if (!"usbGecko".equals(args.get("transport"))) throw new IllegalArgumentException("Swiss upload requires a USB Gecko target.");
        if (timeout < 1 || timeout > 120) throw new IllegalArgumentException("Upload timeout must be between 1 and 120 seconds.");
        if (delay < 0 || delay > 30000) throw new IllegalArgumentException("Boot delay must be between 0 and 30000 milliseconds.");
        args.put("request", "launch"); args.put("uploadTimeoutSeconds", timeout); args.put("bootDelayMilliseconds", delay);
    }
}
