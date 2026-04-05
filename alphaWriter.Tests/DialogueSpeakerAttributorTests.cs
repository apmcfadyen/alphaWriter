using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class DialogueSpeakerAttributorTests
{
    private static Character MakeCharacter(string id, string name, string fullName = "", string aka = "")
        => new() { Id = id, Name = name, FullName = fullName, Aka = aka };

    private static SentenceAnalysis MakeSentence(int index, string text)
        => new() { Index = index, Text = text, WordCount = text.Split(' ').Length };

    [Fact]
    public void BuildNameLookup_IncludesNameFullNameAndAka()
    {
        var characters = new List<Character>
        {
            MakeCharacter("c1", "Sarah", "Sarah Connor", "Sarge, The Protector")
        };

        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);

        Assert.True(lookup.ContainsKey("Sarah"));
        Assert.True(lookup.ContainsKey("Sarah Connor"));
        Assert.True(lookup.ContainsKey("Sarge"));
        Assert.True(lookup.ContainsKey("The Protector"));
        Assert.Equal("c1", lookup["Sarah"]);
        Assert.Equal("c1", lookup["Sarah Connor"]);
        Assert.Equal("c1", lookup["Sarge"]);
    }

    [Fact]
    public void BuildNameLookup_IsCaseInsensitive()
    {
        var characters = new List<Character> { MakeCharacter("c1", "Sarah") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);

        Assert.True(lookup.ContainsKey("sarah"));
        Assert.True(lookup.ContainsKey("SARAH"));
    }

    [Fact]
    public void AttributeDialogue_PostDialogueTag_AttributesCorrectly()
    {
        var characters = new List<Character> { MakeCharacter("c1", "Sarah") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\"Hello there,\" Sarah said softly.")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Single(result);
        Assert.Equal("c1", result[0].CharacterId);
        Assert.Equal("Sarah", result[0].CharacterName);
        Assert.Equal("Hello there,", result[0].DialogueText);
    }

    [Fact]
    public void AttributeDialogue_PreDialogueTag_AttributesCorrectly()
    {
        var characters = new List<Character> { MakeCharacter("c1", "John") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "John replied, \"Not yet.\"")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Single(result);
        Assert.Equal("c1", result[0].CharacterId);
        Assert.Equal("Not yet.", result[0].DialogueText);
    }

    [Fact]
    public void AttributeDialogue_NoSpeechTag_SkipsLine()
    {
        var characters = new List<Character> { MakeCharacter("c1", "Sarah") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\"Where are we going?\"")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Empty(result);
    }

    [Fact]
    public void AttributeDialogue_UnknownName_SkipsLine()
    {
        var characters = new List<Character> { MakeCharacter("c1", "Sarah") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\"Hello,\" Marcus said.")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Empty(result);
    }

    [Fact]
    public void AttributeDialogue_MultiWordName_MatchesCorrectly()
    {
        var characters = new List<Character>
        {
            MakeCharacter("c1", "Sarah", "Sarah Connor")
        };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\"Run!\" Sarah Connor shouted.")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Single(result);
        Assert.Equal("c1", result[0].CharacterId);
    }

    [Fact]
    public void AttributeDialogue_AkaMatching()
    {
        var characters = new List<Character>
        {
            MakeCharacter("c1", "James", aka: "Sarge")
        };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\"Move out,\" Sarge barked.")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Single(result);
        Assert.Equal("c1", result[0].CharacterId);
        Assert.Equal("James", result[0].CharacterName);
    }

    [Fact]
    public void AttributeDialogue_SmartQuotes()
    {
        var characters = new List<Character> { MakeCharacter("c1", "Sarah") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\u201CHello there,\u201D Sarah said softly.")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Single(result);
        Assert.Equal("c1", result[0].CharacterId);
        Assert.Equal("Hello there,", result[0].DialogueText);
    }

    [Fact]
    public void AttributeDialogue_MultipleSpeakers_SeparateAttributions()
    {
        var characters = new List<Character>
        {
            MakeCharacter("c1", "Sarah"),
            MakeCharacter("c2", "John")
        };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);
        var sentences = new List<SentenceAnalysis>
        {
            MakeSentence(0, "\"Hello,\" Sarah said."),
            MakeSentence(1, "\"Goodbye,\" John replied.")
        };

        var result = DialogueSpeakerAttributor.AttributeDialogue(sentences, "s1", lookup, characters);

        Assert.Equal(2, result.Count);
        Assert.Equal("c1", result[0].CharacterId);
        Assert.Equal("c2", result[1].CharacterId);
    }

    [Fact]
    public void AttributeDialogue_EmptySentences_ReturnsEmpty()
    {
        var characters = new List<Character> { MakeCharacter("c1", "Sarah") };
        var lookup = DialogueSpeakerAttributor.BuildNameLookup(characters);

        var result = DialogueSpeakerAttributor.AttributeDialogue([], "s1", lookup, characters);

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractDialogueText_StraightQuotes()
    {
        var text = DialogueSpeakerAttributor.ExtractDialogueText("\"Hello world\" she said.");
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public void ExtractDialogueText_SmartQuotes()
    {
        var text = DialogueSpeakerAttributor.ExtractDialogueText("\u201CHello world\u201D she said.");
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public void ExtractDialogueText_NoQuotes_ReturnsEmpty()
    {
        var text = DialogueSpeakerAttributor.ExtractDialogueText("No quotes here.");
        Assert.Equal(string.Empty, text);
    }
}
