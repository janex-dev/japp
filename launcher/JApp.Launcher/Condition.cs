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
///     src/main/java/org/glavo/japp/condition/{Condition,AndCondition,OrCondition,NotCondition}.java:
///     a predicate over the candidate JVM; the java(...) term is the only
///     leaf kind the DSL defines.</summary>
public interface ICondition
{
    bool Test(JvmContext context);
}

public sealed class AndCondition(IReadOnlyList<ICondition> conditions) : ICondition
{
    public bool Test(JvmContext context)
    {
        foreach (var condition in conditions)
        {
            if (!condition.Test(context))
                return false;
        }

        return true;
    }

    public override string ToString() => "and(" + string.Join(", ", conditions) + ")";
}

public sealed class OrCondition(IReadOnlyList<ICondition> conditions) : ICondition
{
    public bool Test(JvmContext context)
    {
        foreach (var condition in conditions)
        {
            if (condition.Test(context))
                return true;
        }

        return false;
    }

    public override string ToString() => "or(" + string.Join(", ", conditions) + ")";
}

public sealed class NotCondition(ICondition original) : ICondition
{
    public bool Test(JvmContext context) => !original.Test(context);

    public override string ToString() => "not(" + original + ")";
}
