using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class CharacterVoiceAnalyzerTests
{
    private CharacterVoiceAnalyzer CreateAnalyzer()
    {
        return new CharacterVoiceAnalyzer(new StyleAnalyzer());
    }

    [Fact]
    public void BuildProfiles_NoViewpointScenes_ReturnsEmpty()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "char1", Name = "Alice" });

        // Scene has no viewpoint character set
        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                ViewpointCharacterIds = [],
                Sentences = [new SentenceAnalysis { Index = 0, Text = "Hello world.", WordCount = 2 }]
            }
        };

        var profiles = analyzer.BuildProfiles(results, book);
        Assert.Empty(profiles);
    }

    [Fact]
    public void BuildProfiles_WithViewpointScenes_ComputesProfile()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "char1", Name = "Alice" });

        var sentences = new List<SentenceAnalysis>
        {
            new() { Index = 0, Text = "The sun rose over the distant hills.", WordCount = 7,
                     Emotions = [(EmotionLabel.Joy, 0.8f), (EmotionLabel.Optimism, 0.5f)] },
            new() { Index = 1, Text = "She smiled warmly at the horizon.", WordCount = 6,
                     Emotions = [(EmotionLabel.Joy, 0.9f)] },
            new() { Index = 2, Text = "A gentle breeze carried the scent of flowers.", WordCount = 8,
                     Emotions = [(EmotionLabel.Admiration, 0.6f)] },
        };

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                ViewpointCharacterIds = ["char1"],
                Style = new StyleProfile { TotalSentences = 3, TotalWords = 21 },
                Sentences = sentences
            }
        };

        var profiles = analyzer.BuildProfiles(results, book);

        Assert.Single(profiles);
        var profile = profiles[0];
        Assert.Equal("char1", profile.CharacterId);
        Assert.Equal("Alice", profile.CharacterName);
        Assert.Equal(1, profile.SceneCount);
        Assert.True(profile.TotalWords > 0);
        Assert.True(profile.Style.TotalSentences > 0);

        // Emotion distribution should be mostly Joy label
        Assert.True(profile.EmotionDistribution[EmotionLabel.Joy] > 0.5);
    }

    [Fact]
    public void BuildProfiles_ComputesDistinctiveWords()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "char1", Name = "Alice" });

        // Create sentences with some distinctive words
        var sentences = new List<SentenceAnalysis>();
        for (int i = 0; i < 10; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = "The enigmatic crystalline fortress shimmered beneath moonlight.",
                WordCount = 7,
                Emotions = []
            });
        }

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                ViewpointCharacterIds = ["char1"],
                Style = new StyleProfile { TotalSentences = 10, TotalWords = 70 },
                Sentences = sentences
            }
        };

        var profiles = analyzer.BuildProfiles(results, book);

        Assert.Single(profiles);
        Assert.NotEmpty(profiles[0].DistinctiveWords);
        // "enigmatic", "crystalline", "fortress", "shimmered", "beneath", "moonlight"
        // should be among the top words (stop words like "the" are filtered)
    }

    [Fact]
    public void DetectVoiceAnomalies_ConsistentVoice_NoNotes()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "char1", Name = "Alice" });

        var profiles = new List<CharacterVoiceProfile>
        {
            new()
            {
                CharacterId = "char1",
                CharacterName = "Alice",
                Style = new StyleProfile
                {
                    AverageSentenceLength = 10, ContractionRate = 0.3, DialogueRatio = 0.4
                },
                SceneCount = 4
            }
        };

        // Create 4 scenes with consistent style
        var results = new List<SceneAnalysisResult>();
        for (int i = 0; i < 4; i++)
        {
            var sentences = new List<SentenceAnalysis>();
            for (int j = 0; j < 5; j++)
            {
                sentences.Add(new SentenceAnalysis
                {
                    Index = j,
                    Text = "She didn't know what to expect from the journey ahead.",
                    WordCount = 11
                });
            }

            results.Add(new SceneAnalysisResult
            {
                SceneId = $"s{i}",
                SceneTitle = $"Scene {i}",
                ChapterTitle = "Ch1",
                ViewpointCharacterIds = ["char1"],
                Style = new StyleProfile { ContractionRate = 0.3, DialogueRatio = 0.4 },
                Sentences = sentences
            });
        }

        var notes = analyzer.DetectVoiceAnomalies(profiles, results, book);

        // No anomalies when style is consistent
        Assert.Empty(notes);
    }

    [Fact]
    public void DetectVoiceAnomalies_InconsistentContractionRate_FlagsNote()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "char1", Name = "Alice" });

        var profiles = new List<CharacterVoiceProfile>
        {
            new()
            {
                CharacterId = "char1",
                CharacterName = "Alice",
                Style = new StyleProfile { ContractionRate = 0.8 },
                SceneCount = 6
            }
        };

        // 5 scenes with high contraction rate (text with many contractions)
        var results = new List<SceneAnalysisResult>();
        for (int i = 0; i < 5; i++)
        {
            var sentences = new List<SentenceAnalysis>();
            for (int j = 0; j < 8; j++)
            {
                sentences.Add(new SentenceAnalysis
                {
                    Index = j,
                    Text = "She didn't know he couldn't understand what she'd done and they won't stop.",
                    WordCount = 13
                });
            }
            results.Add(new SceneAnalysisResult
            {
                SceneId = $"s{i}",
                SceneTitle = $"Consistent Scene {i}",
                ChapterTitle = "Ch1",
                ViewpointCharacterIds = ["char1"],
                Sentences = sentences
            });
        }

        // 1 scene with zero contractions (formal text — major outlier)
        var formalSentences = new List<SentenceAnalysis>();
        for (int j = 0; j < 8; j++)
        {
            formalSentences.Add(new SentenceAnalysis
            {
                Index = j,
                Text = "She did not know that he could not understand what she had done and they will not stop.",
                WordCount = 17
            });
        }
        results.Add(new SceneAnalysisResult
        {
            SceneId = "s_outlier",
            SceneTitle = "Formal Scene",
            ChapterTitle = "Ch1",
            ViewpointCharacterIds = ["char1"],
            Sentences = formalSentences
        });

        var notes = analyzer.DetectVoiceAnomalies(profiles, results, book);

        // The formal scene should be flagged as having inconsistent voice
        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.LineEditor &&
            n.SceneTitle == "Formal Scene");
    }

    // ── Dialogue voice profiling tests ──────────────────────────────────

    [Fact]
    public void BuildDialogueProfiles_NoDialogue_ReturnsEmpty()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "c1", Name = "Alice" });

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                Sentences = [new SentenceAnalysis { Index = 0, Text = "The sun rose.", WordCount = 3 }]
            }
        };

        var profiles = analyzer.BuildDialogueProfiles(results, book);
        Assert.Empty(profiles);
    }

    [Fact]
    public void BuildDialogueProfiles_WithAttributedDialogue_ComputesProfile()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "c1", Name = "Sarah" });

        var sentences = new List<SentenceAnalysis>
        {
            new() { Index = 0, Text = "\"I can't believe we made it,\" Sarah said.", WordCount = 9,
                     Emotions = [(EmotionLabel.Joy, 0.8f)] },
            new() { Index = 1, Text = "\"We shouldn't stay here long,\" Sarah whispered.", WordCount = 8,
                     Emotions = [(EmotionLabel.Fear, 0.7f)] },
            new() { Index = 2, Text = "The wind blew through the canyon.", WordCount = 6 }
        };

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                Sentences = sentences
            }
        };

        var profiles = analyzer.BuildDialogueProfiles(results, book);

        Assert.Single(profiles);
        var profile = profiles[0];
        Assert.Equal("c1", profile.CharacterId);
        Assert.Equal("Sarah", profile.CharacterName);
        Assert.Equal(2, profile.DialogueLineCount);
        Assert.Equal(1, profile.SceneCount);
        Assert.True(profile.TotalWords > 0);
        Assert.True(profile.ContractionRate > 0);
    }

    [Fact]
    public void BuildDialogueProfiles_MultipleCharacters_SeparateProfiles()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "c1", Name = "Sarah" });
        book.Characters.Add(new Character { Id = "c2", Name = "John" });

        var sentences = new List<SentenceAnalysis>
        {
            new() { Index = 0, Text = "\"Hello there,\" Sarah said.", WordCount = 5, Emotions = [] },
            new() { Index = 1, Text = "\"Goodbye now,\" John replied.", WordCount = 5, Emotions = [] }
        };

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                Sentences = sentences
            }
        };

        var profiles = analyzer.BuildDialogueProfiles(results, book);

        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, p => p.CharacterName == "Sarah");
        Assert.Contains(profiles, p => p.CharacterName == "John");
    }

    [Fact]
    public void BuildDialogueProfiles_PreservesContractions_InDistinctiveWords()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "c1", Name = "Sarah" });

        // Dialogue heavy with contractions
        var sentences = new List<SentenceAnalysis>();
        for (int i = 0; i < 15; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = "\"I don't think we shouldn't worry, and it isn't a problem,\" Sarah said.",
                WordCount = 13,
                Emotions = []
            });
        }

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                Sentences = sentences
            }
        };

        var profiles = analyzer.BuildDialogueProfiles(results, book);

        Assert.Single(profiles);
        var words = profiles[0].DistinctiveWords.Select(d => d.Word).ToList();

        // Contraction fragments must NOT appear
        Assert.DoesNotContain("don", words);
        Assert.DoesNotContain("isn", words);
        Assert.DoesNotContain("shouldn", words);

        // Full contractions may appear (if they score high enough in TF-IDF)
        // At minimum, fragments should be absent
    }

    [Fact]
    public void BuildDialogueProfiles_PreservesCurlyApostropheContractions()
    {
        var analyzer = CreateAnalyzer();
        var book = new Book { Title = "Test Book" };
        book.Characters.Add(new Character { Id = "c1", Name = "Sarah" });

        var sentences = new List<SentenceAnalysis>();
        for (int i = 0; i < 15; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = "\u201CI don\u2019t think it\u2019s a problem,\u201D Sarah said.",
                WordCount = 9,
                Emotions = []
            });
        }

        var results = new List<SceneAnalysisResult>
        {
            new()
            {
                SceneId = "s1",
                SceneTitle = "Scene 1",
                ChapterTitle = "Ch1",
                Sentences = sentences
            }
        };

        var profiles = analyzer.BuildDialogueProfiles(results, book);

        Assert.Single(profiles);
        var words = profiles[0].DistinctiveWords.Select(d => d.Word).ToList();

        // Curly apostrophe contraction fragments must NOT appear
        Assert.DoesNotContain("don", words);
    }

    [Fact]
    public void DetectDialogueVoiceAnomalies_DifferentVoices_NoWarnings()
    {
        var analyzer = CreateAnalyzer();

        var profiles = new List<DialogueVoiceProfile>
        {
            new()
            {
                CharacterId = "c1", CharacterName = "Sarah",
                AvgSentenceLength = 5, VocabularyRichness = 0.9, ContractionRate = 0.8,
                ReadabilityScore = 80, ClauseComplexity = 0.1, ExclamationRate = 0.5,
                DistinctiveWords = [("hello", 0.5), ("friend", 0.4), ("sunshine", 0.3)]
            },
            new()
            {
                CharacterId = "c2", CharacterName = "John",
                AvgSentenceLength = 20, VocabularyRichness = 0.3, ContractionRate = 0.0,
                ReadabilityScore = 30, ClauseComplexity = 0.9, ExclamationRate = 0.0,
                DistinctiveWords = [("darkness", 0.5), ("empire", 0.4), ("protocol", 0.3)]
            }
        };

        var notes = analyzer.DetectDialogueVoiceAnomalies(profiles);

        Assert.Empty(notes);
    }

    [Fact]
    public void DetectDialogueVoiceAnomalies_IdenticalVoices_FlagsWarning()
    {
        var analyzer = CreateAnalyzer();

        var sharedWords = new List<(string, double)>
        {
            ("hello", 0.5), ("friend", 0.4), ("sunshine", 0.3), ("warmth", 0.2),
            ("gentle", 0.15), ("morning", 0.1), ("calm", 0.09), ("breeze", 0.08),
            ("soft", 0.07), ("quiet", 0.06)
        };

        var profiles = new List<DialogueVoiceProfile>
        {
            new()
            {
                CharacterId = "c1", CharacterName = "Sarah",
                AvgSentenceLength = 10, VocabularyRichness = 0.6, ContractionRate = 0.5,
                ReadabilityScore = 60, ClauseComplexity = 0.3, ExclamationRate = 0.2,
                DistinctiveWords = sharedWords
            },
            new()
            {
                CharacterId = "c2", CharacterName = "John",
                AvgSentenceLength = 10, VocabularyRichness = 0.6, ContractionRate = 0.5,
                ReadabilityScore = 60, ClauseComplexity = 0.3, ExclamationRate = 0.2,
                DistinctiveWords = sharedWords
            }
        };

        var notes = analyzer.DetectDialogueVoiceAnomalies(profiles);

        Assert.Single(notes);
        Assert.Equal(NlpNoteCategory.DevelopmentalEditor, notes[0].Category);
        Assert.Equal(NlpNoteSeverity.Warning, notes[0].Severity);
        Assert.Contains("Sarah", notes[0].Message);
        Assert.Contains("John", notes[0].Message);
    }
}
