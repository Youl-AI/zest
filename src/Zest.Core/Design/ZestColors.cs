using System.Globalization;
using Zest.Core.Catalog;

namespace Zest.Core.Design;

/// <summary>A 32-bit colour, 0xAARRGGBB, with no UI-framework dependency.</summary>
public readonly record struct Argb(uint Value)
{
    public byte A => (byte)(Value >> 24);
    public byte R => (byte)(Value >> 16);
    public byte G => (byte)(Value >> 8);
    public byte B => (byte)Value;

    /// <summary>Parses "#AARRGGBB" or "#RRGGBB" (opaque).</summary>
    public static Argb Hex(string hex)
    {
        var s = hex.TrimStart('#');
        var v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return s.Length switch
        {
            6 => new Argb(0xFF000000u | v),
            8 => new Argb(v),
            _ => throw new FormatException($"Expected #RRGGBB or #AARRGGBB, got '{hex}'."),
        };
    }
}

/// <summary>One citrus accent: its fill, the text on it, and the ink / wash per theme.</summary>
public sealed record Citrus(string Name, Argb Fill, Argb OnFill, Argb InkLight, Argb WashLight)
{
    /// <summary>Accent used as text or line colour: the darker ink on light, the fill itself on dark.</summary>
    public Argb Ink(bool dark) => dark ? Fill : InkLight;

    /// <summary>Tinted background: a pale solid on light, the fill at about 13 % alpha on dark.</summary>
    public Argb Wash(bool dark) => dark ? new Argb((Fill.Value & 0x00FFFFFFu) | 0x22000000u) : WashLight;
}

/// <summary>Neutral tokens for one theme.</summary>
public sealed record Surface(
    Argb Window, Argb Raised, Argb Sunken, Argb Line, Argb LineStrong, Argb Seg,
    Argb Text, Argb Text2, Argb Text3, Argb Danger, Argb DangerWash);

/// <summary>The single colour source for the wheel, windows and cards.</summary>
public static class ZestColors
{
    public static readonly Surface Light = new(
        Window: Argb.Hex("#FBFBF9"), Raised: Argb.Hex("#FFFFFF"), Sunken: Argb.Hex("#F2F2ED"),
        Line: Argb.Hex("#171D1B16"), LineStrong: Argb.Hex("#2E1D1B16"), Seg: Argb.Hex("#0F1D1B16"),
        Text: Argb.Hex("#1D1B16"), Text2: Argb.Hex("#5F5B52"), Text3: Argb.Hex("#8A867C"),
        Danger: Argb.Hex("#B42D1A"), DangerWash: Argb.Hex("#FBE6E2"));

    public static readonly Surface Dark = new(
        Window: Argb.Hex("#1D1B16"), Raised: Argb.Hex("#27251F"), Sunken: Argb.Hex("#18170F"),
        Line: Argb.Hex("#14FFFFFF"), LineStrong: Argb.Hex("#2EFFFFFF"), Seg: Argb.Hex("#12FFFFFF"),
        Text: Argb.Hex("#F1EEE6"), Text2: Argb.Hex("#ABA79C"), Text3: Argb.Hex("#7F7B71"),
        Danger: Argb.Hex("#FF8A7A"), DangerWash: Argb.Hex("#22FF8A7A"));

    public static readonly Citrus Lemon = new("Lemon",
        Argb.Hex("#FFD43B"), Argb.Hex("#241C00"), Argb.Hex("#A67C00"), Argb.Hex("#FFF5CC"));

    public static readonly Citrus Tangerine = new("Tangerine",
        Argb.Hex("#FF8A3D"), Argb.Hex("#2A1204"), Argb.Hex("#A84300"), Argb.Hex("#FFEBDD"));

    public static readonly Citrus Lime = new("Lime",
        Argb.Hex("#9BE15D"), Argb.Hex("#162A05"), Argb.Hex("#3B7A0E"), Argb.Hex("#E8F6D8"));

    public static Surface For(bool dark) => dark ? Dark : Light;

    public static Citrus For(WheelMode mode) => mode switch
    {
        WheelMode.Convert => Lemon,
        WheelMode.Tools => Tangerine,
        WheelMode.Actions => Lime,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
