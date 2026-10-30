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

/// <summary>Ported from the writer half of
/// src/main/java/org/glavo/japp/launcher/Launcher.java plus the field ids
/// of boot/src/main/java/org/glavo/japp/boot/JAppBootArgs.java; the byte
/// layout is what org.glavo.japp.boot parses back.</summary>
public static class BootArgs
{
    public enum Field
    {
        End,
        MainClass,
        MainModule,
        ModulePath,
        ClassPath,
        AddReads,
        AddExports,
        AddOpens,
        EnableNativeAccess,
    }

    public const byte IdResolvedReferenceEnd = 0;
    public const byte IdResolvedReferenceLocal = 1;
    public const byte IdResolvedReferenceExternal = 2;

    internal sealed class Builder
    {
        private readonly LittleEndianWriter _writer = new();

        public void WriteHeader(string jappFile, long baseOffset, long bootMetadataOffset, long bootMetadataSize)
        {
            _writer.WriteString(jappFile);
            _writer.WriteInt64(baseOffset);
            _writer.WriteInt64(bootMetadataOffset);
            _writer.WriteInt64(bootMetadataSize);
        }

        public void WriteStringListField(Field field, List<string> list)
        {
            if (list.Count == 0)
                return;

            _writer.WriteByte((byte)field);
            _writer.WriteInt32(list.Count);
            foreach (var value in list)
                _writer.WriteString(value);
        }

        public async Task WriteClassOrModulePathAsync(
            Field field, int release, List<JAppResourceGroupReference> references,
            Func<JAppResourceGroupReference.Maven, Task<string>>? resolveMaven = null)
        {
            if (references.Count == 0)
                return;

            resolveMaven ??= async maven => Path.GetFullPath(await MavenResolver.ResolveAsync(
                maven.Repository, maven.Group, maven.Artifact, maven.Version, maven.Classifier).ConfigureAwait(false));

            // Maven resolutions start eagerly so several artifacts download
            // concurrently while the bytes are still written in file order.
            List<(JAppResourceGroupReference Reference, Task<string>? Download)> entries = [];
            foreach (var reference in references)
                entries.Add((reference, reference is JAppResourceGroupReference.Maven maven ? resolveMaven(maven) : null));

            _writer.WriteByte((byte)field);
            foreach (var (reference, download) in entries)
            {
                var name = reference.Name;

                if (reference is JAppResourceGroupReference.Local local)
                {
                    _writer.WriteByte(IdResolvedReferenceLocal);
                    _writer.WriteString(name);
                    _writer.WriteInt32(local.Index);

                    if (local.MultiReleaseIndexes is { } indexes)
                    {
                        // SortedList iterates ascending, mirroring the Java TreeMap.
                        foreach (var entry in indexes)
                        {
                            if (entry.Key <= release)
                                _writer.WriteInt32(entry.Value);
                            else
                                break;
                        }
                    }

                    _writer.WriteInt32(-1);
                }
                else if (reference is JAppResourceGroupReference.Maven)
                {
                    var file = await download!.ConfigureAwait(false);

                    _writer.WriteByte(IdResolvedReferenceExternal);
                    _writer.WriteString(name);
                    _writer.WriteString(file);
                }
                else
                {
                    throw new IOException("Type: " + reference.GetType());
                }
            }

            _writer.WriteByte(IdResolvedReferenceEnd);
        }

        public void WriteMainClass(string? mainClass)
        {
            if (mainClass is null)
                return;

            _writer.WriteByte((byte)Field.MainClass);
            _writer.WriteString(mainClass);
        }

        public void WriteMainModule(string? mainModule)
        {
            if (mainModule is null)
                return;

            _writer.WriteByte((byte)Field.MainModule);
            _writer.WriteString(mainModule);
        }

        public void WriteEnd() => _writer.WriteByte((byte)Field.End);

        public byte[] ToArray() => _writer.ToArray();
    }
}
