using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Proofing;

namespace Daydreamer.App.Editor;

/// <summary>
/// Windows' own spell checker, the one Word, Edge and the Windows text boxes use. It works offline,
/// knows every language Windows has a dictionary for, and needs nothing installed. We talk to it through
/// COM, Windows' way of letting programs share objects: the interfaces at the bottom of this file are
/// copied from Windows' spellcheck.h, in the same order, because COM finds each method by its position.
/// </summary>
public sealed class WindowsSpellChecker : ISpellingEngine
{
    private const int MaxSuggestions = 6;
    private readonly ISpellChecker _checker;

    private WindowsSpellChecker(ISpellChecker checker, string language)
    {
        _checker = checker;
        Language = language;
    }

    /// <summary>The dictionary's language, like "en-US".</summary>
    public string Language { get; }

    /// <summary>
    /// The spell checker for Windows' language (or US English if Windows has no dictionary for it).
    /// Null if this version of Windows has no spell checker at all.
    /// </summary>
    public static WindowsSpellChecker? TryCreate(string? language = null)
    {
        try
        {
            var factory = (ISpellCheckerFactory)new SpellCheckerFactory();
            foreach (string? tag in new[] { language, CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name, "en-US" })
            {
                if (!string.IsNullOrEmpty(tag) && factory.IsSupported(tag))
                    return new WindowsSpellChecker(factory.CreateSpellChecker(tag), tag);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // No spell checker on this PC.
        }
        return null;
    }

    public IEnumerable<SpellingIssue> Check(string text)
    {
        var issues = new List<SpellingIssue>();
        var errors = _checker.Check(text);
        while (errors.Next(out var error) == 0) // 0 means "here's another one"; 1 means "that's all"
        {
            var kind = error.CorrectiveAction == CorrectiveAction.Delete ? SpellingIssueKind.RepeatedWord : SpellingIssueKind.Misspelled;
            issues.Add(new SpellingIssue(new TextRange((int)error.StartIndex, (int)error.Length), kind));
        }
        return issues;
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        var suggestions = new List<string>();
        var list = _checker.Suggest(word);
        var one = new string[1];
        while (suggestions.Count < MaxSuggestions && list.Next(1, one, IntPtr.Zero) == 0)
            suggestions.Add(one[0]);
        return suggestions;
    }

    // ── From Windows' spellcheck.h ──────────────────────────────────────────

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    private class SpellCheckerFactory { }

    private enum CorrectiveAction { None = 0, GetSuggestions = 1, Replace = 2, Delete = 3 }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { get; }

        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    // Only the first three methods are used; the rest of ISpellChecker isn't needed, so it isn't listed.
    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig]
        int Next(out ISpellingError value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint StartIndex { get; }
        uint Length { get; }
        CorrectiveAction CorrectiveAction { get; }
        string? Replacement { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
    }
}
