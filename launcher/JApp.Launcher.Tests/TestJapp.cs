namespace JApp.Launcher.Tests;

/// <summary>Builds japp file bytes by hand for the format tests: content =
/// data pool + boot metadata + launcher metadata + FileEnd, optionally
/// wrapped in a prefix and a trailing zip EOCD.</summary>
internal static class TestJapp
{
    public const int DataPoolSize = 16;

    public static byte[] Eocd()
    {
        var eocd = new byte[22];
        eocd[0] = 0x50;
        eocd[1] = 0x4b;
        eocd[2] = 0x05;
        eocd[3] = 0x06;
        return eocd;
    }

    public static byte[] GroupBytes(Action<LittleEndianWriter> writeFields)
    {
        var writer = new LittleEndianWriter();
        writer.WriteInt32(JAppConfigGroup.MagicNumber);
        writeFields(writer);
        writer.WriteByte((byte)JAppConfigGroup.Field.End);
        return writer.ToArray();
    }

    public static void WriteStringField(LittleEndianWriter writer, JAppConfigGroup.Field field, string value)
    {
        writer.WriteByte((byte)field);
        writer.WriteString(value);
    }

    public static void WriteStringListField(
        LittleEndianWriter writer, JAppConfigGroup.Field field, IReadOnlyList<string> values)
    {
        writer.WriteByte((byte)field);
        writer.WriteInt32(values.Count);
        foreach (var value in values)
            writer.WriteString(value);
    }

    public static void WriteLocalReference(
        LittleEndianWriter writer, string name, int index, params (int Version, int Index)[] multiRelease)
    {
        writer.WriteByte(JAppResourceGroupReference.MagicNumber);
        writer.WriteByte(JAppResourceGroupReference.Local.Id);
        writer.WriteString(name);
        writer.WriteInt32(index);
        writer.WriteInt32(multiRelease.Length);
        foreach (var (version, multiIndex) in multiRelease)
        {
            writer.WriteInt32(version);
            writer.WriteInt32(multiIndex);
        }
    }

    public static byte[] BuildFile(
        byte[] launcherMetadata, long bootMetadataOffset, byte[]? prefix = null, byte[]? zipSuffix = null,
        short majorVersion = JAppLauncherMetadata.MajorVersion,
        short minorVersion = JAppLauncherMetadata.MinorVersion,
        long flags = 0)
    {
        var launcherMetadataOffset = DataPoolSize + bootMetadataOffset;
        var fileContentSize = launcherMetadataOffset + launcherMetadata.Length + JAppLauncherMetadata.FileEndSize;

        var buffer = new LittleEndianWriter();
        if (prefix is not null)
            foreach (var b in prefix)
                buffer.WriteByte(b);

        for (var i = 0; i < launcherMetadataOffset; i++)
            buffer.WriteByte(0);

        foreach (var b in launcherMetadata)
            buffer.WriteByte(b);

        buffer.WriteInt32(JAppLauncherMetadata.FileEndMagicNumber);
        buffer.WriteInt16(majorVersion);
        buffer.WriteInt16(minorVersion);
        buffer.WriteInt64(flags);
        buffer.WriteInt64(fileContentSize);
        buffer.WriteInt64(bootMetadataOffset);
        buffer.WriteInt64(launcherMetadataOffset);
        for (var i = 0; i < 24; i++)
            buffer.WriteByte(0);

        if (zipSuffix is not null)
            foreach (var b in zipSuffix)
                buffer.WriteByte(b);

        return buffer.ToArray();
    }
}
