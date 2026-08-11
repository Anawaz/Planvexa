namespace Planvexa.BuildingBlocks.Formulas;

using System.Globalization;

/// <summary>
/// A hand-parsed/evaluated arithmetic expression (no <c>eval</c>, no reflection, no
/// scripting-engine dependency — AGENTS.md rule 15/16). Originally built for WorkManagement's Formula
/// custom field; the pure grammar lives here (BuildingBlocks, the shared kernel every module may
/// depend on) so the Reporting module can reuse the exact same engine for report-level formulas instead of
/// building a second one, without violating the modular-monolith "no module references another module"
/// rule (AGENTS.md rule 7 / <c>ModuleBoundaryTests</c>) — WorkManagement's <c>FormulaEngine.cs</c> keeps
/// its custom-field-specific pieces (RollupAggregator, CustomFieldDependencyGraph) and now just aliases
/// these shared types.
///
/// Grammar: <c>expr := term (('+'|'-') term)*</c>, <c>term := unary (('*'|'/') unary)*</c>,
/// <c>unary := '-' unary | primary</c>,
/// <c>primary := NUMBER | '{' fieldName '}' | IDENT '(' [expr] ')' | '(' expr ')'</c>.
/// A field reference is <c>{Field Name}</c> (case-insensitive). The <c>IDENT '(' ... ')'</c> form
/// (<see cref="AggregateCall"/>) is the addition for report-level aggregate functions
/// (<c>SUM(hours)</c>, <c>COUNT(tasks)</c>, ...) — see <see cref="AggregateFormulaEvaluator"/>. It was
/// previously unreachable (the tokenizer rejected any bare letter outside <c>{}</c>), so this is purely
/// additive and cannot change how any existing saved Formula field parses.
/// </summary>
public abstract record FormulaNode;

public sealed record NumberLiteral(decimal Value) : FormulaNode;

public sealed record FieldRef(string Name) : FormulaNode;

public sealed record BinaryOp(char Op, FormulaNode Left, FormulaNode Right) : FormulaNode;

public sealed record UnaryMinus(FormulaNode Operand) : FormulaNode;

/// <summary>An aggregate function call, e.g. <c>SUM(hours)</c> or <c>COUNT()</c> — the report-formula
/// extension. <see cref="Argument"/> is null for the no-argument <c>COUNT()</c> form (row count).</summary>
public sealed record AggregateCall(string FunctionName, FormulaNode? Argument) : FormulaNode;

public sealed class FormulaParseException(string message) : Exception(message);

public sealed class FormulaEvaluationException(string message) : Exception(message);

public static class FormulaParser
{
    private enum TokenKind { Number, Field, Ident, Plus, Minus, Star, Slash, LParen, RParen, End }

    private readonly record struct Token(TokenKind Kind, string Text);

    public static FormulaNode Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormulaParseException("A formula expression cannot be empty.");
        }

        var tokens = Tokenize(expression);
        var pos = 0;
        var node = ParseExpr(tokens, ref pos);
        if (tokens[pos].Kind != TokenKind.End)
        {
            throw new FormulaParseException($"Unexpected token '{tokens[pos].Text}' in formula.");
        }

        return node;
    }

    private static List<Token> Tokenize(string expr)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < expr.Length)
        {
            var c = expr[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '+': tokens.Add(new Token(TokenKind.Plus, "+")); i++; continue;
                case '-': tokens.Add(new Token(TokenKind.Minus, "-")); i++; continue;
                case '*': tokens.Add(new Token(TokenKind.Star, "*")); i++; continue;
                case '/': tokens.Add(new Token(TokenKind.Slash, "/")); i++; continue;
                case '(': tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue;
                case ')': tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue;
                case '{':
                    var end = expr.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        throw new FormulaParseException("Unterminated field reference — missing '}'.");
                    }

                    var name = expr[(i + 1)..end].Trim();
                    if (name.Length == 0)
                    {
                        throw new FormulaParseException("Empty field reference '{}'.");
                    }

                    tokens.Add(new Token(TokenKind.Field, name));
                    i = end + 1;
                    continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                var start = i;
                var sawDot = false;
                while (i < expr.Length && (char.IsDigit(expr[i]) || (expr[i] == '.' && !sawDot)))
                {
                    if (expr[i] == '.')
                    {
                        sawDot = true;
                    }

                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, expr[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Ident, expr[start..i]));
                continue;
            }

            throw new FormulaParseException($"Unexpected character '{c}' at position {i} in formula.");
        }

        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private static FormulaNode ParseExpr(List<Token> t, ref int pos)
    {
        var left = ParseTerm(t, ref pos);
        while (t[pos].Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = t[pos].Kind == TokenKind.Plus ? '+' : '-';
            pos++;
            left = new BinaryOp(op, left, ParseTerm(t, ref pos));
        }

        return left;
    }

    private static FormulaNode ParseTerm(List<Token> t, ref int pos)
    {
        var left = ParseUnary(t, ref pos);
        while (t[pos].Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = t[pos].Kind == TokenKind.Star ? '*' : '/';
            pos++;
            left = new BinaryOp(op, left, ParseUnary(t, ref pos));
        }

        return left;
    }

    private static FormulaNode ParseUnary(List<Token> t, ref int pos)
    {
        if (t[pos].Kind == TokenKind.Minus)
        {
            pos++;
            return new UnaryMinus(ParseUnary(t, ref pos));
        }

        return ParsePrimary(t, ref pos);
    }

    private static FormulaNode ParsePrimary(List<Token> t, ref int pos)
    {
        var token = t[pos];
        switch (token.Kind)
        {
            case TokenKind.Number:
                pos++;
                return new NumberLiteral(decimal.Parse(token.Text, CultureInfo.InvariantCulture));
            case TokenKind.Field:
                pos++;
                return new FieldRef(token.Text);
            case TokenKind.Ident:
                pos++;
                if (t[pos].Kind != TokenKind.LParen)
                {
                    throw new FormulaParseException($"Expected '(' after function name '{token.Text}'.");
                }

                pos++;
                FormulaNode? arg = null;
                if (t[pos].Kind != TokenKind.RParen)
                {
                    arg = ParseExpr(t, ref pos);
                }

                if (t[pos].Kind != TokenKind.RParen)
                {
                    throw new FormulaParseException($"Expected ')' to close '{token.Text}('.");
                }

                pos++;
                return new AggregateCall(token.Text, arg);
            case TokenKind.LParen:
                pos++;
                var inner = ParseExpr(t, ref pos);
                if (t[pos].Kind != TokenKind.RParen)
                {
                    throw new FormulaParseException("Expected ')' in formula.");
                }

                pos++;
                return inner;
            default:
                throw new FormulaParseException(
                    token.Kind == TokenKind.End ? "Unexpected end of formula." : $"Unexpected token '{token.Text}' in formula.");
        }
    }
}

/// <summary>Scalar evaluation over a single row's named field values (the original per-task Formula field
/// use case). Throws on <see cref="AggregateCall"/> — aggregation only makes sense across many rows; see
/// <see cref="AggregateFormulaEvaluator"/> for that.</summary>
public static class FormulaEvaluator
{
    public static decimal Evaluate(FormulaNode node, IReadOnlyDictionary<string, decimal> fieldValues) => node switch
    {
        NumberLiteral n => n.Value,
        FieldRef f => fieldValues.TryGetValue(f.Name, out var v)
            ? v
            : throw new FormulaEvaluationException($"Unknown field reference '{{{f.Name}}}'."),
        UnaryMinus u => -Evaluate(u.Operand, fieldValues),
        BinaryOp b => Apply(b.Op, Evaluate(b.Left, fieldValues), Evaluate(b.Right, fieldValues)),
        AggregateCall a => throw new FormulaEvaluationException(
            $"'{a.FunctionName}(...)' is an aggregate function and cannot be used in a single-row formula."),
        _ => throw new FormulaEvaluationException("Unsupported formula expression."),
    };

    /// <summary>Every <c>{FieldName}</c> token referenced anywhere in the tree, case-insensitively deduplicated.</summary>
    public static IReadOnlySet<string> CollectFieldRefs(FormulaNode node)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(node, set);
        return set;
    }

    private static void Collect(FormulaNode node, HashSet<string> set)
    {
        switch (node)
        {
            case FieldRef f:
                set.Add(f.Name);
                break;
            case UnaryMinus u:
                Collect(u.Operand, set);
                break;
            case BinaryOp b:
                Collect(b.Left, set);
                Collect(b.Right, set);
                break;
            case AggregateCall { Argument: { } arg }:
                Collect(arg, set);
                break;
        }
    }

    internal static decimal Apply(char op, decimal left, decimal right) => op switch
    {
        '+' => left + right,
        '-' => left - right,
        '*' => left * right,
        '/' => right == 0 ? throw new FormulaEvaluationException("Division by zero.") : left / right,
        _ => throw new FormulaEvaluationException($"Unsupported operator '{op}'."),
    };
}

/// <summary>
/// Evaluates a formula tree across many rows (e.g. one row per Space in a Portfolio
/// report), resolving each <see cref="AggregateCall"/> (SUM/COUNT/AVERAGE/MIN/MAX, case-insensitive) by
/// evaluating its argument as a plain <see cref="FormulaEvaluator"/> scalar expression against every row
/// and reducing. Non-aggregate nodes (bare <see cref="FieldRef"/>, arithmetic between two aggregate
/// results) compose normally — <c>SUM(hours) / COUNT(tasks)</c> works because both sides reduce to a
/// scalar before the outer '/' applies. A bare <see cref="FieldRef"/> at the top level (no aggregate
/// wrapping it) is rejected: outside a row it is ambiguous which row's value to use.
/// </summary>
public static class AggregateFormulaEvaluator
{
    public static decimal Evaluate(FormulaNode node, IReadOnlyList<IReadOnlyDictionary<string, decimal>> rows) => node switch
    {
        NumberLiteral n => n.Value,
        UnaryMinus u => -Evaluate(u.Operand, rows),
        BinaryOp b => FormulaEvaluator.Apply(b.Op, Evaluate(b.Left, rows), Evaluate(b.Right, rows)),
        AggregateCall a => Aggregate(a, rows),
        FieldRef f => throw new FormulaEvaluationException(
            $"'{{{f.Name}}}' must be wrapped in an aggregate function (e.g. SUM({{{f.Name}}})) in a report formula."),
        _ => throw new FormulaEvaluationException("Unsupported formula expression."),
    };

    private static decimal Aggregate(AggregateCall call, IReadOnlyList<IReadOnlyDictionary<string, decimal>> rows)
    {
        var fn = call.FunctionName.ToUpperInvariant();
        if (fn == "COUNT")
        {
            // COUNT() counts rows; COUNT({field}) counts rows where the field is present (non-zero-arg form).
            return call.Argument is null
                ? rows.Count
                : rows.Count(r => r.ContainsKey(FieldName(call.Argument)));
        }

        var values = rows
            .Select(r => call.Argument is null
                ? throw new FormulaEvaluationException($"'{call.FunctionName}(...)' requires an argument.")
                : FormulaEvaluator.Evaluate(call.Argument, r))
            .ToList();

        if (values.Count == 0)
        {
            return fn == "SUM" ? 0m : throw new FormulaEvaluationException("No rows to aggregate.");
        }

        return fn switch
        {
            "SUM" => values.Sum(),
            "AVERAGE" or "AVG" => values.Average(),
            "MIN" => values.Min(),
            "MAX" => values.Max(),
            _ => throw new FormulaEvaluationException($"Unsupported aggregate function '{call.FunctionName}'."),
        };
    }

    private static string FieldName(FormulaNode node) => node switch
    {
        FieldRef f => f.Name,
        _ => throw new FormulaEvaluationException("COUNT(...) argument must be a field reference."),
    };
}
