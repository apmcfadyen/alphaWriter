using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class ToneShiftDetectionTests
{
    [Fact]
    public void DetectToneShifts_NoEmotions_ReturnsEmpty()
    {
        var result = new SceneAnalysisResult
        {
            SceneId = "s1",
            SceneTitle = "Scene 1",
            ChapterTitle = "Ch1",
            Sentences = CreateSentences(15, [])
        };

        var notes = NlpAnalysisService.DetectToneShifts(result);
        Assert.Empty(notes);
    }

    [Fact]
    public void DetectToneShifts_TooFewSentences_ReturnsEmpty()
    {
        // Only 5 sentences with emotions — below the 10-sentence threshold
        var emotions = new List<(EmotionLabel, float)> { (EmotionLabel.Joy, 0.9f) };
        var result = new SceneAnalysisResult
        {
            SceneId = "s1",
            SceneTitle = "Scene 1",
            ChapterTitle = "Ch1",
            Sentences = CreateSentences(5, emotions)
        };

        var notes = NlpAnalysisService.DetectToneShifts(result);
        Assert.Empty(notes);
    }

    [Fact]
    public void DetectToneShifts_StableEmotion_NoNotes()
    {
        // All sentences have Joy — no shift
        var emotions = new List<(EmotionLabel, float)>
        {
            (EmotionLabel.Joy, 0.9f),
            (EmotionLabel.Optimism, 0.5f)
        };

        var result = new SceneAnalysisResult
        {
            SceneId = "s1",
            SceneTitle = "Scene 1",
            ChapterTitle = "Ch1",
            Sentences = CreateSentences(15, emotions)
        };

        var notes = NlpAnalysisService.DetectToneShifts(result);
        Assert.Empty(notes);
    }

    [Fact]
    public void DetectToneShifts_AbruptShift_ReturnsNote()
    {
        var sentences = new List<SentenceAnalysis>();

        // First 5 sentences: strong Joy cluster
        for (int i = 0; i < 5; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = $"Happy sentence {i}.",
                WordCount = 3,
                Emotions = [(EmotionLabel.Joy, 0.95f), (EmotionLabel.Admiration, 0.8f)]
            });
        }

        // Next 5 sentences: strong Anger cluster (complete shift, no overlap)
        for (int i = 5; i < 10; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = $"Angry sentence {i}.",
                WordCount = 3,
                Emotions = [(EmotionLabel.Anger, 0.95f), (EmotionLabel.Disgust, 0.8f)]
            });
        }

        // 5 more Anger to ensure enough sentences
        for (int i = 10; i < 15; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = $"Angry sentence {i}.",
                WordCount = 3,
                Emotions = [(EmotionLabel.Anger, 0.95f), (EmotionLabel.Disgust, 0.8f)]
            });
        }

        var result = new SceneAnalysisResult
        {
            SceneId = "s1",
            SceneTitle = "The Arrival",
            ChapterTitle = "Ch1",
            Sentences = sentences
        };

        var notes = NlpAnalysisService.DetectToneShifts(result);

        Assert.NotEmpty(notes);
        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.Emotion &&
            n.SceneTitle == "The Arrival");
    }

    [Fact]
    public void DetectToneShifts_GradualShift_LessLikelyToFlag()
    {
        var sentences = new List<SentenceAnalysis>();

        // 15 sentences with gradually changing emotions
        for (int i = 0; i < 15; i++)
        {
            float joyConf = Math.Max(0, 0.9f - i * 0.06f);
            float sadConf = Math.Max(0, i * 0.06f - 0.1f);

            var emotions = new List<(EmotionLabel, float)>();
            if (joyConf > 0.3f) emotions.Add((EmotionLabel.Joy, joyConf));
            if (sadConf > 0.3f) emotions.Add((EmotionLabel.Sadness, sadConf));
            if (emotions.Count == 0) emotions.Add((EmotionLabel.Neutral, 0.5f));

            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = $"Sentence {i}.",
                WordCount = 2,
                Emotions = emotions
            });
        }

        var result = new SceneAnalysisResult
        {
            SceneId = "s1",
            SceneTitle = "Scene 1",
            ChapterTitle = "Ch1",
            Sentences = sentences
        };

        // Gradual shifts may or may not flag — this tests it doesn't crash
        var notes = NlpAnalysisService.DetectToneShifts(result);
        Assert.NotNull(notes);
    }

    private static List<SentenceAnalysis> CreateSentences(int count,
        List<(EmotionLabel Label, float Confidence)> emotions)
    {
        var sentences = new List<SentenceAnalysis>();
        for (int i = 0; i < count; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = $"Test sentence number {i}.",
                WordCount = 4,
                Emotions = [.. emotions]
            });
        }
        return sentences;
    }
}
