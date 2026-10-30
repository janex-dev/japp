/*
 * Copyright (C) 2023 Glavo
 * Copyright (C) 2026 ghostflyby
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

namespace JApp.Launcher;

using JdkFind;

/// <summary>The fields a condition evaluates: the checked names japp's Java
/// side derives from a candidate's release file.</summary>
public sealed record JvmContext(int MajorVersion, string Os, string Arch, string LibC)
{
    public override string ToString() => $"version={MajorVersion}, os={Os}, arch={Arch}, libc={LibC}";
}

/// <summary>JVM discovery adapting JdkFind (NuGet) to japp's condition model:
/// the platform name normalization tables are ported from
/// src/main/java/org/glavo/japp/platform/{OperatingSystem,Architecture,LibC}.java
/// so conditions match the same checked names the Java launcher tests
/// against. JdkFind does not expose LIBC, so it is re-read from the
/// candidate's release file.</summary>
public static class JavaFinder
{
    // Condition matching needs only release-file facts; the per-installation
    // runtime probe (a child java process each) is pure startup latency here.
    private static readonly JdkFinder Finder = JdkFinder.Default with { ProbeRuntimeProperties = false };

    public static async Task<(Jvm Jvm, JvmContext Context)?> SearchAsync(
        JAppConfigGroup config, CancellationToken cancellationToken = default)
    {
        foreach (var jvm in await Finder.LocateAsync(cancellationToken).ConfigureAwait(false))
        {
            var context = TryCreateContext(jvm);
            if (context is not null && config.CanApply(context))
                return (jvm, context);
        }

        return null;
    }

    public static async Task<IReadOnlyList<(Jvm Jvm, JvmContext Context)>> LocateAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<(Jvm, JvmContext)>();
        foreach (var jvm in await Finder.LocateAsync(cancellationToken).ConfigureAwait(false))
        {
            var context = TryCreateContext(jvm);
            if (context is not null)
                result.Add((jvm, context));
        }

        return result;
    }

    private static JvmContext? TryCreateContext(Jvm jvm)
    {
        if (jvm.LanguageVersion is not { } majorVersion)
            return null;

        var os = ParseOperatingSystem(jvm.Executable.OsName);
        if (os is null)
            return null;

        var arch = ParseArchitecture(jvm.Executable.Architecture);
        if (arch is null)
            return null;

        var libc = ParseLibC(ReadLibCValue(jvm.Home.FullName));
        if (libc is null)
            return null;

        return new JvmContext(majorVersion, os, arch, libc);
    }

    // Ported from OperatingSystem.parseOperatingSystem + getCheckedName.
    private static string? ParseOperatingSystem(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        name = name.Trim().ToLowerInvariant();
        if (name.Contains("mac") || name.Contains("darwin"))
            return "macos";
        if (name.Contains("win"))
            return "windows";
        if (name.Contains("linux"))
            return "linux";

        return null;
    }

    // Ported from Architecture.parseArchitecture + getCheckedName: raw
    // OS_ARCH/os.arch spellings to japp's checked names.
    private static string? ParseArchitecture(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        value = value.Trim().ToLowerInvariant();

        switch (value)
        {
            case "x8664":
            case "x86-64":
            case "x86_64":
            case "amd64":
            case "ia32e":
            case "em64t":
            case "x64":
                return "x86-64";
            case "x8632":
            case "x86-32":
            case "x86_32":
            case "x86":
            case "i86pc":
            case "i386":
            case "i486":
            case "i586":
            case "i686":
            case "ia32":
            case "x32":
                return "x86";
            case "arm64":
            case "aarch64":
                return "aarch64";
            case "arm":
            case "arm32":
                return "arm";
            case "mips64":
                return "mips-64";
            case "mips64el":
                return "mips64el";
            case "mips":
            case "mips32":
                return "mips";
            case "mipsel":
            case "mips32el":
                return "mipsel";
            case "riscv32":
                return "riscv-32";
            case "riscv64":
                return "riscv-64";
            case "ia64":
            case "ia64w":
            case "itanium64":
                return "ia-64";
            case "sparcv9":
            case "sparc64":
                return "sparcv9";
            case "sparc":
            case "sparc32":
                return "sparc";
            case "ppc64":
            case "powerpc64":
                return "ppc64";
            case "ppc64le":
            case "powerpc64le":
                return "ppc64le";
            case "ppc":
            case "ppc32":
            case "powerpc":
            case "powerpc32":
                return "ppc";
            case "ppcle":
            case "ppc32le":
            case "powerpcle":
            case "powerpc32le":
                return "ppcle";
            case "s390":
                return "s390";
            case "s390x":
                return "s390x";
            case "loongarch32":
                return "loongarch32";
            case "loongarch64":
                return "loongarch64";
            default:
                if (value.StartsWith("armv7"))
                    return "arm";
                if (value.StartsWith("armv8") || value.StartsWith("armv9"))
                    return "aarch64";
                return null;
        }
    }

    // Ported from LibC.parseLibC: a missing LIBC key means the default libc.
    private static string? ParseLibC(string? value) => value switch
    {
        null => "default",
        "" or "default" or "gnu" => "default",
        "musl" => "musl",
        _ => null,
    };

    private static string? ReadLibCValue(string home)
    {
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(home, "release")))
            {
                if (line.StartsWith("LIBC=", StringComparison.Ordinal))
                    return line["LIBC=".Length..].Trim('"');
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable release file degrades to the default libc.
        }

        return null;
    }
}
