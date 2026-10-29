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
                args.add("-p:DndGdbTransport=Tcp"); args.add("-p:DndGdbIp=" + target.host.trim()); args.add("-p:DndGdbPort=" + target.port);
            }
        } else if (mode.equals("emulator") || mode.equals("play")) args.add("-p:DndGdbTransport=Off");
        else throw new IllegalArgumentException("Unknown launch mode.");
        return args;
    }
}
