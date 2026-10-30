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

using System.Runtime.Versioning;

namespace JApp.Launcher;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Process handoff: on Unix the launcher replaces itself with java
/// via execvp (signals reach java directly, no parent shim); on Windows the
/// child is spawned and awaited. The exec pattern matches the author's
/// JdkFind CommandRunner.</summary>
internal static partial class JavaCommand
{
    private const int ExitCommandNotFound = 127;
    private const int ExitCannotExecute = 126;

    public static async Task<int> RunAsync(IReadOnlyList<string> commandArgs)
    {
        if (OperatingSystem.IsWindows())
        {
            // In-process launch keeps the pid like the Unix exec path; a
            // failure before main is invoked falls through to spawning.
            if (JniLauncher.TryRun(commandArgs) is { } exitCode)
                return exitCode;

            return await SpawnAsync(commandArgs).ConfigureAwait(false);
        }

        return Exec(commandArgs);
    }

    [LibraryImport("libc", EntryPoint = "execvp", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [UnsupportedOSPlatform("windows")]
    private static partial int Execvp(string file, [In] string?[] argv);

    /// <summary>Replaces this process; a return means the command could not be executed.</summary>
    [UnsupportedOSPlatform("windows")]
    private static int Exec(IReadOnlyList<string> commandArgs)
    {
        // execvp(3) requires a NULL-terminated argv.
        var argv = commandArgs.Append(null).ToArray();
        _ = Execvp(commandArgs[0], argv);

        var errno = Marshal.GetLastWin32Error();
        Console.Error.WriteLine(errno switch
        {
            2 => $"japp: command not found: {argv[0]}",
            13 => $"japp: permission denied: {argv[0]}",
            _ => $"japp: cannot execute '{argv[0]}' (errno {errno})",
        });
        return errno == 2 ? ExitCommandNotFound : ExitCannotExecute;
    }

    private static async Task<int> SpawnAsync(IReadOnlyList<string> commandArgs)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = commandArgs[0],
                UseShellExecute = false,
            };
            foreach (var argument in commandArgs.Skip(1))
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
            {
                await Console.Error.WriteLineAsync($"japp: cannot execute '{commandArgs[0]}'.");
                return ExitCannotExecute;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            await Console.Error.WriteLineAsync($"japp: cannot execute '{commandArgs[0]}': {exception.Message}");
            return ExitCannotExecute;
        }
    }
}
