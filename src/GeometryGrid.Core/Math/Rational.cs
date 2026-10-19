using System.Globalization;
using System.Numerics;

namespace GeometryGrid.Core.Math;

public readonly record struct Rational : IComparable<Rational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public static Rational Zero => new(0, 1);
    public static Rational One => new(1, 1);

    public static Rational Parse(string text)
    {
        text = text.Trim();
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        if (exponentIndex >= 0)
        {
            var mantissa = text[..exponentIndex];
            var exponentText = text[(exponentIndex + 1)..];
            if (string.IsNullOrWhiteSpace(mantissa) ||
                !int.TryParse(
                    exponentText,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var exponent) ||
                System.Math.Abs((long)exponent) > 10_000)
                throw new FormatException("科学计数法格式无效。");

            var value = Parse(mantissa);
            var scale = BigInteger.Pow(10, System.Math.Abs(exponent));
            return exponent >= 0
                ? new Rational(value.Numerator * scale, value.Denominator)
                : new Rational(value.Numerator, value.Denominator * scale);
        }

        var slash = text.IndexOf('/');
        if (slash >= 0)
            return new Rational(
                BigInteger.Parse(text[..slash], CultureInfo.InvariantCulture),
                BigInteger.Parse(text[(slash + 1)..], CultureInfo.InvariantCulture));

        var dot = text.IndexOf('.');
        if (dot < 0)
            return new Rational(BigInteger.Parse(text, CultureInfo.InvariantCulture), 1);

        var sign = text.StartsWith('-') ? -1 : 1;
        var unsigned = text.TrimStart('+', '-');
        var parts = unsigned.Split('.');
        var denominator = BigInteger.Pow(10, parts[1].Length);
        var numerator = BigInteger.Parse(parts[0] + parts[1], CultureInfo.InvariantCulture) * sign;
        return new Rational(numerator, denominator);
    }

    public double ToDouble() => (double)Numerator / (double)Denominator;
    public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public override string ToString() => Denominator == 1 ? Numerator.ToString() : $"{Numerator}/{Denominator}";

    public static Rational operator +(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Rational operator -(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Rational operator *(Rational a, Rational b) =>
        new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static Rational operator /(Rational a, Rational b) =>
        b.Numerator.IsZero ? throw new DivideByZeroException() : new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);
    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;
    public static implicit operator Rational(int value) => new(value, 1);
}
