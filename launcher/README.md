# JApp.Launcher

A native launcher for [japp](../README.md) files, implemented in C# and published as a
single-file NativeAOT executable (`japp`, ~5.5 MB, no .NET runtime required).

The launcher does everything before the JVM starts: reads the japp launcher metadata,
selects a suitable Java runtime according to the declared conditions, merges the
config groups, resolves Maven dependencies, and starts the boot module (`org.glavo.japp.boot`), which loads the application. The command line is compatible
with the Java launcher.

## How it works

```
┌──────────────────────┐                    ┌───────────────────────────────┐
│ japp (native)        │  exec / spawn      │ java … org.glavo.japp.boot    │
│ metadata · JVM       │ ─────────────────► │ resources · module layer ·    │
│ selection · config · │  boot args passed  │ main                          │
│ maven · command line │  as a system prop  │                               │
└──────────────────────┘                    └───────────────────────────────┘
```

* **JVM discovery** uses [JdkFind](https://www.nuget.org/packages/JdkFind) (`JAVA_HOME`
  first, then PATH, `/Library/Java`, Homebrew, SDKMAN, asdf, Scoop, …). The first
  runtime satisfying the root condition wins, e.g. `java(version: 21) && java(os: linux|macos)`.
* **Maven dependencies** resolve at launch time: `local` reuses the user's
  `~/.m2/repository` (downloading missing artifacts into it), while `central` (the default)
  keeps its own platform cache so the bytes always match what Central published.
* On Unix the launcher **replaces itself** with the java process; on Windows it hosts the
  JVM in-process via `jvm.dll` (same pid, exit code and Ctrl+C semantics, emulating the
  java.exe launcher behavior), falling back to spawning `java.exe` when the JVM cannot
  be loaded.

## Build

```shell
./gradlew :boot:buildLauncher                             # current platform
./gradlew :boot:buildLauncher -PlauncherRid=linux-x64     # cross build
```

`:boot:buildLauncher` runs `dotnet publish` (NativeAOT) and appends the boot jar
produced by `:boot:bootJar`. The executable lands in
`launcher/JApp.Launcher/bin/Release/net10.0/<rid>/publish/japp`.

A manual `dotnet publish -c Release -r <rid> /p:PublishAot=true` works too. The
property `JappBootJar` selects the jar (default `../build/japp-boot.jar`); a
missing default publishes a bare launcher, a missing explicit value fails the
build, and `AppendBootJar=false` disables the step.

## Usage

Bare launcher:

```shell
japp run [-J<flag>]... <file.japp> [args...]    # -J passes flags to the JVM
japp list-java                                   # list discovered runtimes
```

Create a self-contained executable from a japp archive:

```shell
japp bundle <file.japp> [-o <output>] [--launcher <base>]
```

The output is `[launcher][japp archive][boot.jar]`. The default base is the running
executable; pass `--launcher <path>` to bundle with a launcher built for another
platform (cross builds). The `.exe` suffix is added when the base is a PE image.

A bundled executable is **transparent by default** — every argument goes to the
application verbatim:

```shell
./myapp [app args...]                          # run the bundled application
./myapp --japp run -J-Xmx4g -- app args...     # full launcher CLI via the gateway
./myapp --japp list-java
```

| Variable        | Purpose                                                          |
|-----------------|------------------------------------------------------------------|
| `JAPP_BOOT_JAR` | Explicit boot module jar path (overrides the self-appended tail) |

## Development

```
launcher/
├── JApp.Launcher.slnx
├── JApp.Launcher/          # the application (AOT-compatible, warnings as errors)
└── JApp.Launcher.Tests/    # xunit.v3
```

Run the tests with `cd launcher && dotnet test`. Files ported from the Java
implementation keep the original Apache-2.0 headers.
