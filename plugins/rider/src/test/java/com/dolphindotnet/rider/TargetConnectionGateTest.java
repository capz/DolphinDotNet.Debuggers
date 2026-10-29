package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.util.concurrent.*;
import static org.junit.jupiter.api.Assertions.*;

class TargetConnectionGateTest {
    public static final class WaitingChild {
        public static void main(String[] args) throws Exception { System.in.read(); }
    }
    @Test void readinessLeaseOutlivesItsCallbackUntilTheOwningProcessExits() throws Exception {
        var javaExecutable = java.nio.file.Path.of(System.getProperty("java.home"), "bin", "java").toString();
        var childClasses = System.getProperty("dnd.testClasses");
        var child = new ProcessBuilder(javaExecutable, "-cp", childClasses, WaitingChild.class.getName()).start();
        var lease = TargetConnectionGate.tryProbe();
        try {
            assertNotNull(lease); lease.holdUntilExit(child); lease.close();
            assertNull(TargetConnectionGate.tryProbe());
            child.getOutputStream().close(); assertTrue(child.waitFor(5, TimeUnit.SECONDS));
            // onExit completion runs asynchronously after waitFor returns.
            var executor = Executors.newSingleThreadExecutor();
            try (var released = executor.submit(() -> TargetConnectionGate.acquire(() -> {})).get(3, TimeUnit.SECONDS)) {
                assertNotNull(released);
            } finally { executor.shutdownNow(); }
        } finally { child.destroyForcibly(); if (lease != null) lease.close(); }
    }
    @Test void debugWaitsForProbeToReleaseAndSuppressesOtherProbesForItsLifetime() throws Exception {
        var probe = TargetConnectionGate.tryProbe(); assertNotNull(probe);
        var executor = Executors.newSingleThreadExecutor();
        try {
            var debug = executor.submit(() -> TargetConnectionGate.acquire(() -> {}));
            assertThrows(TimeoutException.class, () -> debug.get(100, TimeUnit.MILLISECONDS));
            assertNull(TargetConnectionGate.tryProbe());
            probe.close();
            try (var lease = debug.get(2, TimeUnit.SECONDS)) { assertNull(TargetConnectionGate.tryProbe()); }
            try (var after = TargetConnectionGate.tryProbe()) { assertNotNull(after); }
            probe.close(); // Idempotent cleanup must not increase the semaphore capacity.
            try (var after = TargetConnectionGate.tryProbe()) { assertNotNull(after); assertNull(TargetConnectionGate.tryProbe()); }
        } finally { probe.close(); executor.shutdownNow(); }
    }
}
