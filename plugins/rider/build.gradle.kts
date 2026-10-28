plugins {
    java
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

version = "0.3.1"
group = "com.dolphindotnet"
repositories {
    mavenCentral()
    intellijPlatform { defaultRepositories() }
}
dependencies {
    intellijPlatform {
        // Pin the SDK. Interactive Rider acceptance is a separate release gate.
        if (providers.gradleProperty("riderSdk").isPresent) local(providers.gradleProperty("riderSdk").get())
        else rider("2026.2.3.1")
        bundledModule("intellij.platform.debugger")
        pluginVerifier()
    }
    implementation("org.eclipse.lsp4j:org.eclipse.lsp4j.debug:0.24.0")
    testImplementation("org.junit.jupiter:junit-jupiter:5.11.4")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}
java { toolchain { languageVersion = JavaLanguageVersion.of(25) } }
intellijPlatform {
    pluginConfiguration {
        name = "DolphinDotNet"
        ideaVersion { sinceBuild = "262"; untilBuild = "262.*" }
    }
    pluginVerification { ides { current() } }
}
val adapterOutput = layout.buildDirectory.dir("adapter")
val launcherOutput = rootDir.resolve("../../out/launcher")
val checkLauncher by tasks.registering {
    inputs.dir(launcherOutput)
    doLast {
        for (name in listOf("DolphinDotNet.Launcher.dol", "README.md", "LICENSE", "build-info.json")) {
            check(launcherOutput.resolve(name).isFile) {
                "Missing bundled GameCube launcher; run python scripts/package_launcher.py --build from the repository root."
            }
        }
    }
}
val publishAdapter by tasks.registering(Exec::class) {
    workingDir = rootDir.resolve("../..")
    commandLine("dotnet", "publish", "tools/DolphinDotNet.DebugAdapter", "-c", "Release", "--no-self-contained", "-o", adapterOutput.get().asFile.absolutePath)
    inputs.files(fileTree("../../tools/DolphinDotNet.DebugAdapter") { exclude("bin/**", "obj/**") },
        fileTree("../../tools/DolphinDotNet.Debugger") { exclude("bin/**", "obj/**") })
    outputs.dir(adapterOutput)
}
tasks.prepareSandbox {
    dependsOn(publishAdapter, checkLauncher)
    from(adapterOutput) { into("${pluginName.get()}/adapter") }
    from(launcherOutput) { into("${pluginName.get()}/launcher") }
}

tasks.test {
    useJUnitPlatform()
    dependsOn(publishAdapter)
    // Adapter changes must rerun the packaged Java client's integration tests.
    inputs.dir(adapterOutput)
    systemProperty("dnd.adapter", adapterOutput.get().file("DolphinDotNet.DebugAdapter.dll").asFile.absolutePath)
    systemProperty("dnd.repositoryRoot", rootDir.resolve("../..").canonicalPath)
    systemProperty("dnd.python", System.getenv("PYTHON") ?: "python3")
}
