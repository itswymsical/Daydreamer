using Daydreamer.Core.Game;
using Daydreamer.Core.Models;
using Daydreamer.Core.Parsing;
using Daydreamer.Core.Proofing;

namespace Daydreamer.Core.Tests;

public class HjsonTests
{
    [Theory]
    [InlineData("Line1.Text: Hi there.[] Yes.\n")]
    [InlineData("A.Text: \"[speakerName:player].\"\n")]
    [InlineData("A.Text: \" leading space\"\n")]
    [InlineData("A.Text: '''\"Splorch!\"'''\n")]
    [InlineData("A.Text: \"\"\n")]
    [InlineData("A.Text: \"12\"\n")]
    [InlineData("A.Text: 12 monkeys\n")]
    [InlineData("A.Text: \"true\"\n")]
    [InlineData("A.Text: truest\n")]
    [InlineData("\"Skip Cutscene.DisplayName\": Skip it\n")]
    public void Values_are_written_back_exactly_as_tModLoader_writes_them(string file)
    {
        Assert.Equal(file, HjsonWriter.Write(HjsonReader.Parse(file), "\n"));
    }

    [Fact]
    public void A_whole_file_keeps_its_layout_and_comments()
    {
        const string file = """
            DialogueLibrary: {
            	Silvian: {
            		# The meeting.
            		Greeting: {
            			Line1: {
            				Text: Well,[] howdy!
            				Choice1.Text: "[speakerName:player]."
            			}
            		}

            		JobOffer.Line1.Text: Hey there.

            		Lie: {
            			Line1.Text: Sure.

            			Line2: {
            				Text: Keep it.
            				Choice1.Text: ...
            			}
            		}

            		A.Line1.Text: One.
            		B.Line1.Text: Two.
            	}
            }

            """;
        string text = file.Replace("\r\n", "\n");
        Assert.Equal(text, HjsonWriter.Write(HjsonReader.Parse(text), "\n"));
    }

    [Fact]
    public void Multiline_strings_lose_their_indent_when_read_and_get_it_back_when_written()
    {
        string file = "Items: {\n\tA: {\n\t\tName: Sword\n\t\tTooltip:\n\t\t\t'''\n\t\t\tFirst\n\t\t\tSecond\n\t\t\t'''\n\t}\n}\n";
        var root = HjsonReader.Parse(file);
        Assert.Equal("First\nSecond", root.FindPath("Items", "A", "Tooltip")!.Value);
        Assert.Equal(file, HjsonWriter.Write(root, "\n"));
    }

    [Fact]
    public void A_broken_file_is_reported_rather_than_guessed_at()
    {
        Assert.Throws<HjsonException>(() => HjsonReader.Parse("A: {\n\tB: hi\n"));
    }
}

public class DialogueEffectTests
{
    [Theory]
    [InlineData("pause 30", "new Pause(30)")]
    [InlineData("shake", "new Shake()")]
    [InlineData("shake 0.6", "new Shake(0.6f)")]
    [InlineData("slow 2", "new Slow(2f)")]
    [InlineData("pitch 1.2", "new Pitch(1.2f)")]
    [InlineData("emphasis 0.25 lift 2", "Inflection.Emphasis(0.25f, lift: 2f)")]
    [InlineData("question", "Inflection.Question()")]
    [InlineData("waver", "Inflection.Waver()")]
    [InlineData("inflect to 0.75 shape settle", "new Inflection(0.75f, shape: InflectionShape.Settle)")]
    [InlineData("inflect to 1.4 steps 4 lift 3", "new Inflection(1.4f, steps: 4, lift: 3f)")]
    [InlineData("sound MenuTick", "new Sound(SoundID.MenuTick)")]
    public void Effects_become_the_mods_code_and_back(string effect, string code)
    {
        Assert.Equal(code, DialogueEffects.ToExpression(effect, out string? problem));
        Assert.Null(problem);
        Assert.Equal(effect, DialogueEffects.FromExpression(code));
    }

    [Theory]
    [InlineData("wobble")]
    [InlineData("shake loud")]
    [InlineData("pitch")]
    public void A_mistake_is_explained(string effect)
    {
        Assert.Null(DialogueEffects.ToExpression(effect, out string? problem));
        Assert.NotNull(problem);
    }
}

public class DialogueBookTests
{
    private static DialogueBook Read(params string[] lines) => DialogueBook.Parse("Silvian", string.Join("\n", lines));

    [Fact]
    public void A_heading_starts_a_conversation_and_every_paragraph_is_a_line()
    {
        var book = Read("# Greeting", "SILVIAN: Hello.", "", "And another thing.", "", "# Other", "Silvian: Bye.");
        Assert.Empty(book.Issues);
        Assert.Equal(["Greeting", "Other"], book.Conversations.Select(c => c.Name));
        Assert.Equal(["Hello.", "And another thing."], book.Conversations[0].Lines.Select(l => l.Text));
        Assert.All(book.Conversations.SelectMany(c => c.Lines), l => Assert.Equal("Silvian", l.Speaker));
    }

    [Fact]
    public void Another_speaker_keeps_talking_until_someone_else_does()
    {
        var book = Read("# Chat", "GUIDE: One.", "Two.", "SILVIAN: Three.");
        Assert.Equal(["Guide", "Guide", "Silvian"], book.Conversations[0].Lines.Select(l => l.Speaker));
    }

    [Fact]
    public void You_and_player_are_the_player()
    {
        Assert.Equal("Player", Read("# Chat", "YOU: Hi.").Conversations[0].Lines[0].Speaker);
    }

    [Fact]
    public void Pauses_reduce_to_anchors_with_their_lengths_in_the_code()
    {
        var line = Read("# A", "Well,[] howdy![30] Wait...[] What?[] Ok").Conversations[0].Lines[0];
        Assert.Equal("Well,[] howdy![] Wait...[] What?[] Ok", line.Text);
        Assert.Equal(["new Pause(14)", "new Pause(30)", "new Pause(28)", "new Pause(18)"], line.Affects);
    }

    [Fact]
    public void Effects_keep_their_words_in_the_text_and_their_look_in_the_code()
    {
        var line = Read("# A", "Word of advice?[] [shake 0.6: Stop that.] Did[n't] you?").Conversations[0].Lines[0];
        Assert.Equal("Word of advice?[] [Stop that.] Did[n't] you?", line.Text);
        Assert.Equal(["new Pause(18)", "new Shake(0.6f)", "Inflection.Emphasis()"], line.Affects);
    }

    [Fact]
    public void The_players_name_is_not_an_anchor()
    {
        var line = Read("# A", "Hey [player]! And [speakerName:Guide].").Conversations[0].Lines[0];
        Assert.Equal("Hey [speakerName:player]! And [speakerName:Guide].", line.Text);
        Assert.Empty(line.Affects);
    }

    [Fact]
    public void An_unknown_effect_is_reported_and_its_words_stay()
    {
        var book = Read("# A", "A [wobble: word].");
        Assert.Equal("A word.", book.Conversations[0].Lines[0].Text);
        Assert.Contains(book.Issues, i => i.Line == 2 && i.Message.Contains("wobble"));
    }

    [Fact]
    public void Bold_and_italic_do_not_reach_the_game()
    {
        Assert.Equal("So bold.", Read("# A", "So **bold**.").Conversations[0].Lines[0].Text);
    }

    [Fact]
    public void Tones_set_the_lines_speed_and_emote()
    {
        var lines = Read(
            "// speed: 1.2",
            "// tone worried: emote 3, speed 0.9",
            "# A",
            "SILVIAN: Normal.",
            "SILVIAN (worried): Oh.",
            "SILVIAN (speed 0.8, emote 2): Slow.",
            "SILVIAN (slow): Slower.",
            "(fast) Quick.").Conversations[0].Lines;
        Assert.Equal([1.2, 0.9, 0.8, 0.85, 1.3], lines.Select(l => l.Speed));
        Assert.Equal([0, 3, 2, 0, 0], lines.Select(l => l.Emote));
        Assert.Equal("Quick.", lines[4].Text);
    }

    [Fact]
    public void Words_in_parentheses_that_are_not_a_tone_stay_words()
    {
        Assert.Equal("(He sighs) Well.", Read("# A", "(He sighs) Well.").Conversations[0].Lines[0].Text);
    }

    [Fact]
    public void An_unknown_tone_on_a_named_line_is_reported()
    {
        Assert.Contains(Read("# A", "SILVIAN (grumpy): No.").Issues, i => i.Message.Contains("grumpy"));
    }

    [Fact]
    public void Choices_go_on_the_line_above_with_where_they_lead()
    {
        var line = Read("# A", "Yours?", "- [player]. -> True", "- ... -> Argie.Letter (dead)", "- Not now. (dead)", "- Pick. (run: Foo.Bar(1))").Conversations[0].Lines[0];
        Assert.Equal("[speakerName:player].", line.Choices[0].Text);
        Assert.Equal("Silvian.True", line.Choices[0].Branch);
        Assert.Equal("Argie.Letter", line.Choices[1].Branch);
        Assert.True(line.Choices[1].Dead);
        Assert.Null(line.Choices[2].Branch);
        Assert.True(line.Choices[2].Dead);
        Assert.Equal("Foo.Bar(1)", line.Choices[3].OnSelected);
    }

    [Fact]
    public void A_response_indented_under_its_choice_becomes_its_own_conversation()
    {
        var book = Read(
            "# Greeting",
            "Who's there?",
            "- Me.",
            "\tOh, you.",
            "\tGood to see you.",
            "\t- Likewise. -> Thanks",
            "\t\tAw.",
            "- Nobody. -> Liar",
            "\tSure.",
            "- ...");
        Assert.Empty(book.Issues);
        Assert.Equal(["Greeting", "Greeting_Choice1", "Thanks", "Liar"], book.Conversations.Select(c => c.Name));

        var greeting = book.Conversations[0];
        Assert.Single(greeting.Lines);
        Assert.Equal(["Silvian.Greeting_Choice1", "Silvian.Liar", null], greeting.Lines[0].Choices.Select(c => c.Branch));
        Assert.Equal(["Oh, you.", "Good to see you."], book.Conversations[1].Lines.Select(l => l.Text));
        Assert.Equal("Silvian.Thanks", book.Conversations[1].Lines[1].Choices[0].Branch);
        Assert.Equal("Aw.", book.Conversations[2].Lines[0].Text);
    }

    [Fact]
    public void Notes_become_comments_or_instructions()
    {
        var conversation = Read(
            "// The meeting.",
            "# A",
            "Here?",
            "- Yes.",
            "- No.",
            "// handin: HerbalForager",
            "// a reminder",
            "Done.",
            "// unlock: BotanyBasics",
            "// complete: Silvian.Give(Main.LocalPlayer)",
            "// complete: Argie.TakeLetter").Conversations[0];
        Assert.Equal(["The meeting."], conversation.Comments);
        Assert.Equal("HerbalForager", conversation.Lines[0].HandIn);
        Assert.Equal(["a reminder"], conversation.Lines[1].Comments);
        Assert.Equal([".UnlockJob(JobID.BotanyBasics)", ".OnComplete(() => Silvian.Give(Main.LocalPlayer))", ".OnComplete(Argie.TakeLetter)"], conversation.Ending);
    }

    [Fact]
    public void A_hand_in_needs_two_choices()
    {
        Assert.Contains(Read("# A", "Here?", "// handin: Job").Issues, i => i.Message.Contains("two choices"));
    }

    [Fact]
    public void The_profile_can_name_the_speaker_and_key()
    {
        var book = DialogueBook.Parse("The Tinker", "// speaker: Goblin Tinkerer\n// key: Tinkerer\n# A\nHi.");
        Assert.Equal("Tinkerer", book.Group);
        Assert.Equal("Goblin Tinkerer", book.Conversations[0].Lines[0].Speaker);
    }

    [Fact]
    public void Auto_pauses_land_after_sentences_on_lines_without_any()
    {
        var lines = Read("// pauses: auto", "# A", "Well, howdy. Mr. Smith is here.", "Mine[] stays. Yes.").Conversations[0].Lines;
        Assert.Equal("Well,[] howdy.[] Mr. Smith is here.", lines[0].Text);
        Assert.Equal("Mine[] stays. Yes.", lines[1].Text);
    }

    [Fact]
    public void Writing_outside_a_conversation_is_reported()
    {
        Assert.Contains(Read("Hello.").Issues, i => i.Line == 1);
    }

    [Fact]
    public void Two_conversations_with_one_name_are_reported()
    {
        Assert.Contains(Read("# A", "Hi.", "# A", "Bye.").Issues, i => i.Message.Contains("two conversations"));
    }
}

public class DialogueMarkupTests
{
    private static ScriptParser Parser() => new(new CharacterRegistry([new Character { Name = "SILVIAN" }])) { DialogueMarkup = true };

    [Fact]
    public void Pauses_and_effect_words_are_hidden_like_other_markup()
    {
        const string text = "SILVIAN: Well,[] [shake 0.6: stop].";
        var hidden = Parser().ParseLine(text).Markup().Select(r => r.Of(text)).ToList();
        Assert.Equal(["[]", " [shake 0.6: ".TrimStart(), "]"], hidden);
    }

    [Fact]
    public void Where_a_choice_leads_shows_but_is_not_proofread()
    {
        const string text = "- Finding my way. -> Lost_Exploring (dead)";
        var line = Parser().ParseLine(text);
        Assert.Equal("-> Lost_Exploring (dead)", line.NotProse().Single().Of(text));
        Assert.Empty(line.Markup());
        Assert.Equal("Finding my way. ", ScriptSpellChecker.RangesToCheck(line).Single().Of(text));
    }

    [Fact]
    public void Headings_and_the_players_name_are_not_proofread()
    {
        const string heading = "# Greeting_True";
        Assert.Empty(ScriptSpellChecker.RangesToCheck(Parser().ParseLine(heading)));

        const string text = "SILVIAN: Hi [player] there";
        var ranges = ScriptSpellChecker.RangesToCheck(Parser().ParseLine(text)).Select(r => r.Of(text));
        Assert.Equal(["Hi ", " there"], ranges);
    }

    [Fact]
    public void Other_stories_keep_their_brackets()
    {
        var parser = new ScriptParser();
        Assert.Empty(parser.ParseLine("Notes [] and [shake: words]").Markup());
    }
}

public sealed class GameExportTests : IDisposable
{
    private const string Silvian = "en-US_Mods.TestMod.hjson";

    private readonly string _root = Directory.CreateTempSubdirectory("daydreamer-game").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Localization(string relativeFolder, string text)
    {
        string folder = Path.Combine(_root, "Localization", relativeFolder);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Silvian);
        File.WriteAllText(path, text);
        return path;
    }

    private void Code(string name, string text)
    {
        string folder = Path.Combine(_root, "Core", "Dialogue", "Definitions");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private GameFolder Folder() => GameFolder.Locate(_root)!;

    [Fact]
    public void The_mod_is_found_from_a_folder_above_it()
    {
        string inner = Path.Combine(_root, "src", "TestMod");
        Directory.CreateDirectory(Path.Combine(inner, "Localization"));
        File.WriteAllText(Path.Combine(inner, "Localization", Silvian), "A: b\n");

        var folder = GameFolder.Locate(_root)!;
        Assert.Equal(inner, folder.Root);
        Assert.Equal("TestMod", folder.ModName);
    }

    [Fact]
    public void A_folder_with_no_localization_is_not_a_mod()
    {
        Assert.Null(GameFolder.Locate(_root));
    }

    [Fact]
    public void A_new_character_gets_their_own_file_in_the_usual_place()
    {
        Localization("", "Items.A: b\n");
        var book = DialogueBook.Parse("Nim", "# Hello\nHi.");
        var plan = GameExporter.Plan(book, Folder());
        GameExporter.Apply(plan);

        string file = Path.Combine(_root, "Localization", "DialogueLibrary", "Nim", Silvian);
        Assert.Equal("DialogueLibrary.Nim.Hello.Line1.Text: Hi.\r\n", File.ReadAllText(file)); // one text: one dotted key, as tModLoader writes it
        Assert.Equal(["Nim.Hello"], plan.Added);
    }

    [Fact]
    public void An_existing_character_stays_in_their_file_and_everything_else_in_it_is_kept()
    {
        string file = Localization("DialogueLibrary", """
            DialogueLibrary: {
            	Sophie: {
            		# Keep this note.
            		Greeting.Line1.Text: Hi.
            		Other.Line1.Text: Untouched.
            	}

            	Sonder.Hello.Line1.Text: Also untouched.
            }

            """);
        var plan = GameExporter.Plan(DialogueBook.Parse("Sophie", "# Greeting\nHello![] Again.\n# New\nBrand new."), Folder());
        GameExporter.Apply(plan);

        string text = File.ReadAllText(file);
        Assert.Contains("# Keep this note.", text);
        Assert.Contains("Greeting.Line1.Text: Hello![] Again.", text);
        Assert.Contains("Other.Line1.Text: Untouched.", text);
        Assert.Contains("New.Line1.Text: Brand new.", text);
        Assert.Contains("Sonder.Hello.Line1.Text: Also untouched.", text);
        Assert.Equal(["Sophie.Other"], plan.NotInDocument);
        Assert.Equal(["Sophie.Greeting"], plan.Updated);
        Assert.Equal(["Sophie.New"], plan.Added);
    }

    [Fact]
    public void Conversations_only_leave_the_game_when_asked_to()
    {
        string file = Localization("DialogueLibrary/Sophie", "DialogueLibrary: {\n\tSophie: {\n\t\tA.Line1.Text: One.\n\t\tB.Line1.Text: Two.\n\t}\n}\n");
        var book = DialogueBook.Parse("Sophie", "# A\nOne.");

        Assert.DoesNotContain(GameExporter.Plan(book, Folder()).Changes, c => c.Path == file);

        GameExporter.Apply(GameExporter.Plan(book, Folder(), removeOthers: true));
        Assert.DoesNotContain("B.Line1", File.ReadAllText(file));
    }

    [Fact]
    public void Exporting_again_changes_nothing()
    {
        Localization("", "Items.A: b\n");
        var book = DialogueBook.Parse("Nim", "# Hello\nHi.[] There.");
        GameExporter.Apply(GameExporter.Plan(book, Folder()));
        Assert.False(GameExporter.Plan(book, Folder()).HasChanges);
    }

    [Fact]
    public void Replaced_files_are_copied_aside_first()
    {
        string file = Localization("DialogueLibrary/Nim", "DialogueLibrary.Nim.A.Line1.Text: Old words.\n");
        string backups = Path.Combine(_root, "backups");
        GameExporter.Apply(GameExporter.Plan(DialogueBook.Parse("Nim", "# A\nNew words."), Folder()), backups);

        Assert.Contains("New words.", File.ReadAllText(file));
        Assert.Contains("Old words.", File.ReadAllText(Directory.GetFiles(backups).Single()));
    }

    [Fact]
    public void The_definition_reads_like_a_hand_written_one()
    {
        var book = DialogueBook.Parse("Nim", """
            # Hello
            NIM (speed 0.8, emote 2): Hi,[16] [shake 0.6: you].
            - Yes. -> Next
            - No. -> Argie.Letter (dead)
            GUIDE: Enough.
            // unlock: FirstJob

            # Next
            Bye.
            """);
        string code = DialogueExporter.ToDefinition(book, "TestMod")!.Replace("\r\n", "\n");

        Assert.Contains("using TestMod.Core.Jobs;", code);
        Assert.Contains("namespace TestMod.Core.Dialogue.Definitions;", code);
        Assert.Contains("sealed class NimWrittenDialogue : ModSystem", code);
        Assert.Contains("""
                    Dialogue.Define("Nim.Hello")
                        .Line(Speaker, emote: 2, speed: 0.8f, [new Pause(16), new Shake(0.6f)])
                            .Choice("Nim.Next")
                            .Choice("Argie.Letter", dead: true)
                        .Line("Guide", emote: 0, speed: 1.1f)
                        .UnlockJob(JobID.FirstJob)
                        .Register();
            """.Replace("\r\n", "\n"), code);
        Assert.Contains(".Line(Speaker, emote: 0, speed: 1.1f)\n", code);
    }

    [Fact]
    public void A_hand_written_conversation_exports_its_words_but_no_definition()
    {
        var book = DialogueBook.Parse("Nim", "# Hello\n// hand-written\nHi.");
        Assert.Null(DialogueExporter.ToDefinition(book, "TestMod"));
        Assert.Contains("Hello.Line1.Text: Hi.", DialogueExporter.ToLocalizationFile(book));
    }

    [Fact]
    public void Taking_over_from_hand_written_code_and_broken_choices_are_noticed()
    {
        Localization("", "Items.A: b\n");
        Code("NimDialogue.cs", """
            class NimDialogue : ModDialogue
            {
                protected override void Register()
                {
                    Dialogue.Define("Nim.Hello").Line("Nim", emote: 0, speed: 1f).Register();
                }
            }
            """);
        var plan = GameExporter.Plan(DialogueBook.Parse("Nim", "# Hello\nHi.\n- Go. -> Nowhere"), Folder());
        Assert.Equal(["Nim.Hello"], plan.TakesOverFrom);
        Assert.Equal(["Hello → Nim.Nowhere"], plan.BrokenBranches);
        Assert.Equal(["Nim"], plan.MissingProfiles);
    }

    [Fact]
    public void A_hand_written_conversation_that_no_longer_fits_its_code_is_noticed()
    {
        Localization("", "Items.A: b\n");
        Code("NimDialogue.cs", """Dialogue.Define("Nim.Hello").Line("Nim", emote: 0, speed: 1f, new Pause(20)).Register();""");
        var plan = GameExporter.Plan(DialogueBook.Parse("Nim", "# Hello\n// hand-written\nNo pauses here."), Folder());
        Assert.Contains(plan.MismatchedAnchors, m => m.Contains("line 1"));
    }

    [Fact]
    public void A_color_gives_a_new_character_a_profile()
    {
        Localization("", "Items.A: b\n");
        var book = DialogueBook.Parse("Nim", "// color: #FF6E50\n# Hello\nHi.");
        var plan = GameExporter.Plan(book, Folder());
        Assert.Empty(plan.MissingProfiles);
        Assert.Contains("""SpeakerRegistry.Register(new SpeakerProfile { Id = "Nim", Color = new(255, 110, 80) });""",
            plan.Changes.Single(c => c.Path.EndsWith(".cs")).Text);
    }

    [Fact]
    public void Importing_joins_the_words_to_the_code_and_exports_back_the_same()
    {
        const string hjson = """
            DialogueLibrary: {
            	Nim: {
            		# The meeting.
            		Greeting: {
            			Line1: {
            				Text: Well,[] [shake it], [speakerName:player]?[]
            				Choice1.Text: "[speakerName:player]."
            				Choice2.Text: ...
            			}
            		}

            		Next.Line1.Text: Bye.
            		Helper.Line1.Text: Hi.[] There.
            		Private.Line1.Text: Hm.
            	}
            }

            """;
        Localization("DialogueLibrary/Nim", hjson);
        Code("NimDialogue.cs", """
            class NimDialogue : ModDialogue
            {
                private const string Speaker = "Nim";
                private const string NextKey = "Nim.Next";

                protected override void Register()
                {
                    // A comment in the chain doesn't matter.
                    Dialogue.Define("Nim.Greeting")
                        .Line(Speaker, emote: 3, speed: 0.8f, [new Pause(14), new Shake(0.6f), Inflection.Emphasis(0.25f, lift: 2f)])
                            .Choice(NextKey)
                            .Choice(NextKey, dead: true)
                        .UnlockJob(JobID.Foo)
                        .Register();

                    Dialogue.Define(NextKey)
                        .Line(Speaker, emote: 0, speed: 1.1f)
                        .OnComplete(Other.Thing)
                        .Register();

                    DefineHelper("Nim.Helper", new Pause(3));

                    Dialogue.Define("Nim.Private")
                        .Line(Speaker, emote: 0, speed: 1.1f)
                        .OnComplete(GivePrivately)
                        .Register();
                }
            }
            """);

        var imported = DialogueImporter.Read(hjson, GameDefinitions.Read(Folder())).Single();
        Assert.Equal("Nim", imported.Title);
        Assert.Contains("NIM (speed 0.8, emote 3): Well,[14] [shake 0.6: shake it], [player]?[emphasis 0.25 lift 2:]", imported.Text);
        Assert.Contains("- [player]. -> Next", imported.Text);
        Assert.Contains("- ... -> Next (dead)", imported.Text);
        Assert.Contains("// unlock: Foo", imported.Text);
        Assert.Contains("// complete: Other.Thing", imported.Text);

        var book = DialogueBook.Parse(imported.Title, imported.Text);
        Assert.Empty(book.Issues);
        Assert.True(book.Conversations.Single(c => c.Name == "Helper").HandWritten);
        Assert.True(book.Conversations.Single(c => c.Name == "Private").HandWritten); // its method is private to the old class
        Assert.False(book.Conversations.Single(c => c.Name == "Greeting").HandWritten);

        Assert.Equal(hjson.Replace("\r\n", "\n"), DialogueExporter.ToLocalizationFile(book).Replace("\r\n", "\n"));

        var greeting = book.Conversations.Single(c => c.Name == "Greeting").Lines[0];
        Assert.Equal(["new Pause(14)", "new Shake(0.6f)", "Inflection.Emphasis(0.25f, lift: 2f)"], greeting.Affects);
        Assert.Equal(0.8, greeting.Speed);
        Assert.Equal(3, greeting.Emote);
    }

    [Fact]
    public void A_file_without_dialogue_imports_nothing()
    {
        Assert.Empty(DialogueImporter.Read("Items.A.DisplayName: Sword\n"));
    }
}
