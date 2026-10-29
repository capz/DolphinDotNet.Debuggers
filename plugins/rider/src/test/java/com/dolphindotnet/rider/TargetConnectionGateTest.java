package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.util.concurrent.*;
import static org.junit.jupiter.api.Assertions.*;

class TargetConnectionGateTest {
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
