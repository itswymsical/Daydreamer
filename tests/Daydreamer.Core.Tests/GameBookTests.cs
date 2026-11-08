using Daydreamer.Core.Game;

namespace Daydreamer.Core.Tests;

public class BookDocumentTests
{
    private static Dictionary<string, string> Entries(BookDocument book) =>
        book.Root.Flatten().ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Only_a_document_with_a_book_line_at_the_top_is_a_book()
    {
        Assert.True(BookDocument.IsBook("// book: Misc.Diary\r\n# Diary"));
        Assert.True(BookDocument.IsBook("// a note first\n\n// Book: Misc.Diary\n"));
        Assert.False(BookDocument.IsBook("# Greeting\nSILVIAN: Hi.\n// book: Misc.Diary"));
        Assert.False(BookDocument.IsBook("// speed: 1.1\n\n# Greeting"));
        Assert.False(BookDocument.IsBook(""));
    }

    [Fact]
    public void A_new_book_only_needs_a_title_chapters_and_words()
    {
        var book = BookDocument.Parse("Ignored", """
            // book: Lore.Tides

            # The Tides

            A preface.

            ## Low Water
            It was cold.

            It got colder.
            Much colder.

            ## High Water
            It came back.
            """);

        Assert.Equal("Lore.Tides", book.Key);
        Assert.Empty(book.Issues);
        Assert.Equal(["Title", "Opening", "Ch1", "Ch2"], book.Root.Children.Select(c => c.Key));
        Assert.Equal(new Dictionary<string, string>
        {
            ["Title"] = "The Tides",
            ["Opening"] = "A preface.",
            ["Ch1.Title"] = "Low Water",
            ["Ch1.Opening"] = "It was cold.[p]It got colder.\nMuch colder.",
            ["Ch2.Title"] = "High Water",
            ["Ch2.Opening"] = "It came back.",
        }, Entries(book));
    }

    [Fact]
    public void Blocks_are_named_under_their_chapter_unless_they_start_at_the_top()
    {
        var book = BookDocument.Parse("Ignored", """
            // book: Lore.Corridor
            ## Chapter: My Own Corridor
            ### Excerpt
            A line.
            ### /Prose
            The words.
            ### Aside.Body
            Deep.
            """);

        Assert.Equal(new Dictionary<string, string>
        {
            ["Chapter.Title"] = "My Own Corridor",
            ["Chapter.Excerpt"] = "A line.",
            ["Prose"] = "The words.",
            ["Chapter.Aside.Body"] = "Deep.",
        }, Entries(book));
    }

    [Fact]
    public void A_chapter_can_have_no_heading_and_auto_keys_step_over_taken_ones()
    {
        var book = BookDocument.Parse("Ignored", "// book: A.B\n## Ch1:\n### Opening\nHi\n## Ch2: Taken\n## Ch. 3 | Later\n");
        Assert.Equal("Hi", Entries(book)["Ch1.Opening"]);
        Assert.False(Entries(book).ContainsKey("Ch1.Title"));
        Assert.Equal("Ch. 3 | Later", Entries(book)["Ch3.Title"]);
        Assert.Empty(book.Issues);
    }

    [Fact]
    public void Notes_stay_out_of_the_words_and_go_above_the_next_entry()
    {
        var book = BookDocument.Parse("Ignored", "// book: A.B\n// About the title\n# T\n### Body\nOne\n// fix this\nTwo\n### Next\nThree");
        Assert.Equal(["# About the title"], book.Root.Find("Title")!.Comments);
        Assert.Equal("One\nTwo", book.Root.Find("Body")!.Value);
        Assert.Equal(["# fix this"], book.Root.Find("Next")!.Comments);
    }

    [Fact]
    public void Problems_are_reported_with_their_line()
    {
        var doubled = BookDocument.Parse("Ignored", "// book: A.B\n### X\none\n### X\ntwo\n");
        Assert.Equal(4, Assert.Single(doubled.Issues).Line);
        Assert.Equal("two", doubled.Root.Find("X")!.Value);

        Assert.False(BookDocument.Parse("Ignored", "// book: Not A Key\n# T").HasValidKey);
        Assert.Single(BookDocument.Parse("Ignored", "// book: A.B\n##\n").Issues);
        Assert.Equal("My_Book", BookDocument.Parse("My Book", "// book:\n# T").Key);
    }

    [Theory]
    [InlineData("Plain words.")]
    [InlineData("")]
    [InlineData("One[p]Two")]
    [InlineData("One[p]Two[p]Three\nFour")]
    [InlineData("Ends in a break\n")]
    [InlineData("\nStarts with a break")]
    [InlineData("A blank line\n\nthat isn't a paragraph")]
    [InlineData("\n")]
    [InlineData("   ")]
    [InlineData("a\n   \nb")]
    [InlineData("[p]Starts with a token")]
    [InlineData("Ends with a token[p]")]
    [InlineData("Token at a line's end[p]\nnext")]
    [InlineData("Two[p][p]in a row")]
    [InlineData("Spaced [p] out")]
    [InlineData("# Looks like a title")]
    [InlineData("## Looks like a chapter\n### Looks like a block")]
    [InlineData("// Looks like a note")]
    [InlineData("\\ Starts with a backslash")]
    [InlineData("\\")]
    [InlineData("  indented\n\tand tabbed")]
    [InlineData("trailing space \nkept ")]
    [InlineData("One[p]// note after a token")]
    [InlineData("One[p]# heading after a token")]
    [InlineData("Word [gloss:Silhouette] and [c/ff0000:color]")]
    [InlineData("Opening: Second:")]
    [InlineData("\"Quoted\" and '''triple'''")]
    public void Any_text_reads_back_exactly(string value)
    {
        var node = new HjsonNode("Book");
        node.Children.Add(new HjsonNode("Title", "T"));
        node.Children.Add(new HjsonNode("Body", value));

        var book = BookDocument.Parse("Ignored", BookDocument.Write("A.B", node));
        Assert.Equal(value, book.Root.Find("Body")!.Value);
        Assert.Empty(book.Issues);
    }

    [Fact]
    public void A_paragraph_token_becomes_a_blank_line_where_it_can()
    {
        Assert.Equal(["One", "", "Two"], BookDocument.WriteBody("One[p]Two"));
        Assert.Equal(["\\# One", "", "Two"], BookDocument.WriteBody("# One[p]Two"));
        Assert.Equal(["Spaced ", "", " out"], BookDocument.WriteBody("Spaced [p] out"));
    }

    [Theory]
    [InlineData("Title: T\nCh1.Title: One\nOpening: Flat, after a chapter\nCh2.Title: Two\nProse: More\n")]
    [InlineData("Opening: Before the title\nTitle: T\n")]
    [InlineData("Title:\n\t'''\n\tTwo\n\tlines\n\t'''\n")]
    [InlineData("Title: \"\"\nCh1: {\n\tOpening: No heading\n\tTitle: Late heading\n}\n")]
    [InlineData("Title: T\nCh1: {\n\tTitle: \" spaced \"\n\tBody: Words\n}\n")]
    [InlineData("Title: T\nCh1: {\n\tTitle: One\n\tSub: {\n\t\tA: x\n\t\tB: y\n\t}\n}\n")]
    [InlineData("Title: T\nGroup: {\n\tA: x\n\tB: y\n}\nOther: z\n")]
    [InlineData("Title: T\n\"My Chapter\": {\n\tA: x\n\tB: y\n}\n")]
    [InlineData("# About it\nTitle: T\n# First\nCh1: {\n\tTitle: One\n\t# Note\n\tOpening: Words\n}\n")]
    public void Any_shape_of_book_reads_back_in_order(string entries)
    {
        var node = new HjsonNode("Book");
        var parsed = HjsonReader.Parse(entries);
        node.Children.AddRange(parsed.Children);

        var book = BookDocument.Parse("Ignored", BookDocument.Write("A.B", node));
        Assert.Equal(HjsonWriter.Write(parsed, "\n"), HjsonWriter.Write(WithChildren(book.Root), "\n"));
    }

    private static HjsonNode WithChildren(HjsonNode source)
    {
        var root = new HjsonNode("");
        root.Children.AddRange(source.Children);
        return root;
    }

    [Fact]
    public void Importing_finds_books_however_deep_they_are_and_skips_what_isnt_a_book()
    {
        var books = BookImporter.Read("""
            BookLibrary: {
            	Misc: {
            		Diary: {
            			Title: Annie's Diary
            		}
            	}
            	Solo: {
            		Body: Untitled
            	}
            }
            LoreGlossary: {
            	Figures.Term: figures
            }
            """);
        Assert.Equal(["Misc.Diary", "Solo"], books.Select(b => b.Key));
        Assert.Equal(["Annie's Diary", "Solo"], books.Select(b => b.Title));
        Assert.Empty(BookImporter.Read("DialogueLibrary: {\n\tSilvian.Hi.Line1.Text: Hi\n}\n"));
    }
}

public sealed class BookExportTests : IDisposable
{
    private const string Library = """
        BookLibrary: {
        	Misc: {
        		Diary: {
        			Title: Annie's Diary
        			Day1.Title: Day 1
        			# Keep this note.
        			Opening: Dear diary.
        			Day2.Title: Day 2
        		}

        		Other: {
        			Title: Another
        			Body: Untouched.
        		}
        	}
        }

        LoreGlossary: {
        	Figures: {
        		Term: figures
        		Definition: Numbers.
        	}
        }

        LoreUI: {
        	ContentsTab: Contents
        	ContentsTooltip: Turn to the contents
        }

        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "daydreamer-book-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly GameFolder _folder;

    public BookExportTests()
    {
        _file = Path.Combine(_root, "Localization", "BookLibrary", "en-US_Mods.Example.hjson");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, Library.Replace("\r\n", "\n"));
        _folder = GameFolder.Locate(_root)!;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static BookDocument Doc(string text) => BookDocument.Parse("Ignored", text);

    private void DefineBook(string key, string build)
    {
        string path = Path.Combine(_folder.BookDefinitionsFolder, "Hand.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            public sealed class Hand : ModBook
            {
                public override string Key => "{{key}}";
                protected override void Build(BookBuilder book) => {{build}};
            }
            """);
    }

    [Fact]
    public void Changing_a_book_changes_only_its_own_words()
    {
        var plan = BookExporter.Plan(Doc("// book: Misc.Diary\n# Annie's Diary\n## Day1: Day 1\n### /Opening\nDear diary, again.\n## Day2: Day 2\n"), _folder, createDefinition: false);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(_file, change.Path);
        Assert.Equal(Library.Replace("\r\n", "\n").Replace("Opening: Dear diary.", "Opening: Dear diary, again."), change.Text);
        Assert.Equal(["Misc.Diary.Opening"], plan.Updated);
        Assert.Empty(plan.Added);
        Assert.Empty(plan.NotInDocument);
    }

    [Fact]
    public void What_the_document_leaves_out_stays_where_it_was_unless_asked()
    {
        var doc = Doc("// book: Misc.Diary\n# Annie's Diary\n### Added\nNew.\n");

        var kept = BookExporter.Plan(doc, _folder, createDefinition: false);
        Assert.Equal(["Misc.Diary.Day1.Title", "Misc.Diary.Opening", "Misc.Diary.Day2.Title"], kept.NotInDocument);
        Assert.Equal(["Misc.Diary.Added"], kept.Added);
        var book = HjsonReader.Parse(kept.Changes[0].Text).FindPath("BookLibrary", "Misc", "Diary")!;
        Assert.Equal(["Title", "Day1", "Opening", "Day2", "Added"], book.Children.Select(c => c.Key));
        Assert.Equal(["# Keep this note."], book.Find("Opening")!.Comments);

        var removed = BookExporter.Plan(doc, _folder, removeOthers: true, createDefinition: false);
        Assert.Empty(removed.NotInDocument);
        var cleared = HjsonReader.Parse(removed.Changes[0].Text);
        Assert.Equal(["Title", "Added"], cleared.FindPath("BookLibrary", "Misc", "Diary")!.Children.Select(c => c.Key));
        Assert.NotNull(cleared.FindPath("BookLibrary", "Misc", "Other", "Body"));
        Assert.NotNull(cleared.FindPath("LoreGlossary", "Figures", "Term"));
    }

    [Fact]
    public void A_chapter_keeps_the_blocks_the_document_doesnt_have()
    {
        File.WriteAllText(_file, "BookLibrary: {\n\tA.B: {\n\t\tTitle: T\n\n\t\tCh1: {\n\t\t\tTitle: One\n\t\t\tFirst: a\n\t\t\tSecond: b\n\t\t}\n\t}\n}\n");
        var plan = BookExporter.Plan(Doc("// book: A.B\n# T\n## Ch1: One\n### First\nchanged\n"), _folder, createDefinition: false);

        Assert.Equal(["A.B.Ch1.Second"], plan.NotInDocument);
        var chapter = HjsonReader.Parse(plan.Changes[0].Text).FindPath("BookLibrary", "A", "B", "Ch1")!;
        Assert.Equal("changed", chapter.Find("First")!.Value);
        Assert.Equal("b", chapter.Find("Second")!.Value);
    }

    [Fact]
    public void A_new_book_goes_in_the_library_without_disturbing_it()
    {
        var plan = BookExporter.Plan(Doc("// book: Lore.Tides\n# The Tides\n"), _folder, createDefinition: false);

        string text = Assert.Single(plan.Changes).Text;
        var root = HjsonReader.Parse(text);
        Assert.Equal("The Tides", root.FindPath("BookLibrary", "Lore", "Tides", "Title")!.Value);
        Assert.Equal(["BookLibrary", "LoreGlossary", "LoreUI"], root.Children.Select(c => c.Key));
        Assert.Equal("Untouched.", root.FindPath("BookLibrary", "Misc", "Other", "Body")!.Value);
        Assert.Equal(["Lore.Tides.Title"], plan.Added);
    }

    [Fact]
    public void A_mod_with_no_library_yet_gets_the_usual_file()
    {
        File.Delete(_file);
        File.WriteAllText(Path.Combine(_root, "Localization", "en-US_Mods.Example.hjson"), "Mods.Example.Name: E\n");
        var plan = BookExporter.Plan(Doc("// book: Lore.Tides\n# The Tides\n"), _folder, createDefinition: false);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(_file, change.Path);
        Assert.True(change.IsNew);
        Assert.Equal("BookLibrary.Lore.Tides.Title: The Tides\r\n", change.Text);
    }

    [Fact]
    public void A_book_kept_in_another_file_stays_there()
    {
        string elsewhere = Path.Combine(_root, "Localization", "Extra", "en-US_Mods.Example.hjson");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.WriteAllText(elsewhere, "BookLibrary: {\n\tLore.Tides: {\n\t\tTitle: Old\n\t\tBody: b\n\t}\n}\n");

        var plan = BookExporter.Plan(Doc("// book: Lore.Tides\n# New\n### Body\nb\n"), _folder, createDefinition: false);
        Assert.Equal(elsewhere, Assert.Single(plan.Changes).Path);
        Assert.Equal(["Lore.Tides.Title"], plan.Updated);
    }

    [Fact]
    public void Sending_the_same_book_again_changes_nothing()
    {
        var doc = Doc(BookDocument.Write("Misc.Diary", HjsonReader.Parse(Library).FindPath("BookLibrary", "Misc", "Diary")!));
        var plan = BookExporter.Plan(doc, _folder, createDefinition: false);
        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Added);
        Assert.Empty(plan.Updated);
    }

    [Theory]
    [InlineData("Misc", "group of books")]
    [InlineData("Misc.Diary.Extra", "would go inside it")]
    [InlineData("Not A Key", "can't be a book's key")]
    public void A_key_that_would_land_on_other_books_is_refused(string key, string reason)
    {
        var plan = BookExporter.Plan(Doc($"// book: {key}\n# T\n"), _folder);
        Assert.Contains(reason, plan.Refused);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void Applying_writes_the_file_and_backs_up_the_old_one()
    {
        string backups = Path.Combine(_root, "backups");
        GameExporter.Apply(BookExporter.Plan(Doc("// book: Misc.Other\n# Renamed\n### Body\nUntouched.\n"), _folder, createDefinition: false), backups);

        Assert.Contains("Title: Renamed", File.ReadAllText(_file));
        Assert.Contains("Title: Another", File.ReadAllText(Assert.Single(Directory.GetFiles(backups))));
    }

    [Fact]
    public void A_book_the_mod_doesnt_define_gets_a_generated_definition()
    {
        var plan = BookExporter.Plan(Doc("""
            // book: Lore.Tides
            # The Tides
            ### ContentsHeader
            Contents
            ## Ch1: Low Water
            ### Opening
            It was cold.
            ### Second
            It got colder.
            ### Excerpt
            A line.
            ### AsideTitle
            A note
            ### AsideBody
            Noted.
            """), _folder);

        Assert.True(plan.NewDefinition);
        var definition = plan.Changes.Single(c => c.Path.EndsWith(".cs", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(_folder.BookDefinitionsFolder, "Written", "LoreTidesBook.cs"), definition.Path);
        Assert.Contains("namespace Example.Core.Books.Definitions.Written;", definition.Text);
        Assert.Contains("public sealed class LoreTidesBook : ModBook", definition.Text);
        Assert.Contains("Key => \"Lore.Tides\"", definition.Text);
        Assert.Contains(".Contents(\"ContentsHeader\")", definition.Text);
        Assert.Contains(".Chapter(\"Ch1\")", definition.Text);
        Assert.Contains(".Prose(\"Ch1.Opening\", BlockStyle.Body with { DropCap = true, Align = TextAlign.Justify })", definition.Text);
        Assert.Contains(".Prose(\"Ch1.Second\", BlockStyle.Body with { Align = TextAlign.Justify })", definition.Text);
        Assert.Contains(".Quote(\"Ch1.Excerpt\")", definition.Text);
        Assert.Contains(".Panel(panel => panel.Heading(\"Ch1.AsideTitle\"", definition.Text);
        Assert.Contains(".Prose(\"Ch1.AsideBody\"", definition.Text);
        Assert.DoesNotContain("Ch1.Title", definition.Text);
    }

    [Fact]
    public void A_hand_written_book_keeps_its_code_and_warns_about_words_it_wont_show()
    {
        DefineBook("Lore.Tides", """book.Heading("Title").Chapter("Chapter").Prose("Prose")""");
        var plan = BookExporter.Plan(Doc("// book: Lore.Tides\n# T\n## Chapter: One\n### /Prose\nShown.\n### /Stray\nNot shown.\n"), _folder);

        Assert.False(plan.NewDefinition);
        Assert.DoesNotContain(plan.Changes, c => c.Path.EndsWith(".cs", StringComparison.Ordinal));
        Assert.Equal(["Lore.Tides.Stray"], plan.UnreadWords);
    }

    [Fact]
    public void A_definition_written_by_hand_takes_over_from_a_generated_one()
    {
        var doc = Doc("// book: Lore.Tides\n# T\n");
        GameExporter.Apply(BookExporter.Plan(doc, _folder));
        Assert.True(File.Exists(_folder.BookDefinitionFile(doc)));

        DefineBook("Lore.Tides", """book.Heading("Title")""");
        Assert.Equal(_folder.BookDefinitionFile(doc), BookExporter.Plan(doc, _folder).DefinitionToRemove);
    }

    [Fact]
    public void Definitions_are_followed_through_a_helper_and_given_up_on_past_that()
    {
        var definitions = new BookDefinitions();
        definitions.ReadFile("""
            internal static class Series { public static string Name => "x"; }

            public sealed class First : ModBook
            {
                public override string Key => "Series.First";
                protected override void Build(BookBuilder book)
                {
                    book.Heading("Title").Contents("ContentsHeader");
                    // .Prose("Commented")
                    Chapter(book, "Ch1");
                    Chapter(book, "Ch2");
                }
                private static void Chapter(BookBuilder builder, string chapter) => builder
                    .Chapter(chapter)
                    .Prose($"{chapter}.Opening", BlockStyle.Body with { DropCap = true })
                    .Quote($"{chapter}.Excerpt");
            }

            public sealed class Looped : ModBook
            {
                public override string Key => "Series.Looped";
                protected override void Build(BookBuilder book)
                {
                    for (int i = 0; i < 3; i++) book.Prose($"Ch{i}.Opening");
                }
            }
            """);

        Assert.True(definitions.Defines("series.first"));
        Assert.False(definitions.Defines("Series"));
        Assert.True(definitions.Reads("Series.First", "Title"));
        Assert.True(definitions.Reads("Series.First", "ContentsHeader"));
        Assert.True(definitions.Reads("Series.First", "Ch2.Title"));
        Assert.True(definitions.Reads("Series.First", "Ch2.Excerpt"));
        Assert.False(definitions.Reads("Series.First", "Ch3.Opening"));
        Assert.False(definitions.Reads("Series.First", "Commented"));
        Assert.True(definitions.Reads("Series.Looped", "Anything"));
    }
}

/// <summary>Checks against the real mod when it's on this computer. It's only ever read: exports go to a temp copy.</summary>
public sealed class RealBookTests : IDisposable
{
    private static readonly string ModFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "Terraria", "tModLoader", "ModSources", "TrelamiumIII", "src", "TrelamiumIII");

    private static string RealFile => Path.Combine(ModFolder, "Localization", "BookLibrary", "en-US_Mods.TrelamiumIII.hjson");

    private readonly string _copy = Path.Combine(Path.GetTempPath(), "daydreamer-realbook-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_copy)) Directory.Delete(_copy, recursive: true);
    }

    private GameFolder CopyOfMod()
    {
        string file = Path.Combine(_copy, "Localization", "BookLibrary", "en-US_Mods.TrelamiumIII.hjson");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.Copy(RealFile, file);

        string definitions = Path.Combine(_copy, "Core", "Books", "Definitions");
        Directory.CreateDirectory(definitions);
        foreach (string source in Directory.GetFiles(Path.Combine(ModFolder, "Core", "Books", "Definitions"), "*.cs"))
            File.Copy(source, Path.Combine(definitions, Path.GetFileName(source)));
        return GameFolder.Locate(_copy)!;
    }

    [Fact]
    public void Every_real_book_reads_back_as_the_same_text()
    {
        if (!File.Exists(RealFile)) return;
        string text = File.ReadAllText(RealFile);
        var library = HjsonReader.Parse(text).Find(BookExporter.LibraryKey)!;

        var books = BookImporter.Read(text);
        Assert.Equal(
            ["Lore.CorridorOfDarkness", "IlluminanceSeries.IlluminanceI", "IlluminanceSeries.IlluminanceII"],
            books.Select(b => b.Key));

        foreach (var imported in books)
        {
            Assert.True(BookDocument.IsBook(imported.Text));
            var book = BookDocument.Parse(imported.Title, imported.Text);
            Assert.Equal(imported.Key, book.Key);
            Assert.Empty(book.Issues);

            var original = library.FindPath(imported.Key.Split('.'))!.Flatten().ToList();
            var back = book.Root.Flatten().ToList();
            Assert.Equal(original, back); // the same keys, the same words, the same order
        }
    }

    [Fact]
    public void Sending_every_real_book_back_leaves_the_file_byte_for_byte()
    {
        if (!File.Exists(RealFile)) return;
        var folder = CopyOfMod();
        string file = folder.BookLocalizationFile;
        string before = File.ReadAllText(file);

        foreach (var imported in BookImporter.Read(before))
        {
            var plan = BookExporter.Plan(BookDocument.Parse(imported.Title, imported.Text), folder, createDefinition: false);
            Assert.Null(plan.Refused);
            Assert.Empty(plan.Added);
            Assert.Empty(plan.Updated);
            Assert.Empty(plan.NotInDocument);
            Assert.Empty(plan.Changes);
        }
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public void A_changed_real_book_changes_only_its_own_lines()
    {
        if (!File.Exists(RealFile)) return;
        var folder = CopyOfMod();
        string file = folder.BookLocalizationFile;
        string before = File.ReadAllText(file);

        var imported = BookImporter.Read(before).Single(b => b.Key == "CorridorOfDarkness");
        var book = BookDocument.Parse(imported.Title, imported.Text.Replace("# Corridor of Darkness", "# The Corridor"));
        GameExporter.Apply(BookExporter.Plan(book, folder));

        Assert.Equal(before.Replace("Title: Corridor of Darkness", "Title: The Corridor"), File.ReadAllText(file));
    }
}
