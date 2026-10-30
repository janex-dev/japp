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
/// src/main/java/org/glavo/japp/launcher/JAppConfigGroup.java (read and
/// resolve side only — the launcher never writes these).</summary>
public sealed class JAppConfigGroup
{
    public enum Field
    {
        End,
        Condition,
        MainClass,
        MainModule,
        ModulePath,
        ClassPath,
        JvmProperties,
        AddReads,
        AddExports,
        AddOpens,
        EnableNativeAccess,
        ExtraJvmOptions,
        SubGroups,
    }

    private static readonly Field[] FieldValues = Enum.GetValues<Field>();

    public const int MagicNumber = 0x00505247;

    public static JAppConfigGroup ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var reader = new LittleEndianReader(buffer);
        return ReadFrom(ref reader);
    }

    // Continues at the shared cursor's position so nested groups parse in place.
    internal static JAppConfigGroup ReadFrom(ref LittleEndianReader reader)
    {
        var group = new JAppConfigGroup();

        var magic = reader.ReadInt32();
        if (magic != MagicNumber)
            throw new IOException($"Wrong group magic: 0x{magic:x2}");

        Field field;
        while ((field = ReadField(ref reader)) != Field.End)
        {
            switch (field)
            {
                case Field.Condition:
                {
                    if (group.Condition is not null)
                        throw new IOException("Duplicate field: " + field);

                    group.Condition = reader.ReadString();
                    break;
                }
                case Field.MainClass:
                {
                    if (group.MainClass is not null)
                        throw new IOException("Duplicate field: " + field);

                    group.MainClass = reader.ReadString();
                    break;
                }
                case Field.MainModule:
                {
                    if (group.MainModule is not null)
                        throw new IOException("Duplicate field: " + field);

                    group.MainModule = reader.ReadString();
                    break;
                }
                case Field.JvmProperties:
                case Field.AddReads:
                case Field.AddExports:
                case Field.AddOpens:
                case Field.EnableNativeAccess:
                case Field.ExtraJvmOptions:
                {
                    var list = field switch
                    {
                        Field.JvmProperties => group.JvmProperties,
                        Field.AddReads => group.AddReads,
                        Field.AddExports => group.AddExports,
                        Field.AddOpens => group.AddOpens,
                        Field.EnableNativeAccess => group.EnableNativeAccess,
                        Field.ExtraJvmOptions => group.ExtraJvmOptions,
                        _ => throw new IOException("Unexpected field: " + field),
                    };
                    reader.ReadStringList(list);
                    break;
                }
                case Field.ClassPath:
                case Field.ModulePath:
                {
                    var list = field == Field.ModulePath ? group.ModulePath : group.ClassPath;

                    var count = reader.ReadInt32();
                    for (var i = 0; i < count; i++)
                        list.Add(JAppResourceGroupReference.ReadFrom(ref reader));
                    break;
                }
                case Field.SubGroups:
                {
                    var count = reader.ReadInt32();
                    for (var i = 0; i < count; i++)
                        group.Children.Add(ReadFrom(ref reader));
                    break;
                }
                default:
                    throw new IOException("Unexpected field: " + field);
            }
        }

        return group;
    }

    private static Field ReadField(ref LittleEndianReader reader)
    {
        var id = reader.ReadByte();
        if (id < FieldValues.Length)
            return FieldValues[id];
        throw new IOException($"Unknown field: 0x{id:x2}");
    }

    public List<JAppResourceGroupReference> ModulePath { get; } = [];

    public List<JAppResourceGroupReference> ClassPath { get; } = [];

    public List<string> JvmProperties { get; } = [];

    public List<string> AddReads { get; } = [];

    public List<string> AddExports { get; } = [];

    public List<string> AddOpens { get; } = [];

    public List<string> EnableNativeAccess { get; } = [];

    public List<string> ExtraJvmOptions { get; } = [];

    public string? Condition { get; set; }

    public List<JAppConfigGroup> Children { get; } = [];

    public string? MainClass { get; set; }

    public string? MainModule { get; set; }

    public bool CanApply(JvmContext context) =>
        Condition is null || ConditionParser.Parse(Condition).Test(context);

    private static void AddOrReplace(List<JAppResourceGroupReference> target, List<JAppResourceGroupReference> source)
    {
        foreach (var reference in source)
        {
            var index = target.FindIndex(existing => reference.Name.Equals(existing.Name));
            if (index >= 0)
            {
                target[index] = reference;
                continue;
            }

            target.Add(reference);
        }
    }

    private void Resolve(JvmContext context, JAppConfigGroup source)
    {
        if (!source.CanApply(context))
            return;

        AddOrReplace(ModulePath, source.ModulePath);
        AddOrReplace(ClassPath, source.ClassPath);
        JvmProperties.AddRange(source.JvmProperties);
        AddReads.AddRange(source.AddReads);
        AddExports.AddRange(source.AddExports);
        AddOpens.AddRange(source.AddOpens);
        EnableNativeAccess.AddRange(source.EnableNativeAccess);
        ExtraJvmOptions.AddRange(source.ExtraJvmOptions);

        if (source.MainModule is not null)
            MainModule = source.MainModule;

        if (source.MainClass is not null)
            MainClass = source.MainClass;

        foreach (var subConfig in source.Children)
            Resolve(context, subConfig);
    }

    public void Resolve(JvmContext context)
    {
        foreach (var group in Children)
            Resolve(context, group);
    }
}
