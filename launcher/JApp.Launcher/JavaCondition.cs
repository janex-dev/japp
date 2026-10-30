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

using System.Globalization;

/// <summary>Ported from
/// src/main/java/org/glavo/japp/condition/JavaCondition.java: the java(...)
/// term — version is a MINIMUM feature version, os/arch/libc are MatchLists
/// against the checked names.</summary>
public sealed class JavaCondition(int? version, MatchList? os, MatchList? arch, MatchList? libc) : ICondition
{
    public static JavaCondition FromMap(Dictionary<string, string> options)
    {
        options.Remove("version", out var version);
        options.Remove("os", out var os);
        options.Remove("arch", out var arch);
        options.Remove("libc", out var libc);

        if (options.Count > 0)
            throw new ArgumentException("Unknown options: [" + string.Join(", ", options.Keys) + "]");

        return new JavaCondition(
            version is null ? null : int.Parse(version, CultureInfo.InvariantCulture),
            MatchList.Of(os), MatchList.Of(arch), MatchList.Of(libc));
    }

    public bool Test(JvmContext context)
    {
        if (version is { } minimum && context.MajorVersion < minimum)
            return false;

        if (os is not null && !os.Test(context.Os))
            return false;

        if (arch is not null && !arch.Test(context.Arch))
            return false;

        if (libc is not null && !libc.Test(context.LibC))
            return false;

        return true;
    }

    public override string ToString()
    {
        var joiner = new List<string>();
        if (version is { } v)
            joiner.Add("version=" + v.ToString(CultureInfo.InvariantCulture));
        if (os is not null)
            joiner.Add("os=" + os);
        if (arch is not null)
            joiner.Add("arch=" + arch);
        if (libc is not null)
            joiner.Add("libc=" + libc);

        return "java(" + string.Join(", ", joiner) + ")";
    }
}
