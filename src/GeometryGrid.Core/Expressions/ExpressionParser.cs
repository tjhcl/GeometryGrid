using System.Numerics;
using GeometryGrid.Core.Math;

namespace GeometryGrid.Core.Expressions;

public readonly record struct NumericValue(Rational? Exact, double Approximate)
{
    public static NumericValue From(Rational value) => new(value, value.ToDouble());
    public static NumericValue From(double value) => new(null, value);
    public static implicit operator NumericValue(Rational value) => From(value);
}

public sealed class ParsedExpression
{
    private readonly ExpressionParser.Node _root;
    public string Source { get; }
    public IReadOnlySet<string> Dependencies { get; }

    internal ParsedExpression(string source, ExpressionParser.Node root)
    {
        Source = source;
        _root = root;
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        root.CollectDependencies(dependencies);
        Dependencies = dependencies;
    }

    public NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters) =>
        _root.Evaluate(parameters);

    public NumericValue Evaluate(IReadOnlyDictionary<string, Rational> parameters) =>
        _root.Evaluate(parameters.ToDictionary(
            entry => entry.Key,
            entry => NumericValue.From(entry.Value),
            StringComparer.Ordinal));
}

public static class ExpressionParser
{
    public static ParsedExpression Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ExpressionException("表达式不能为空。", 0);
        var parser = new Parser(source);
        var root = parser.ParseExpression();
        parser.RequireEnd();
        return new ParsedExpression(source, root);
    }

    private sealed class Parser(string source)
    {
        private readonly Lexer _lexer = new(source);
        private Token _current = new(TokenKind.None, "", 0);
        private bool _started;

        private Token Current
        {
            get
            {
                if (!_started)
                {
                    _current = _lexer.Next();
                    _started = true;
                }
                return _current;
            }
        }

        private void Advance() => _current = _lexer.Next();

        public Node ParseExpression()
        {
            var node = ParseTerm();
            while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Current.Kind;
                Advance();
                node = new BinaryNode(op, node, ParseTerm());
            }
            return node;
        }

        private Node ParseTerm()
        {
            var node = ParseUnary();
            while (Current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = Current.Kind;
                Advance();
                node = new BinaryNode(op, node, ParseUnary());
            }
            return node;
        }

        private Node ParseUnary()
        {
            if (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Current.Kind;
                Advance();
                return new UnaryNode(op, ParseUnary());
            }
            return ParsePower();
        }

        private Node ParsePower()
        {
            var node = ParsePrimary();
            if (Current.Kind == TokenKind.Caret)
            {
                Advance();
                node = new PowerNode(node, ParseUnary());
            }
            return node;
        }

        private Node ParsePrimary()
        {
            var token = Current;
            if (token.Kind == TokenKind.Number)
            {
                Advance();
                return new NumberNode(Rational.Parse(token.Text));
            }

            if (token.Kind == TokenKind.Identifier)
            {
                Advance();
                var name = token.Text.ToLowerInvariant();
                if (Current.Kind == TokenKind.LeftParen)
                {
                    Advance();
                    var arguments = new List<Node>();
                    if (Current.Kind != TokenKind.RightParen)
                    {
                        while (true)
                        {
                            arguments.Add(ParseExpression());
                            if (Current.Kind != TokenKind.Comma) break;
                            Advance();
                        }
                    }
                    Require(TokenKind.RightParen, $"函数 {token.Text} 缺少右括号。");
                    return new FunctionNode(name, arguments, token.Position);
                }
                if (name is "pi" or "e" or "tau")
                    return new ConstantNode(name);
                return new ParameterNode(token.Text);
            }

            if (token.Kind == TokenKind.LeftParen)
            {
                Advance();
                var node = ParseExpression();
                Require(TokenKind.RightParen, "缺少右括号。");
                return node;
            }

            throw new ExpressionException($"不允许的标记“{token.Text}”。", token.Position);
        }

        private void Require(TokenKind kind, string message)
        {
            if (Current.Kind != kind) throw new ExpressionException(message, Current.Position);
            Advance();
        }

        public void RequireEnd()
        {
            if (Current.Kind != TokenKind.End)
                throw new ExpressionException($"表达式尾部存在不允许的内容“{Current.Text}”。", Current.Position);
        }
    }

    private sealed class Lexer(string source)
    {
        private int _position;

        public Token Next()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++;
            if (_position >= source.Length) return new Token(TokenKind.End, "", _position);
            var start = _position;
            var ch = source[_position++];
            if (char.IsDigit(ch) || ch == '.')
            {
                while (_position < source.Length &&
                       (char.IsDigit(source[_position]) || source[_position] == '.'))
                    _position++;
                if (_position < source.Length &&
                    (source[_position] == 'e' || source[_position] == 'E'))
                {
                    var exponentPosition = _position++;
                    if (_position < source.Length &&
                        (source[_position] == '+' || source[_position] == '-'))
                        _position++;
                    var exponentDigits = _position;
                    while (_position < source.Length && char.IsDigit(source[_position]))
                        _position++;
                    if (_position == exponentDigits)
                        throw new ExpressionException("科学计数法的指数无效。", exponentPosition);
                }
                var text = source[start.._position];
                if (text.Count(c => c == '.') > 1 || text == ".")
                    throw new ExpressionException("数字格式无效。", start);
                return new Token(TokenKind.Number, text, start);
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                while (_position < source.Length &&
                       (char.IsLetterOrDigit(source[_position]) || source[_position] == '_'))
                    _position++;
                return new Token(TokenKind.Identifier, source[start.._position], start);
            }

            return ch switch
            {
                '+' => new Token(TokenKind.Plus, "+", start),
                '-' => new Token(TokenKind.Minus, "-", start),
                '*' => new Token(TokenKind.Star, "*", start),
                '/' => new Token(TokenKind.Slash, "/", start),
                '^' => new Token(TokenKind.Caret, "^", start),
                ',' => new Token(TokenKind.Comma, ",", start),
                '(' => new Token(TokenKind.LeftParen, "(", start),
                ')' => new Token(TokenKind.RightParen, ")", start),
                _ => throw new ExpressionException($"字符“{ch}”不在表达式白名单中。", start)
            };
        }
    }

    internal abstract class Node
    {
        public abstract NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters);
        public virtual void CollectDependencies(HashSet<string> dependencies) { }
    }

    private sealed class NumberNode(Rational value) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters) =>
            NumericValue.From(value);
    }

    private sealed class ConstantNode(string name) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters) =>
            NumericValue.From(name switch
            {
                "pi" => System.Math.PI,
                "e" => System.Math.E,
                "tau" => System.Math.Tau,
                _ => throw new InvalidOperationException()
            });
    }

    private sealed class ParameterNode(string name) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters) =>
            parameters.TryGetValue(name, out var value)
                ? value
                : throw new ExpressionException($"缺少参数“{name}”。", 0);

        public override void CollectDependencies(HashSet<string> dependencies) => dependencies.Add(name);
    }

    private sealed class UnaryNode(TokenKind op, Node child) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters)
        {
            var value = child.Evaluate(parameters);
            return op == TokenKind.Minus
                ? new NumericValue(value.Exact is null ? null : -value.Exact.Value, -value.Approximate)
                : value;
        }

        public override void CollectDependencies(HashSet<string> dependencies) =>
            child.CollectDependencies(dependencies);
    }

    private sealed class BinaryNode(TokenKind op, Node left, Node right) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters)
        {
            var a = left.Evaluate(parameters);
            var b = right.Evaluate(parameters);
            var approximate = op switch
            {
                TokenKind.Plus => a.Approximate + b.Approximate,
                TokenKind.Minus => a.Approximate - b.Approximate,
                TokenKind.Star => a.Approximate * b.Approximate,
                TokenKind.Slash => a.Approximate / b.Approximate,
                _ => throw new InvalidOperationException()
            };
            Rational? exact = null;
            if (a.Exact is not null && b.Exact is not null)
            {
                exact = op switch
                {
                    TokenKind.Plus => a.Exact.Value + b.Exact.Value,
                    TokenKind.Minus => a.Exact.Value - b.Exact.Value,
                    TokenKind.Star => a.Exact.Value * b.Exact.Value,
                    TokenKind.Slash => a.Exact.Value / b.Exact.Value,
                    _ => null
                };
            }
            return Finite(new NumericValue(exact, approximate));
        }

        public override void CollectDependencies(HashSet<string> dependencies)
        {
            left.CollectDependencies(dependencies);
            right.CollectDependencies(dependencies);
        }
    }

    private sealed class PowerNode(Node left, Node right) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters)
        {
            var basis = left.Evaluate(parameters);
            var exponent = right.Evaluate(parameters);
            var approximate = System.Math.Pow(basis.Approximate, exponent.Approximate);
            Rational? exact = null;
            if (basis.Exact is { } rational &&
                exponent.Exact is { Denominator: var denominator, Numerator: var numerator } &&
                denominator == BigInteger.One &&
                numerator >= int.MinValue &&
                numerator <= int.MaxValue)
            {
                var power = (int)numerator;
                if (power >= 0)
                    exact = new Rational(
                        BigInteger.Pow(rational.Numerator, power),
                        BigInteger.Pow(rational.Denominator, power));
                else if (!rational.Numerator.IsZero)
                    exact = new Rational(
                        BigInteger.Pow(rational.Denominator, -power),
                        BigInteger.Pow(rational.Numerator, -power));
            }
            return Finite(new NumericValue(exact, approximate));
        }

        public override void CollectDependencies(HashSet<string> dependencies)
        {
            left.CollectDependencies(dependencies);
            right.CollectDependencies(dependencies);
        }
    }

    private sealed class FunctionNode(string name, IReadOnlyList<Node> arguments, int position) : Node
    {
        public override NumericValue Evaluate(IReadOnlyDictionary<string, NumericValue> parameters)
        {
            var values = arguments.Select(argument => argument.Evaluate(parameters)).ToArray();
            NumericValue result = name switch
            {
                "sqrt" => Sqrt(RequireOne(values)),
                "abs" => Abs(RequireOne(values)),
                "sin" => NumericValue.From(System.Math.Sin(RequireOne(values).Approximate)),
                "cos" => NumericValue.From(System.Math.Cos(RequireOne(values).Approximate)),
                "tan" => NumericValue.From(System.Math.Tan(RequireOne(values).Approximate)),
                "asin" => NumericValue.From(System.Math.Asin(RequireOne(values).Approximate)),
                "acos" => NumericValue.From(System.Math.Acos(RequireOne(values).Approximate)),
                "atan" => NumericValue.From(System.Math.Atan(RequireOne(values).Approximate)),
                "sind" => NumericValue.From(System.Math.Sin(DegreesToRadians(RequireOne(values).Approximate))),
                "cosd" => NumericValue.From(System.Math.Cos(DegreesToRadians(RequireOne(values).Approximate))),
                "tand" => NumericValue.From(System.Math.Tan(DegreesToRadians(RequireOne(values).Approximate))),
                "asind" => NumericValue.From(RadiansToDegrees(System.Math.Asin(RequireOne(values).Approximate))),
                "acosd" => NumericValue.From(RadiansToDegrees(System.Math.Acos(RequireOne(values).Approximate))),
                "atand" => NumericValue.From(RadiansToDegrees(System.Math.Atan(RequireOne(values).Approximate))),
                "atan2" => NumericValue.From(System.Math.Atan2(RequireTwo(values)[0].Approximate, values[1].Approximate)),
                "atan2d" => NumericValue.From(RadiansToDegrees(System.Math.Atan2(RequireTwo(values)[0].Approximate, values[1].Approximate))),
                "exp" => NumericValue.From(System.Math.Exp(RequireOne(values).Approximate)),
                "ln" or "log" => NumericValue.From(System.Math.Log(RequireOne(values).Approximate)),
                "log10" => NumericValue.From(System.Math.Log10(RequireOne(values).Approximate)),
                "pow" => NumericValue.From(System.Math.Pow(RequireTwo(values)[0].Approximate, values[1].Approximate)),
                "min" => NumericValue.From(RequireTwo(values).Min(value => value.Approximate)),
                "max" => NumericValue.From(RequireTwo(values).Max(value => value.Approximate)),
                _ => throw new ExpressionException($"未知函数“{name}”。", position)
            };
            return Finite(result);
        }

        public override void CollectDependencies(HashSet<string> dependencies)
        {
            foreach (var argument in arguments) argument.CollectDependencies(dependencies);
        }

        private NumericValue RequireOne(IReadOnlyList<NumericValue> values)
        {
            if (values.Count != 1)
                throw new ExpressionException($"函数 {name} 需要 1 个参数。", position);
            return values[0];
        }

        private IReadOnlyList<NumericValue> RequireTwo(IReadOnlyList<NumericValue> values)
        {
            if (values.Count != 2)
                throw new ExpressionException($"函数 {name} 需要 2 个参数。", position);
            return values;
        }

        private static NumericValue Abs(NumericValue value) =>
            new(
                value.Exact is { } exact
                    ? (exact.Numerator.Sign < 0 ? -exact : exact)
                    : null,
                System.Math.Abs(value.Approximate));

        private static NumericValue Sqrt(NumericValue value)
        {
            if (value.Approximate < 0)
                throw new ExpressionException("sqrt 的参数不能为负数。", 0);
            Rational? exact = null;
            if (value.Exact is { } rational)
            {
                var n = IntegerSqrt(rational.Numerator);
                var d = IntegerSqrt(rational.Denominator);
                if (n * n == rational.Numerator && d * d == rational.Denominator)
                    exact = new Rational(n, d);
            }
            return new NumericValue(exact, System.Math.Sqrt(value.Approximate));
        }

        private static double DegreesToRadians(double value) => value * System.Math.PI / 180d;
        private static double RadiansToDegrees(double value) => value * 180d / System.Math.PI;

        private static BigInteger IntegerSqrt(BigInteger value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value <= 1) return value;
            var x = BigInteger.One << ((int)BigInteger.Log(value, 2) / 2 + 1);
            while (true)
            {
                var next = (x + value / x) >> 1;
                if (next >= x) return x;
                x = next;
            }
        }
    }

    private static NumericValue Finite(NumericValue value)
    {
        if (!double.IsFinite(value.Approximate))
            throw new ExpressionException("表达式的结果不是有限实数，请检查除零、函数定义域或数值范围。", 0);
        return value;
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);
    private enum TokenKind
    {
        None, End, Number, Identifier, Plus, Minus, Star, Slash, Caret, Comma, LeftParen, RightParen
    }
}

public sealed class ExpressionException(string message, int position)
    : Exception($"{message}（位置 {position + 1}）")
{
    public int Position { get; } = position;
}
