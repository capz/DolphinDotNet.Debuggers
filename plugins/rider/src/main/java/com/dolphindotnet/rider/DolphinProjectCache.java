package com.dolphindotnet.rider;

import com.intellij.openapi.components.Service;
import com.intellij.openapi.vfs.*;
import java.io.IOException;
import java.util.concurrent.ConcurrentHashMap;

/** Project-owned metadata; never store CachedValue instances on application-wide VFS files. */
@Service(Service.Level.PROJECT)
public final class DolphinProjectCache {
    private record Entry(long stamp, boolean usesSdk) { }
    private final ConcurrentHashMap<String, Entry> files = new ConcurrentHashMap<>();
    boolean usesSdk(VirtualFile file) {
        var stamp = file.getModificationStamp();
        var existing = files.get(file.getPath());
        if (existing != null && existing.stamp == stamp) return existing.usesSdk;
        boolean found = false;
        try { found = DolphinProject.usesSdk(VfsUtilCore.loadText(file)); } catch (IOException ignored) { }
        files.put(file.getPath(), new Entry(stamp, found));
        return found;
    }
}
