package com.dolphindotnet.rider;

import java.util.concurrent.Semaphore;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/** Application-wide ownership: a readiness check must release its port before a debug upload. */
final class TargetConnectionGate {
    private static final Semaphore CONNECTION = new Semaphore(1, true);
    private static final java.util.concurrent.atomic.AtomicInteger WAITERS = new java.util.concurrent.atomic.AtomicInteger();
    static Lease tryProbe() {
        return WAITERS.get() == 0 && CONNECTION.tryAcquire() ? new Lease() : null;
    }
    static Lease acquire(Runnable checkCanceled) throws InterruptedException {
        WAITERS.incrementAndGet();
        try {
            while (!CONNECTION.tryAcquire(100, TimeUnit.MILLISECONDS)) checkCanceled.run();
            return new Lease();
        } finally { WAITERS.decrementAndGet(); }
    }
    static final class Lease implements AutoCloseable {
        private final AtomicBoolean closed = new AtomicBoolean();
        private java.util.concurrent.CompletableFuture<Process> ownerExit;
        void holdUntilExit(Process process) { ownerExit = process.onExit(); }
        @Override public void close() {
            if (!closed.compareAndSet(false, true)) return;
            if (ownerExit == null) CONNECTION.release();
            else ownerExit.whenComplete((process, error) -> CONNECTION.release());
        }
    }
}
