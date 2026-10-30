/*
 * Copyright (C) 2024 Glavo
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

plugins {
    id("java")
    id("com.gradleup.shadow") version "9.2.2"
}

allprojects {
    apply {
        plugin("java")
    }

    group = "org.glavo"
    version = "0.1.0-SNAPSHOT"

    repositories {
        mavenCentral()
    }

    tasks.compileJava {
        // TODO: Java 8
        sourceCompatibility = "9"
        targetCompatibility = "9"
    }
}

tasks.register("buildAll") {
    dependsOn(
        ":boot:bootJar", ":shadowJar"
    )
}

defaultTasks("buildAll")

dependencies {
    testImplementation("org.junit.jupiter:junit-jupiter:6.0.0")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")

    LWJGL.addDependency(this, "testImplementation", "lwjgl")
    LWJGL.addDependency(this, "testImplementation", "lwjgl-xxhash")
}

tasks.compileTestJava {
    options.compilerArgs.add("--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED")
}

tasks.test {
    dependsOn(
        "buildAll",
        ":test-case:HelloWorld:jar",
        ":test-case:ModulePath:jar",
    )

    useJUnitPlatform()
    jvmArgs(
        "--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED"
    )

    fun jarPath(projectName: String) =
        project(projectName).tasks.getByName<Jar>("jar").archiveFile.get().asFile.absolutePath

    fun testCase(projectName: String): String {
        val p = project(projectName)

        return p.configurations.runtimeClasspath.get().map { it.absolutePath }
            .plus(p.tasks.getByName<Jar>("jar").archiveFile.get().asFile.absolutePath)
            .joinToString(File.pathSeparator)
    }

    systemProperties(
        "japp.jar" to tasks.getByName<Jar>("shadowJar").archiveFile.get().asFile.absolutePath,
        "japp.testcase.helloworld" to testCase(":test-case:HelloWorld"),
        "japp.testcase.modulepath" to testCase(":test-case:ModulePath"),
    )

    testLogging {
        this.showStandardStreams = true
    }
}

dependencies {
    implementation(project(":base"))
    implementation(project(":boot"))
    implementation(libs.zstd.jni)
}

tasks.jar {
    manifest.attributes(
        "Main-Class" to "org.glavo.japp.Main",
        "Add-Exports" to "java.base/jdk.internal.misc"
    )
}

tasks.shadowJar {
    destinationDirectory.set(rootProject.layout.buildDirectory)
    archiveFileName.set("japp.jar")
}
