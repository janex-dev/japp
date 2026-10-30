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

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>Boot module location. The distribution shape is japp + boot.jar
/// concatenated: the module path recognizes a packaged module by its .jar
/// file name (ModulePath.readModule has no magic sniffing), and JarFile reads
/// the trailing zip through the EOCD tolerating the binary prefix — so a
/// .jar-suffixed link to this executable is a valid module path entry with
/// zero copying. When a link cannot be created (Windows symlink privileges),
/// the embedded jar is extracted to the temp directory instead. Development
/// points the <c>JAPP_BOOT_JAR</c> environment variable at the Gradle-built
/// jar.</summary>
internal static partial class BootJar
{
    private const string EnvironmentVariable = "JAPP_BOOT_JAR";

    private const int ZipEocdMagic = 0x06054b50;

    public static string ResolveModulePath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            if (!File.Exists(fromEnvironment))
                throw new FileNotFoundException("Boot jar not found: " + fromEnvironment);

            return fromEnvironment;
        }

        if (Environment.ProcessPath is { } self)
        {
            var zipSize = TrailingZipSize(self);
            if (zipSize > 0)
                return CreateModulePathEntry(self, zipSize);
        }

        throw new InvalidOperationException(
            "Boot jar not found: set " + EnvironmentVariable + " or append a jar to this executable");
    }

    /// <summary>The link name derives from the executable path, so every run
    /// of the same executable shares one link and a replaced executable is
    /// picked up automatically (the link targets the path, not the inode).
    /// Symlink first; when that needs privilege the process does not have
    /// (Windows requires admin or developer mode), a hard link needs none —
    /// its only constraint is the same volume, and the leftover cross-volume
    /// cases fall back to extracting the embedded jar.</summary>
    internal static string CreateModulePathEntry(string self, long zipSize)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(self)))[..24].ToLowerInvariant();
        var link = Path.Combine(Path.GetTempPath(), "japp-boot-" + hash + ".jar");

        try
        {
            File.Delete(link);
            File.CreateSymbolicLink(link, self);
            return link;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A concurrent run of the same executable may have recreated
            // the link first; same name means same target, so it is
            // still usable.
            if (File.Exists(link))
                return link;
        }

        try
        {
            File.Delete(link);
            CreateHardLinkEntry(link, self);
            return link;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(link))
                return link;
        }

        return ExtractTrailingJar(self, zipSize);
    }

    // The BCL ships no hard-link API; link(2) and CreateHardLinkW cover the platforms.
    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinkFile(string existingFile, string newName);

    [LibraryImport("kernel32", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string fileName, string existingFileName, nint securityAttributes);

    internal static void CreateHardLinkEntry(string path, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(path, target, 0))
                throw new IOException($"CreateHardLink '{path}' failed (win32 error {Marshal.GetLastWin32Error()})");
            return;
        }

        if (LinkFile(target, path) != 0)
            throw new IOException($"link '{path}' failed (errno {Marshal.GetLastWin32Error()})");
    }

    private static string ExtractTrailingJar(string self, long zipSize)
    {
        using var stream = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.Read);
        var jar = new byte[zipSize];
        stream.Position = stream.Length - zipSize;
        stream.ReadExactly(jar);

        var hash = Convert.ToHexString(SHA1.HashData(jar))[..32].ToLowerInvariant();
        var target = Path.Combine(Path.GetTempPath(), "japp-boot-" + hash + ".jar");
        if (File.Exists(target))
            return target;

        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(temp, jar);
        File.Move(temp, target, overwrite: true);

        return target;
    }

    /// <summary>Size of the zip ending at the file end (central directory,
    /// EOCD and comment), or -1 when the file does not end with a zip. A zip
    /// comment of exactly the remaining tail length is required, which
    /// excludes the EOCD signature appearing incidentally inside code or
    /// data.</summary>
    internal static long TrailingZipSize(string file)
    {
        // The EOCD plus its comment never exceeds 22 + 65535 bytes.
        const int maxScan = 128 * 1024;

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 22)
            return -1;

        var tailSize = (int)Math.Min(stream.Length, maxScan);
        var tail = new byte[tailSize];
        stream.Position = stream.Length - tailSize;
        stream.ReadExactly(tail);

        for (var index = tailSize - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(tail.AsSpan(index)) != ZipEocdMagic)
                continue;

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(index + 20));
            if (commentLength != tailSize - index - 22)
                continue;

            var centralDirectoryLength = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index + 12));
            var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index + 16));
            var size = (long)centralDirectoryOffset + centralDirectoryLength + 22 + commentLength;
            return size <= stream.Length ? size : -1;
        }

        return -1;
    }
}
