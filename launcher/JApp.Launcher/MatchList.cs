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
/// src/main/java/org/glavo/japp/condition/MatchList.java: '|'-separated
/// exact matches with an optional '!' negation prefix.</summary>
public sealed class MatchList
{
    private readonly bool _negative;
    private readonly List<string> _values;

    private MatchList(bool negative, List<string> values)
    {
        _negative = negative;
        _values = values;
    }

    public static MatchList? Of(string? list)
    {
        if (list is null)
            return null;

        var negative = false;
        if (list.StartsWith('!'))
        {
            list = list[1..];
            negative = true;
        }

        return new MatchList(negative, [.. list.Split('|')]);
    }

    public bool Test(string value) => _negative != _values.Contains(value);

    public override string ToString() => (_negative ? "!" : "") + "[" + string.Join(", ", _values) + "]";
}
