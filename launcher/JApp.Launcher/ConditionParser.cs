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
/// src/main/java/org/glavo/japp/condition/ConditionParser.java: a
/// recursive-descent parser for the japp condition DSL (||, &&, !,
/// parentheses and java(version:, os:, arch:, libc:) terms). Note ! binds
/// lower than && — `!a && b` parses as `!(a && b)` — exactly as the Java
/// parser does.</summary>
public sealed class ConditionParser
{
    private static readonly Dictionary<string, Func<Dictionary<string, string>, ICondition>> Factories = new()
    {
        ["java"] = JavaCondition.FromMap,
    };

    public static ICondition Parse(string str)
    {
        var parser = new ConditionParser(str);
        var condition = parser.Expr1();

        if (parser.Peek() != null)
            throw new ArgumentException(str);

        return condition;
    }

    private sealed class Token
    {
        internal static readonly Token And = new("&&", isLiteral: false);
        internal static readonly Token Or = new("||", isLiteral: false);
        internal static readonly Token Not = new("!", isLiteral: false);
        internal static readonly Token Left = new("(", isLiteral: false);
        internal static readonly Token Right = new(")", isLiteral: false);
        internal static readonly Token Colon = new(":", isLiteral: false);
        internal static readonly Token Comma = new(",", isLiteral: false);

        internal readonly string Value;
        internal readonly bool IsLiteral;

        // The Java original carries a precedence number that nothing reads —
        // operator levels are hard-wired in Expr1/Expr2 — so it is not ported.
        internal Token(string value, bool isLiteral)
        {
            Value = value;
            IsLiteral = isLiteral;
        }

        public override string ToString() => IsLiteral ? "Literal[" + Value + "]" : Value;
    }

    private readonly string _source;
    private readonly int _end;
    private int _index;

    private bool _inCondition;

    private Token? _nextToken;

    private ConditionParser(string source)
    {
        _source = source;
        _end = source.Length;
    }

    private static bool IsLiteralStar(char ch) =>
        ch is '*' or '^' or '.' or '<' or '>' or '-' or '/' or '#' or '@' or '~' or '[' or ']'
        || char.IsLetterOrDigit(ch) || ch is '_' or '$';

    private static bool IsLiteralPart(char ch) => ch is '|' or '&' or '!' || IsLiteralStar(ch);

    private void ScanToken()
    {
        if (_nextToken != null || _index >= _end)
            return;

        while (_index < _end && char.IsWhiteSpace(_source[_index]))
            _index++;
        if (_index == _end)
            return;

        var ch = _source[_index];

        if (_inCondition ? IsLiteralPart(ch) : IsLiteralStar(ch))
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(ch);
            _index++;

            while (_index < _end && IsLiteralPart(ch = _source[_index]))
            {
                builder.Append(ch);
                _index++;
            }

            _nextToken = new Token(builder.ToString(), isLiteral: true);
        }
        else if (ch == '(')
        {
            _nextToken = Token.Left;
            _index++;
        }
        else if (ch == ')')
        {
            _nextToken = Token.Right;
            _index++;
        }
        else if (ch == ':')
        {
            _nextToken = Token.Colon;
            _index++;
        }
        else if (ch == ',')
        {
            _nextToken = Token.Comma;
            _index++;
        }
        else if (ch == '!')
        {
            _nextToken = Token.Not;
            _index++;
        }
        else if (ch == '&')
        {
            if (_index == _end - 1 || _source[_index + 1] != '&')
                throw new ArgumentException("Unknown token: " + _index + ":" + _index);

            _nextToken = Token.And;
            _index += 2;
        }
        else if (ch == '|')
        {
            if (_index == _end - 1 || _source[_index + 1] != '|')
                throw new ArgumentException("Unknown token: " + _index + ":" + _index);

            _nextToken = Token.Or;
            _index += 2;
        }
        else if (ch == '\'')
        {
            var endIndex = _source.IndexOf('\'', _index + 1);
            if (endIndex < 0)
                throw new ArgumentException(_source);

            _nextToken = new Token(_source.Substring(_index + 1, endIndex - _index - 1), isLiteral: true);
            _index = endIndex + 1;
        }
        else
        {
            throw new ArgumentException("Unknown token: " + _index + ":" + _index);
        }
    }

    private Token? Peek()
    {
        ScanToken();
        return _nextToken;
    }

    private Token? Next()
    {
        ScanToken();
        var token = _nextToken;
        _nextToken = null;
        return token;
    }

    private ArgumentException Fail() => new(_source);

    private ICondition Expr1()
    {
        var left = Expr2();
        if (Peek() != Token.Or)
            return left;

        var conditions = new List<ICondition> { left };
        while (Peek() == Token.Or)
        {
            Next();
            conditions.Add(Expr2());
        }

        return new OrCondition(conditions);
    }

    private ICondition Expr2()
    {
        var left = Term();
        if (Peek() != Token.And)
            return left;

        var conditions = new List<ICondition> { left };
        while (Peek() == Token.And)
        {
            Next();
            conditions.Add(Term());
        }

        return new AndCondition(conditions);
    }

    private ICondition Term()
    {
        var token = Next();
        if (token == Token.Left)
        {
            var condition = Expr1();

            return Next() != Token.Right ? throw Fail() : condition;
        }

        if (token == Token.Not)
        {
            var condition = Expr1();
            if (condition is null)
                throw Fail();

            return new NotCondition(condition);
        }

        if (token is { IsLiteral: true })
        {
            if (Next() != Token.Left)
                throw Fail();

            _inCondition = true;

            if (!Factories.TryGetValue(token.Value, out var factory))
                throw Fail();

            var map = new Dictionary<string, string>();

            var first = true;
            while (Peek() != Token.Right)
            {
                if (!first && Next() != Token.Comma)
                    throw Fail();
                first = false;

                var key = Next();
                var sep = Next();
                var value = Next();

                if (key is not { IsLiteral: true }
                    || sep != Token.Colon
                    || value is not { IsLiteral: true }
                    || !map.TryAdd(key.Value, value.Value))
                    throw Fail();
            }

            Next();
            _inCondition = false;
            return factory(map);
        }

        throw Fail();
    }
}
