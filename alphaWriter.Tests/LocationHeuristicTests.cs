using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class LocationHeuristicTests
{
    private readonly LocationHeuristicService _service = new();

    // ── Token helpers ────────────────────────────────────────────────────────

    private static (string Value, string Pos) Pron(string v) => (v, "PRON");
    private static (string Value, string Pos) Verb(string v) => (v, "VERB");
    private static (string Value, string Pos) Adp(string v) => (v, "ADP");
    private static (string Value, string Pos) Det(string v) => (v, "DET");
    private static (string Value, string Pos) Propn(string v) => (v, "PROPN");
    private static (string Value, string Pos) Noun(string v) => (v, "NOUN");

    private static IReadOnlySet<string> NoChars => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlySet<string> CharSet(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    // ── Phase A: spatial preposition patterns ────────────────────────────────

    [Fact]
    public void Detects_basic_spatial_pattern()
    {
        // "She traveled to Eldoria"
        var sentences = new[] { "She traveled to Eldoria" };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Pron("She"), Verb("traveled"), Adp("to"), Propn("Eldoria") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.Single(result);
        Assert.Equal("Eldoria", result[0].Name);
        Assert.Equal(1, result[0].Count);
    }

    [Fact]
    public void Strips_determiner_from_location_name()
    {
        // "They walked into the Shadowlands"
        var sentences = new[] { "They walked into the Shadowlands" };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Pron("They"), Verb("walked"), Adp("into"), Det("the"), Propn("Shadowlands") }
        };

        // "They" is a pronoun but this is the only sentence, so 100% co-occurrence.
        // However, let's test with no pronoun filter issue by using a non-pronoun subject.
        // Actually let's test the DET stripping with a non-pronoun sentence:
        var sentences2 = new[] { "The army walked into the Shadowlands" };
        var tagged2 = new IReadOnlyList<(string, string)>[]
        {
            new[] { Det("The"), Noun("army"), Verb("walked"), Adp("into"), Det("the"), Propn("Shadowlands") }
        };

        var result = _service.FindLocationCandidates(sentences2, tagged2, NoChars);

        Assert.Single(result);
        Assert.Equal("Shadowlands", result[0].Name);
    }

    [Fact]
    public void Detects_multi_word_proper_noun_location()
    {
        // "rode across the Iron Mountains"
        var sentences = new[] { "The riders rode across the Iron Mountains" };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Det("The"), Noun("riders"), Verb("rode"), Adp("across"), Det("the"), Propn("Iron"), Propn("Mountains") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.Single(result);
        Assert.Equal("Iron Mountains", result[0].Name);
    }

    // ── Phase B: descriptor-of patterns ──────────────────────────────────────

    [Fact]
    public void Detects_descriptor_of_pattern()
    {
        // "the kingdom of Alderia"
        var sentences = new[] { "Welcome to the kingdom of Alderia" };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Verb("Welcome"), Adp("to"), Det("the"), Noun("kingdom"), Adp("of"), Propn("Alderia") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.Contains(result, r => r.Name == "kingdom of Alderia");
    }

    // ── Phase C: filtering ───────────────────────────────────────────────────

    [Fact]
    public void Excludes_known_character_names()
    {
        // "She traveled to John" - John is a known character
        var sentences = new[] { "The group traveled to John" };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Det("The"), Noun("group"), Verb("traveled"), Adp("to"), Propn("John") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, CharSet("john"));

        Assert.Empty(result);
    }

    [Fact]
    public void Pronoun_adjacent_to_name_filters_likely_characters()
    {
        // "He told Marcus" - pronoun "He" is adjacent to "Marcus" (within 2 tokens)
        // "Marcus told him" - pronoun "him" is adjacent to "Marcus"
        // These patterns suggest Marcus is a character, not a location
        var sentences = new[]
        {
            "Riders went to Marcus",
            "He told Marcus"
        };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Noun("Riders"), Verb("went"), Adp("to"), Propn("Marcus") },
            new[] { Pron("He"), Verb("told"), Propn("Marcus") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.DoesNotContain(result, r => r.Name == "Marcus");
    }

    [Fact]
    public void Does_not_capture_common_nouns()
    {
        // "She went to the store" - store is a lowercase NOUN, not PROPN
        var sentences = new[] { "The group went to the store" };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Det("The"), Noun("group"), Verb("went"), Adp("to"), Det("the"), Noun("store") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.Empty(result);
    }

    [Fact]
    public void Deduplicates_and_counts_across_sentences()
    {
        var sentences = new[]
        {
            "The army marched to Eldoria",
            "Supplies arrived in Eldoria",
            "The battle raged near Eldoria"
        };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Det("The"), Noun("army"), Verb("marched"), Adp("to"), Propn("Eldoria") },
            new[] { Noun("Supplies"), Verb("arrived"), Adp("in"), Propn("Eldoria") },
            new[] { Det("The"), Noun("battle"), Verb("raged"), Adp("near"), Propn("Eldoria") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.Single(result);
        Assert.Equal("Eldoria", result[0].Name);
        Assert.Equal(3, result[0].Count);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        var result = _service.FindLocationCandidates(
            Array.Empty<string>(),
            Array.Empty<IReadOnlyList<(string, string)>>(),
            NoChars);

        Assert.Empty(result);
    }

    [Fact]
    public void Location_with_pronoun_below_threshold_is_kept()
    {
        // Eldoria appears in 3 sentences, only 1 has a pronoun → 33% < 50% → kept
        var sentences = new[]
        {
            "She traveled to Eldoria",
            "The army marched to Eldoria",
            "Supplies arrived in Eldoria"
        };
        var tagged = new IReadOnlyList<(string, string)>[]
        {
            new[] { Pron("She"), Verb("traveled"), Adp("to"), Propn("Eldoria") },
            new[] { Det("The"), Noun("army"), Verb("marched"), Adp("to"), Propn("Eldoria") },
            new[] { Noun("Supplies"), Verb("arrived"), Adp("in"), Propn("Eldoria") }
        };

        var result = _service.FindLocationCandidates(sentences, tagged, NoChars);

        Assert.Single(result);
        Assert.Equal("Eldoria", result[0].Name);
    }
}
