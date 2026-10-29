package com.dolphindotnet.rider;

import java.nio.file.Path;
import java.util.*;

/** Pure command planning; the selected target is snapshotted before building. */
final class LaunchPlan {
    static List<String> build(Path project, String mode, TargetProfile target) {
        var args = new ArrayList<>(List.of("build", project.toString(), "--configuration", "Debug", "--nologo", "-p:DndBuildNative=true", "-p:DndDebugSymbols=true"));
        if (mode.equals("target")) {
            if (target == null) throw new IllegalArgumentException("No target configured.");
            target.validate();
            if (target.transport.equals("usbGecko")) {
                args.add("-p:DndGdbTransport=UsbGecko"); args.add("-p:DndGdbChannel=" + (target.expectedSlot.equals("A") ? 0 : 1));
            } else {
                // libogc's native network stub needs a literal console IPv4
                // address. Reject MSBuild separators as well as DNS names.
                var parts = target.host.trim().split("\\.", -1);
                if (parts.length != 4 || Arrays.stream(parts).anyMatch(p -> !p.matches("[0-9]{1,3}") || Integer.parseInt(p) > 255))
                    throw new IllegalArgumentException("Hardware debugging needs the console's IPv4 address.");
                args.add("-p:DndGdbTransport=Tcp"); args.add("-p:DndGdbIp=" + target.host.trim()); args.add("-p:DndGdbPort=" + target.port);
            }
        } else if (mode.equals("emulator") || mode.equals("play")) args.add("-p:DndGdbTransport=Off");
        else throw new IllegalArgumentException("Unknown launch mode.");
        return args;
    }
}
