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

/// <summary>Ported from
/// src/main/java/org/glavo/japp/launcher/JAppResourceGroupReference.java
/// (read side only — the launcher never writes these).</summary>
public abstract class JAppResourceGroupReference(string name)
{
    public const byte MagicNumber = 0x01;

    /// <summary>Never null on the launcher side — an unnamed entry reads back
    /// as the empty string, which the boot side treats as anonymous.</summary>
    public string Name { get; } = name;

    internal static JAppResourceGroupReference ReadFrom(ref LittleEndianReader reader)
    {
        var magic = reader.ReadByte();
        if (magic != MagicNumber)
            throw new IOException($"Wrong reference magic: 0x{magic:x2}");

        var id = reader.ReadByte();
        var name = reader.ReadString();
        switch (id)
        {
            case Local.Id:
            {
                var index = reader.ReadInt32();
                var multiCount = reader.ReadInt32();
                SortedList<int, int>? multiReleaseIndexes = null;
                if (multiCount != 0)
                {
                    if (multiCount < 0)
                        throw new IOException("Invalid multi-release count: " + multiCount);

                    multiReleaseIndexes = new SortedList<int, int>(multiCount);
                    for (var i = 0; i < multiCount; i++)
                    {
                        var multiVersion = reader.ReadInt32();
                        var multiIndex = reader.ReadInt32();

                        if (multiVersion < 9)
                            throw new IOException("Version should not less than 9");

                        if (multiReleaseIndexes.TryAdd(multiVersion, multiIndex) is false)
                            throw new IOException("Duplicate version: " + multiVersion);
                    }
                }

                return new Local(name, index, multiReleaseIndexes);
            }
            case Maven.Id:
            {
                var repository = reader.ReadStringOrNull();
                var group = reader.ReadStringOrNull();
                var artifact = reader.ReadStringOrNull();
                var version = reader.ReadStringOrNull();
                var classifier = reader.ReadStringOrNull();

                // repository and classifier are genuinely optional on the
                // wire; a missing coordinate is malformed.
                if (group is null || artifact is null || version is null)
                    throw new IOException("Incomplete maven reference");

                return new Maven(name, repository, group, artifact, version, classifier);
            }
            default:
                throw new IOException($"Unknown reference id: 0x{id:x2}");
        }
    }

    public sealed class Local(string name, int index, SortedList<int, int>? multiReleaseIndexes)
        : JAppResourceGroupReference(name)
    {
        public const byte Id = 0;

        public int Index => index;

        public SortedList<int, int>? MultiReleaseIndexes => multiReleaseIndexes;

        public override string ToString() => multiReleaseIndexes is null
            ? "Local[index=" + index + "]"
            : $"Local[index={index}, multiReleaseIndexes={multiReleaseIndexes}]";
    }

    public sealed class Maven(
        string name,
        string? repository,
        string group,
        string artifact,
        string version,
        string? classifier)
        : JAppResourceGroupReference(name)
    {
        public const byte Id = 1;

        public string? Repository => repository;
        public string Group => group;
        public string Artifact => artifact;
        public string Version => version;
        public string? Classifier => classifier;

        public override string ToString() =>
            $"Maven[group={group}, artifact={artifact}, version={version}, classifier={classifier}]";
    }
}
