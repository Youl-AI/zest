using System.Globalization;

namespace Zest.Core.Tools;

public enum SizeUnit { KB = 1_000, MB = 1_000_000 }

/// <summary>Reads the compress window's target-size field.</summary>
public static class TargetSize
{
    const string NotANumber = "목표 크기를 숫자로 입력하세요.";
    const double MaxBytes = 1e15;

    /// <summary>
    /// "500" KB → 500,000 bytes; "1,5" or "1.5" MB → 1,500,000 (a comma is a decimal point, like the original; no
    /// thousands separators). Anything that is not a positive plain number throws <see cref="ZestException"/>.
    /// </summary>
    public static long Parse(string text, SizeUnit unit)
    {
        var s = text.Trim().Replace(',', '.');
        if (!double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value <= 0)
            throw new ZestException(NotANumber);
        var bytes = value * (double)unit;
        if (bytes >= MaxBytes) throw new ZestException(NotANumber);
        return Math.Max(1, (long)Math.Round(bytes));
    }
}
