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

/// <summary>Ported from src/main/java/org/glavo/japp/Main.java (mode dispatch)
/// and the argument loop of
/// src/main/java/org/glavo/japp/launcher/Launcher.java. Exit codes follow the
/// Java launcher: 0 for help/success, 1 for usage errors and no-suitable-Java;
/// unlike the Java loop, positional arguments after the japp file are
/// forwarded to the application (the documented intent, matching
/// EmbeddedLauncher) instead of overwriting the file name.</summary>
/// <remarks>Dual launch modes: a bare launcher keeps the Java-compatible CLI;
/// an executable with an embedded archive is TRANSPARENT by default — every
/// argument reaches the application verbatim — and the single reserved first
/// token <c>--japp</c> opens the full launcher CLI, where <c>-J</c> options
/// and the <c>--</c> separator apply.</remarks>
internal static class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await RunAsync(args, EmbeddedPath()).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync("Error: " + exception.Message);
            return 1;
        }
    }

    private static string? EmbeddedPath() =>
        Environment.ProcessPath is { } self && JAppLauncherMetadata.HasEmbeddedArchive(self) ? self : null;

    internal static Task<int> RunAsync(string[] args, string? embeddedPath) =>
        RunAsync(args, embeddedPath, Launcher.RunAsync);

    internal static async Task<int> RunAsync(
        string[] args, string? embeddedPath,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>, Task<int>> runApp)
    {
        if (embeddedPath is not null)
        {
            if (args.Length == 0 || args[0] != "--japp")
                return await runApp(embeddedPath, [], args).ConfigureAwait(false);

            args = args[1..];
            if (args.Length == 0)
            {
                PrintHelpMessage(Console.Out);
                return 0;
            }
        }
        else if (args.Length == 0)
        {
            PrintHelpMessage(Console.Out);
            return 0;
        }

        switch (args[0])
        {
            case "help":
            case "-help":
            case "--help":
                PrintHelpMessage(Console.Out);
                return 0;

            case "version":
            case "-version":
            case "--version":
                Console.WriteLine("japp 0.1.0");
                return 0;

            case "run":
                return await RunCommand(args[1..], embeddedPath, runApp).ConfigureAwait(false);

            case "bundle":
                return Bundler.Run(args[1..]);

            case "list-java":
                await ListJava().ConfigureAwait(false);
                return 0;

            default:
                await Console.Error.WriteLineAsync("Unsupported mode: " + args[0]);
                PrintHelpMessage(Console.Error);
                return 1;
        }
    }

    private static async Task<int> RunCommand(
        string[] args, string? embeddedPath,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>, Task<int>> runApp)
    {
        var jvmOptions = new List<string>();
        string? jappFile = null;
        var appArgs = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Everything after the separator is verbatim application input.
            if (arg == "--")
            {
                appArgs.AddRange(args[(i + 1)..]);
                break;
            }

            if (jappFile is not null)
            {
                appArgs.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "-help":
                case "--help":
                    PrintRunHelpMessage(Console.Out);
                    return 0;

                default:
                    if (arg.StartsWith("-J", StringComparison.Ordinal))
                        jvmOptions.Add(arg[2..]);
                    else if (arg.StartsWith('-'))
                    {
                        await Console.Error.WriteLineAsync("Unknown option: " + arg);
                        return 1;
                    }
                    else if (embeddedPath is not null && !File.Exists(arg))
                    {
                        // With an embedded archive a positional that names no
                        // existing file is an application argument, not a typo
                        // for the archive path.
                        appArgs.Add(arg);
                    }
                    else
                        jappFile = arg;
                    break;
            }
        }

        jappFile ??= embeddedPath;

        if (jappFile is null)
        {
            PrintRunHelpMessage(Console.Error);
            return 1;
        }

        return await runApp(Path.GetFullPath(jappFile), jvmOptions, appArgs).ConfigureAwait(false);
    }

    private static async Task ListJava()
    {
        var javas = await JavaFinder.LocateAllAsync().ConfigureAwait(false);
        if (javas.Count == 0)
        {
            Console.WriteLine("Java runtime not found");
            return;
        }

        Console.WriteLine("Java runtime found:");
        foreach (var (jvm, context) in javas)
            Console.WriteLine($"  - {jvm.Executable.Path} ({context})");
    }

    private static void PrintHelpMessage(TextWriter writer)
    {
        writer.WriteLine("Usage: japp <mode> [options]");
        writer.WriteLine("Supported mode:");
        writer.WriteLine("  japp run");
        writer.WriteLine("  japp bundle");
        writer.WriteLine("  japp list-java");
    }

    private static void PrintRunHelpMessage(TextWriter writer)
    {
        writer.WriteLine("Usage: japp run [options] <japp file> [args]");
        writer.WriteLine("Supported options:");
        writer.WriteLine("  --help      Print this message");
        writer.WriteLine("  -J<flag>    Pass <flag> directly to java");
    }
}
