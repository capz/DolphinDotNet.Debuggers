package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.nio.file.Path;
import static org.junit.jupiter.api.Assertions.*;

class LaunchPlanTest {
    @Test void emulatorAlwaysDisablesTheConsoleStubRegardlessOfTheFirstTarget() {
        var target = new TargetProfile(); target.transport = "usbGecko"; target.serialPort = "COM7";
        for (var mode : new String[]{"play", "emulator"}) {
            var args = LaunchPlan.build(Path.of("a sample", "Snake.csproj"), mode, target);
            assertTrue(args.contains("-p:DndGdbTransport=Off"));
            assertTrue(args.contains("-p:DndBuildNative=true"));
            assertFalse(args.stream().anyMatch(a -> a.contains("COM7") || a.contains("GdbChannel")));
            assertEquals(Path.of("a sample", "Snake.csproj").toString(), args.get(1));
        }
    }
    @Test void hardwareBuildMatchesBothGeckoSlotsAndTheTcpEndpoint() {
        var target = new TargetProfile(); target.transport = "usbGecko"; target.serialPort = "COM7";
        assertTrue(LaunchPlan.build(Path.of("Snake.csproj"), "target", target).contains("-p:DndGdbChannel=1"));
        target.expectedSlot = "A";
        assertTrue(LaunchPlan.build(Path.of("Snake.csproj"), "target", target).contains("-p:DndGdbChannel=0"));
        target.transport = "tcp"; target.host = "192.168.1.20"; target.port = 3456;
        var args = LaunchPlan.build(Path.of("Snake.csproj"), "target", target);
        assertTrue(args.contains("-p:DndGdbIp=192.168.1.20")); assertTrue(args.contains("-p:DndGdbPort=3456"));
        assertThrows(IllegalArgumentException.class, () -> LaunchPlan.build(Path.of("Snake.csproj"), "target", null));
    }
}
