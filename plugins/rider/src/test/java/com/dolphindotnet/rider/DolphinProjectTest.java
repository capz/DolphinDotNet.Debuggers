package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.nio.file.Files;
import java.nio.file.Path;
import static org.junit.jupiter.api.Assertions.*;

class DolphinProjectTest {
    @Test void recognizesSdkAttributesAndVersionedSdkElements() {
        assertTrue(DolphinProject.usesSdk("<Project Sdk='DolphinDotNet.Sdk'/>"));
        assertTrue(DolphinProject.usesSdk("<Project Sdk='Other; DolphinDotNet.Sdk/1.0'/>"));
        assertTrue(DolphinProject.usesSdk("<Project><Sdk Name='DolphinDotNet.Sdk' Version='1.0'/></Project>"));
    }
    @Test void recognizesRepositoryImportsOnBothPlatforms() {
        assertTrue(DolphinProject.usesSdk("<Project><Import Project='../../sdk/DolphinDotNet.Sdk/Sdk/Sdk.props'/></Project>"));
        assertTrue(DolphinProject.usesSdk("<Project><Import Project='..\\..\\sdk\\DolphinDotNet.Sdk\\Sdk\\Sdk.targets'/></Project>"));
    }
    @Test void recognizesConsumerFixturesButNotDesktopLibraries() throws Exception {
        var root = Path.of(System.getProperty("dnd.repositoryRoot")).resolve("tests/projects");
        for (var name : new String[]{"source-import.xml", "nuget-sdk.xml"})
            assertTrue(DolphinProject.usesSdk(Files.readString(root.resolve(name))), name);
        assertFalse(DolphinProject.usesSdk(Files.readString(root.resolve("desktop.xml"))));
    }
    @Test void leavesRegularDotnetProjectsAndCommentsAlone() {
        assertFalse(DolphinProject.usesSdk("<Project Sdk='Microsoft.NET.Sdk'/>"));
        assertFalse(DolphinProject.usesSdk("<Project><!-- <Sdk Name='DolphinDotNet.Sdk'/> --></Project>"));
        assertFalse(DolphinProject.usesSdk("<Project><PropertyGroup><Note>DolphinDotNet.Sdk</Note></PropertyGroup></Project>"));
        assertFalse(DolphinProject.usesSdk("not XML"));
    }
}
