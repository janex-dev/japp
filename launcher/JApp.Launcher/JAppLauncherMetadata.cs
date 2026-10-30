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

using System.Buffers.Binary;

/// <summary>Ported from
/// src/main/java/org/glavo/japp/launcher/JAppLauncherMetadata.java: reads
/// the FileEnd trailer. The physical file is [prefix][japp content][zip
/// suffix]; the trailer is found by stripping the suffix (EOCD probe) and
/// the prefix length is recovered as baseOffset.</summary>
public sealed class JAppLauncherMetadata
{
    private const int DefaultEndBufferSize = 8192;

    public const int FileEndSize = 64;
    public const int FileEndMagicNumber = 0x5050414a;

    public const short MajorVersion = -1;
    public const short MinorVersion = 0;

    private const int ZipEocdSize = 22;
    private const int ZipEocdMagic = 0x06054b50;

    private static long GetEndZipSize(ReadOnlySpan<byte> endBuffer)
    {
        if (endBuffer.Length < ZipEocdSize)
            return -1;

        var index = endBuffer.Length - ZipEocdSize;

        if (BinaryPrimitives.ReadInt32LittleEndian(endBuffer[index..]) != ZipEocdMagic)
            return -1;
        index += 4;

        // Number of this disk
        if (BinaryPrimitives.ReadInt16LittleEndian(endBuffer[index..]) != 0)
            return -1;
        index += 2;

        // Disk where central directory starts
        if (BinaryPrimitives.ReadInt16LittleEndian(endBuffer[index..]) != 0)
            return -1;
        index += 2;

        // Number of central directory records on this disk
        index += 2;

        // Total number of central directory records
        index += 2;

        var centralDirectoryLength = BinaryPrimitives.ReadUInt32LittleEndian(endBuffer[index..]);
        index += 4;
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(endBuffer[index..]);
        index += 4;

        // Comment length must be zero for the trailer to sit at the very end
        if (BinaryPrimitives.ReadInt16LittleEndian(endBuffer[index..]) != 0)
            return -1;

        return ZipEocdSize + centralDirectoryLength + centralDirectoryOffset;
    }

    public static JAppLauncherMetadata ReadFile(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        long fileSize = stream.Length;

        if (fileSize < FileEndSize)
            throw new IOException("File is too small");

        var endBufferSize = (int)Math.Min(fileSize, DefaultEndBufferSize);
        var endBuffer = ReadFully(stream, fileSize - endBufferSize, endBufferSize);

        var position = endBufferSize - FileEndSize;
        var magicNumber = BinaryPrimitives.ReadInt32LittleEndian(endBuffer.AsSpan(position));

        if (magicNumber != FileEndMagicNumber)
        {
            long endZipSize = GetEndZipSize(endBuffer);
            if (endZipSize > 0 && endZipSize < fileSize)
            {
                fileSize -= endZipSize;

                if (fileSize < endBufferSize)
                    endBufferSize = (int)fileSize;

                endBuffer = ReadFully(stream, fileSize - endBufferSize, endBufferSize);
                position = endBufferSize - FileEndSize;
                magicNumber = BinaryPrimitives.ReadInt32LittleEndian(endBuffer.AsSpan(position));
            }

            if (magicNumber != FileEndMagicNumber)
                throw new IOException($"Invalid magic number: 0x{(uint)magicNumber:x}");
        }

        position += 4;
        var majorVersion = BinaryPrimitives.ReadInt16LittleEndian(endBuffer.AsSpan(position));
        position += 2;
        var minorVersion = BinaryPrimitives.ReadInt16LittleEndian(endBuffer.AsSpan(position));
        position += 2;
        var flags = BinaryPrimitives.ReadInt64LittleEndian(endBuffer.AsSpan(position));
        position += 8;
        var fileContentSize = BinaryPrimitives.ReadInt64LittleEndian(endBuffer.AsSpan(position));
        position += 8;
        var bootMetadataOffset = BinaryPrimitives.ReadInt64LittleEndian(endBuffer.AsSpan(position));
        position += 8;
        var launcherMetadataOffset = BinaryPrimitives.ReadInt64LittleEndian(endBuffer.AsSpan(position));
        // ReSharper disable once RedundantAssignment
        position += 8;

        // remaining 24 bytes are reserved

        if (majorVersion != MajorVersion || minorVersion != MinorVersion)
            throw new IOException("Version number mismatch");

        if (flags != 0)
            throw new IOException("Unsupported flags: " + Convert.ToString(flags, 2));

        if (fileContentSize > fileSize || fileContentSize < FileEndSize)
            throw new IOException("Invalid file size: " + fileContentSize);

        if (launcherMetadataOffset >= fileContentSize - FileEndSize)
            throw new IOException("Invalid metadata offset: " + launcherMetadataOffset);

        var baseOffset = fileSize - fileContentSize;
        long metadataSize = fileContentSize - FileEndSize - launcherMetadataOffset;

        JAppConfigGroup group;
        if (metadataSize < endBufferSize - FileEndSize)
        {
            var endBufferPosition = (int)(endBufferSize - metadataSize - FileEndSize);
            group = JAppConfigGroup.ReadFrom(
                endBuffer.AsSpan(endBufferPosition, (int)metadataSize));
        }
        else
        {
            if (metadataSize > 1 << 30)
                throw new IOException("Metadata is too large");

            var metadataBuffer = ReadFully(stream, baseOffset + launcherMetadataOffset, (int)metadataSize);
            group = JAppConfigGroup.ReadFrom(metadataBuffer);
        }

        return new JAppLauncherMetadata(baseOffset, bootMetadataOffset, launcherMetadataOffset - bootMetadataOffset, group);
    }

    /// <summary>True when the file carries a japp archive: a FileEnd trailer
    /// at the physical end, or before a single trailing zip (the
    /// self-contained distribution shape). Only the trailer position is
    /// probed — the metadata itself is not parsed.</summary>
    public static bool HasEmbeddedArchive(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            long fileSize = stream.Length;
            if (fileSize < FileEndSize)
                return false;

            var tail = new byte[FileEndSize];
            stream.Position = fileSize - FileEndSize;
            stream.ReadExactly(tail);

            if (BinaryPrimitives.ReadInt32LittleEndian(tail) == FileEndMagicNumber)
                return true;

            long endZipSize = GetEndZipSize(tail);
            if (endZipSize <= 0 || endZipSize >= fileSize)
                return false;

            var strippedSize = fileSize - endZipSize;
            if (strippedSize < FileEndSize)
                return false;

            stream.Position = strippedSize - FileEndSize;
            stream.ReadExactly(tail, 0, 4);
            return BinaryPrimitives.ReadInt32LittleEndian(tail) == FileEndMagicNumber;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] ReadFully(FileStream stream, long position, int size)
    {
        var buffer = new byte[size];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }

    internal JAppLauncherMetadata(long baseOffset, long bootMetadataOffset, long bootMetadataSize, JAppConfigGroup group)
    {
        BaseOffset = baseOffset;
        BootMetadataOffset = bootMetadataOffset;
        BootMetadataSize = bootMetadataSize;
        Group = group;
    }

    public long BaseOffset { get; }

    public long BootMetadataOffset { get; }

    public long BootMetadataSize { get; }

    public JAppConfigGroup Group { get; }
}
