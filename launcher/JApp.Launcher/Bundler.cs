/*
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

/// <summary>Bundles a japp archive into a launcher executable: output =
/// [launcher executable][japp archive][the launcher's trailing boot jar]. One
/// formula covers every base shape — a bare launcher (append), a launcher
/// with a boot jar tail (insert before the jar), and a launcher that already
/// embeds an archive (replace it, keeping the executable and the jar).
/// <c>--launcher</c> accepts a foreign-RID executable for cross
/// builds.</summary>
internal static class Bundler
{
    public static int Run(string[] args)
    {
        string? jappFile = null;
        string? output = null;
        string? launcher = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-help":
                case "--help":
                    PrintHelpMessage(Console.Out);
                    return 0;

                case "-o":
                case "--output":
                case "--launcher":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for " + arg);
                        return 1;
                    }

                    if (arg == "--launcher")
                        launcher = args[++i];
                    else
                        output = args[++i];
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine("Unknown option: " + arg);
                        return 1;
                    }

                    if (jappFile is not null)
                    {
                        Console.Error.WriteLine("Multiple input files: " + arg);
                        return 1;
                    }

                    jappFile = arg;
                    break;
            }
        }

        if (jappFile is null)
        {
            PrintHelpMessage(Console.Error);
            return 1;
        }

        try
        {
            return Bundle(
                Path.GetFullPath(jappFile),
                output is null ? null : Path.GetFullPath(output),
                launcher is null ? Environment.ProcessPath : Path.GetFullPath(launcher));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return 1;
        }
    }

    private static int Bundle(string jappFile, string? output, string? launcher)
    {
        if (launcher is null)
        {
            Console.Error.WriteLine("Error: No base launcher available; pass --launcher <file>");
            return 1;
        }

        if (output is null)
        {
            var name = Path.GetFileName(jappFile);
            output = Path.Combine(
                Path.GetDirectoryName(jappFile) ?? ".",
                name.EndsWith(".japp", StringComparison.OrdinalIgnoreCase) ? name[..^".japp".Length] : name + ".out");
            // The TARGET's format decides — a PE base launcher gains the .exe
            // suffix even when bundling on a Unix host (cross build).
            if (IsWindowsExecutable(launcher) && !output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                output += ".exe";
        }

        if (string.Equals(output, launcher, StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, jappFile, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: Output would overwrite an input: " + output);
            return 1;
        }

        // Validate the archive before any output is written.
        JAppLauncherMetadata.ReadFile(jappFile);

        var launcherLength = new FileInfo(launcher).Length;
        var tailSize = BootJar.TrailingZipSize(launcher);
        var tailStart = launcherLength - Math.Max(tailSize, 0);

        // A base that already embeds an archive yields its executable size as
        // BaseOffset; a base without one reads as pure executable up to the
        // trailing jar.
        long exeSize;
        try
        {
            exeSize = JAppLauncherMetadata.ReadFile(launcher).BaseOffset;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            exeSize = tailStart;
        }

        using (var input = new FileStream(launcher, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var archive = new FileStream(jappFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var result = new FileStream(output, FileMode.Create, FileAccess.Write))
        {
            CopyExactly(input, result, exeSize);
            archive.CopyTo(result);
            input.Position = tailStart;
            input.CopyTo(result);
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(output, File.GetUnixFileMode(launcher));

        Console.WriteLine($"japp: bundled {jappFile} -> {output}");
        if (tailSize <= 0)
            Console.Error.WriteLine(
                "japp: warning: the base launcher carries no boot jar; JAPP_BOOT_JAR is required at runtime");

        return 0;
    }

    /// <summary>True when the base launcher is a PE image ("MZ"). Mach-O and
    /// ELF bases need no suffix on their platforms.</summary>
    private static bool IsWindowsExecutable(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[2];
        return stream.Read(header) == 2 && header[0] == 0x4D && header[1] == 0x5A;
    }

    private static void CopyExactly(FileStream input, FileStream output, long count)
    {
        var buffer = new byte[80 * 1024];
        long remaining = count;
        while (remaining > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new IOException("Unexpected end of launcher");
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void PrintHelpMessage(TextWriter writer)
    {
        writer.WriteLine("Usage: japp bundle [options] <japp file>");
        writer.WriteLine("Options:");
        writer.WriteLine("  -o, --output <file>    Output executable (default: input name without .japp)");
        writer.WriteLine("      --launcher <file>  Base launcher executable (default: this executable)");
        writer.WriteLine("      --help             Print this message");
    }
}
