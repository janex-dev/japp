import org.glavo.mic.tasks.CompileModuleInfo

plugins {
    id("org.glavo.compile-module-info-plugin") version "2.0" apply false
}

dependencies {
    implementation(project(":base"))
}

tasks.compileJava {
    options.compilerArgs.addAll(
        listOf(
            "--add-exports=java.base/jdk.internal.loader=ALL-UNNAMED",
            "--add-exports=java.base/jdk.internal.module=ALL-UNNAMED",
            "--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED",
        )
    )
}

val mainClassName = "org.glavo.japp.boot.JAppBootLauncher"

val compileModuleInfo by tasks.registering(CompileModuleInfo::class) {
    sourceFile.set(layout.projectDirectory.file("src/main/module-info/module-info.java"))
    targetFile.set(layout.buildDirectory.file("classes/module-info/main/module-info.class"))
    moduleMainClass = mainClassName
}

tasks.classes.get().dependsOn(compileModuleInfo)

val bootJar = tasks.register<Jar>("bootJar") {
    destinationDirectory.set(rootProject.layout.buildDirectory)
    archiveFileName.set("japp-boot.jar")

    dependsOn(configurations.runtimeClasspath)

    from(sourceSets.main.get().output)
    from(configurations.runtimeClasspath.get().map { zipTree(it) })
}

tasks.withType<Jar> {
    from(compileModuleInfo.map { it.targetFile })
    manifest.attributes("Main-Class" to mainClassName)
}

// Publishes the native japp launcher (NativeAOT). The jar appended to the
// published executable is bootJar's own output, passed to MSBuild explicitly
// via JappBootJar rather than the csproj default path; the same provider is
// the task input, which also carries the bootJar task dependency.
// Cross builds: -PlauncherRid=<rid>.
val launcherRid = providers.gradleProperty("launcherRid").orElse(
    run {
        val os = System.getProperty("os.name").lowercase()
        val arch = System.getProperty("os.arch")
        when {
            "mac" in os -> if (arch == "aarch64") "osx-arm64" else "osx-x64"
            "win" in os -> if (arch == "aarch64") "win-arm64" else "win-x64"
            else -> if (arch == "aarch64") "linux-arm64" else "linux-x64"
        }
    },
)

val bootJarOutput = bootJar.flatMap { it.archiveFile }

tasks.register<Exec>("buildLauncher") {
    group = "build"
    description = "Publishes the native japp launcher with the boot jar appended."

    workingDir = rootProject.file("launcher/JApp.Launcher")

    commandLine("dotnet")

    argumentProviders.add {
        listOf(
            "publish", "-c", "Release", "-r", launcherRid.get(), "/p:PublishAot=true",
            "/p:JappBootJar=" + bootJarOutput.get().asFile.absolutePath,
        )
    }

    inputs.property("rid", launcherRid)
    inputs.file(bootJarOutput)
    inputs.files(fileTree("../launcher/JApp.Launcher") { exclude("bin/**", "obj/**") })
    outputs.file(
        launcherRid.map {
            "../launcher/JApp.Launcher/bin/Release/net10.0/$it/publish/" +
                    if (it.startsWith("win")) "japp.exe" else "japp"
        },
    )
}

