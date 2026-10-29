package com.dolphindotnet.rider;

import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import javax.xml.stream.XMLInputFactory;
import javax.xml.stream.XMLStreamConstants;
import java.io.StringReader;

final class DolphinProject {
    private DolphinProject() { }
    static boolean isProject(Project project) {
        return com.intellij.openapi.application.ReadAction.compute(() -> isProjectUnderReadAction(project));
    }
    private static boolean isProjectUnderReadAction(Project project) {
        if (project.isDisposed() || project.getBasePath() == null) return false;
        var root = com.intellij.openapi.vfs.LocalFileSystem.getInstance().findFileByPath(project.getBasePath());
        if (root == null) return false;
        var found = new boolean[1];
        com.intellij.openapi.roots.ProjectFileIndex.getInstance(project).iterateContent(file -> {
            if (!file.isDirectory() && "csproj".equalsIgnoreCase(file.getExtension()) && contains(file, project)) found[0] = true;
            return !found[0];
        });
        return found[0];
    }
    static boolean contains(VirtualFile file, Project project) {
        for (var directory = file.getParent(); directory != null; directory = directory.getParent()) {
            boolean projectDirectory = false;
            for (var child : directory.getChildren()) {
                if (!child.isDirectory() && "csproj".equalsIgnoreCase(child.getExtension())) {
                    projectDirectory = true;
                    boolean usesSdk = project.getService(DolphinProjectCache.class).usesSdk(child);
                    if (usesSdk) return true;
                }
            }
            if (projectDirectory || directory.getPath().equals(project.getBasePath())) break;
        }
        return false;
    }
    static boolean usesSdk(String xml) {
        try {
            var factory = XMLInputFactory.newFactory();
            factory.setProperty(XMLInputFactory.SUPPORT_DTD, false);
            factory.setProperty(XMLInputFactory.IS_SUPPORTING_EXTERNAL_ENTITIES, false);
            var reader = factory.createXMLStreamReader(new StringReader(xml));
            try {
                while (reader.hasNext()) {
                    if (reader.next() != XMLStreamConstants.START_ELEMENT) continue;
                    var element = reader.getLocalName();
                    if (element.equals("PackageReference") || element.equals("ProjectReference")) {
                        var include = reader.getAttributeValue(null, "Include");
                        if (include != null) {
                            var name = include.replace('\\', '/');
                            name = name.substring(name.lastIndexOf('/') + 1);
                            if (name.equals("DolphinDotNet") || name.startsWith("DolphinDotNet.")) return true;
                        }
                    }
                    var sdk = reader.getAttributeValue(null, element.equals("Sdk") ? "Name" : "Sdk");
                    if ((element.equals("Project") || element.equals("Sdk")) && sdk != null) {
                        for (var item : sdk.split(";")) {
                            var name = item.trim();
                            if (name.equals("DolphinDotNet.Sdk") || name.startsWith("DolphinDotNet.Sdk/")) return true;
                        }
                    }
                    if (element.equals("Import")) {
                        var path = reader.getAttributeValue(null, "Project");
                        if (path != null) {
                            path = "/" + path.replace('\\', '/');
                            if (path.contains("/DolphinDotNet.Sdk/Sdk/")) return true;
                        }
                    }
                }
            } finally { reader.close(); }
        } catch (Exception ignored) { }
        return false;
    }
}
