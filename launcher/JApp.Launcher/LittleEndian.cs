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
using System.Text;

/// <summary>Ported from
/// base/src/main/java/org/glavo/japp/util/ByteBufferUtils.java and
/// base/src/main/java/org/glavo/japp/io/LittleEndianDataOutput.java: cursor
/// over a little-endian byte span; every read advances and bounds failures
/// surface as <see cref="IOException" />.</summary>
internal ref struct LittleEndianReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;
    private int _position = 0;

    public int Remaining => _buffer.Length - _position;

    public byte ReadByte()
    {
        Require(1);
        return _buffer[_position++];
    }

    public short ReadInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer[_position..]);
        _position += 2;
        return value;
    }

    public int ReadInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer[_position..]);
        _position += 4;
        return value;
    }

    public long ReadInt64()
    {
        Require(8);
        var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer[_position..]);
        _position += 8;
        return value;
    }

    public string ReadString()
    {
        var length = ReadInt32();
        Require(length);
        var value = Encoding.UTF8.GetString(_buffer.Slice(_position, length));
        _position += length;
        return value;
    }

    public string? ReadStringOrNull()
    {
        var length = ReadInt32();
        return length == 0 ? null : ReadStringAfterLength(length);
    }

    private string ReadStringAfterLength(int length)
    {
        Require(length);
        var value = Encoding.UTF8.GetString(_buffer.Slice(_position, length));
        _position += length;
        return value;
    }

    public void ReadStringList(List<string> output)
    {
        var count = ReadInt32();
        for (var i = 0; i < count; i++)
            output.Add(ReadString());
    }

    private void Require(int count)
    {
        if (count < 0 || _position + count > _buffer.Length)
            throw new IOException("Unexpected end of data");
    }
}

/// <summary>Grows a little-endian byte stream; mirrors the writer half of the
/// Java side's ByteBufferOutputStream usage.</summary>
internal sealed class LittleEndianWriter
{
    private readonly MemoryStream _stream = new();

    public void WriteByte(byte value) => _stream.WriteByte(value);

    public void WriteInt16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        _stream.Write(bytes);
    }

    public void WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _stream.Write(bytes);
    }

    public void WriteInt64(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        _stream.Write(bytes);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(bytes.Length);
        _stream.Write(bytes);
    }

    public byte[] ToArray() => _stream.ToArray();
}
