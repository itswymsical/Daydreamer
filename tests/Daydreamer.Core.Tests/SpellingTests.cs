using System.Text.RegularExpressions;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Proofing;

namespace Daydreamer.Core.Tests;

/// <summary>Spell checking that understands scripts, checked with a pretend dictionary.</summary>
public class SpellingTests
{
    /// <summary>
    /// A pretend dictionary: it knows a short list of words and flags everything else, plus a word said
    /// twice in a row. It also remembers what it was asked to check.
    /// </summary>
    private sealed class FakeDictionary : ISpellingEngine
    {
        private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "the", "it's", "late", "too", "i", "remember", "train", "is", "here", "chapter", "last",
            "station", "at", "midnight", "sits", "sit", "on", "bench", "don't", "move", "rain", "falls", "in",
        };

        public List<string> Checked { get; } = [];

        public IEnumerable<SpellingIssue> Check(string text)
        {
            Checked.Add(text);
            string? previous = null;
            foreach (Match word in Regex.Matches(text, @"[\p{L}\p{N}'’]+"))
            {
                var range = new TextRange(word.Index, word.Length);
                if (string.Equals(word.Value, previous, StringComparison.OrdinalIgnoreCase))
                    yield return new SpellingIssue(range, SpellingIssueKind.RepeatedWord);
                else if (!Words.Contains(word.Value))
                    yield return new SpellingIssue(range, SpellingIssueKind.Misspelled);
                previous = word.Value;
            }
        }

        public IReadOnlyList<string> Suggest(string word) => word == "trian" ? ["train"] : [];
    }

    private static readonly Character Akira = new() { Name = "AKIRA", Aliases = "The Kid" };
    private static readonly Character Tanaka = new() { Name = "MR. TANAKA" };

    private readonly FakeDictionary _dictionary = new();
    private readonly ScriptParser _parser = new(new CharacterRegistry([Akira, Tanaka]));
    private readonly ScriptSpellChecker _checker;

    public SpellingTests()
    {
        _checker = new ScriptSpellChecker(_dictionary);
        _checker.SetKnownWords([Akira, Tanaka], []);
    }

    /// <summary>The words underlined in a line.</summary>
    private List<string> Underlined(string line) =>
        _checker.CheckLine(line, _parser.ParseLine).Select(issue => issue.Range.Of(line)).ToList();

    [Fact]
    public void Finds_misspelled_words_in_a_description()
    {
        Assert.Equal(["trian"], Underlined("The trian is here."));
    }

    [Fact]
    public void Checks_what_a_character_says_but_not_their_name()
    {
        Assert.Equal(["Waht"], Underlined("AKIRA: Waht? It's late."));
        Assert.Equal(["Waht"], Underlined("AKIRA Waht? It's late.")); // no colon
    }

    [Fact]
    public void Leaves_the_modifier_alone()
    {
        Assert.Empty(Underlined("AKIRA (whsper): Don't move."));
    }

    [Theory]
    [InlineData("SFX: KRRRSHHH bzzt fwoosh")]  // sound effects are spelled however they sound
    [InlineData("// note to slef: fix this")]   // notes are just for you
    [InlineData("PANEL 3")]
    [InlineData("PAGE 12")]
    public void Skips_lines_that_are_not_read(string line)
    {
        Assert.Empty(Underlined(line));
    }

    [Fact]
    public void Hidden_formatting_is_never_checked()
    {
        Assert.Empty(Underlined("{center} **Rain** *falls*."));
        Assert.DoesNotContain(_dictionary.Checked, text => text.Contains('{') || text.Contains('*'));
    }

    [Fact]
    public void Checks_captions_headings_and_list_items()
    {
        Assert.Equal(["remmber"], Underlined("CAPTION: I remmber."));
        Assert.Equal(["Chaptr"], Underlined("# Chaptr 1"));
        Assert.Equal(["benhc"], Underlined("- benhc"));
    }

    [Fact]
    public void Names_and_aliases_are_never_mistakes_even_in_descriptions()
    {
        Assert.Empty(Underlined("Akira sits on a bench."));
        Assert.Empty(Underlined("Akira's train is here."));
        Assert.Empty(Underlined("The Kid sits on a bench."));
        Assert.Empty(Underlined("Tanaka sits on a bench."));
    }

    [Fact]
    public void Words_in_capitals_and_numbers_are_left_alone()
    {
        Assert.Empty(Underlined("NO WAY. The train is 2045."));
    }

    [Fact]
    public void The_storys_own_words_are_known()
    {
        _checker.SetKnownWords([Akira], ["Neo-Kyoto", "gonna"]);
        Assert.Empty(Underlined("A train is gonna sit in Neo-Kyoto."));
    }

    [Fact]
    public void Ignored_words_stop_being_underlined()
    {
        Assert.Equal(["Zzz"], Underlined("Zzz."));
        _checker.Ignore("zzz");
        Assert.Empty(Underlined("Zzz."));
    }

    [Fact]
    public void Repeated_words_are_found()
    {
        var issue = Assert.Single(_checker.CheckLine("The the train.", _parser.ParseLine));
        Assert.Equal(SpellingIssueKind.RepeatedWord, issue.Kind);
        Assert.Equal(new TextRange(4, 3), issue.Range);
    }

    [Fact]
    public void A_line_is_only_checked_once_until_it_changes()
    {
        Underlined("The trian is here.");
        Underlined("The trian is here.");
        Assert.Single(_dictionary.Checked);

        Underlined("The train is here.");
        Assert.Equal(2, _dictionary.Checked.Count);
    }

    [Fact]
    public void Known_words_are_saved_with_the_project()
    {
        var project = new Project();
        bool changed = false;
        project.FormattingChanged += (_, _) => changed = true;
        project.KnownWords.Add("Neo-Kyoto");
        Assert.True(changed);

        string json = System.Text.Json.JsonSerializer.Serialize(project);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Project>(json)!;
        Assert.Equal(["Neo-Kyoto"], loaded.KnownWords);
    }
}
