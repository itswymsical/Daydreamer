using System.Globalization;

namespace Daydreamer.Core.Export;

/// <summary>A sheet of paper and its margins, in points (1/72 inch, as word processors and PDF measure).</summary>
public sealed record PaperSize(string Name, double Width, double Height, double Margin = 72)
{
    /// <summary>8.5 × 11 inches.</summary>
    public static readonly PaperSize Letter = new("Letter", 612, 792);

    /// <summary>210 × 297 mm.</summary>
    public static readonly PaperSize A4 = new("A4", 595.28, 841.89);

    // The countries that use Letter paper; nearly everywhere else uses A4.
    private static readonly HashSet<string> LetterCountries =
        ["US", "CA", "MX", "PR", "PH", "CL", "CO", "VE", "GT", "CR", "PA", "DO", "SV", "NI", "HN"];

    /// <summary>The usual paper where this computer is set up to be, like Word chooses for a new document.</summary>
    public static PaperSize ForRegion()
    {
        try
        {
            return LetterCountries.Contains(RegionInfo.CurrentRegion.TwoLetterISORegionName) ? Letter : A4;
        }
        catch (ArgumentException)
        {
            return Letter; // an unusual region setting
        }
    }

    /// <summary>The width text can use: the page minus a margin on each side.</summary>
    public double TextWidth => Width - Margin * 2;
}
