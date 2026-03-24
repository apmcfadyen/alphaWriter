using alphaWriter.Models;
using Xunit;

namespace alphaWriter.Tests;

public class SceneAnalysisBadgeTests
{
    [Fact]
    public void AnalysisNoteCount_DefaultIsZero()
    {
        var scene = new Scene { Title = "Test" };
        Assert.Equal(0, scene.AnalysisNoteCount);
        Assert.False(scene.HasAnalysisNotes);
    }

    [Fact]
    public void AnalysisNoteCount_SetPositive_HasAnalysisNotesIsTrue()
    {
        var scene = new Scene { Title = "Test" };
        scene.AnalysisNoteCount = 3;

        Assert.Equal(3, scene.AnalysisNoteCount);
        Assert.True(scene.HasAnalysisNotes);
    }

    [Fact]
    public void AnalysisNoteCount_SetBackToZero_HasAnalysisNotesIsFalse()
    {
        var scene = new Scene { Title = "Test" };
        scene.AnalysisNoteCount = 5;
        Assert.True(scene.HasAnalysisNotes);

        scene.AnalysisNoteCount = 0;
        Assert.False(scene.HasAnalysisNotes);
    }

    [Fact]
    public void AnalysisNoteCount_RaisesPropertyChanged()
    {
        var scene = new Scene { Title = "Test" };
        var changedProperties = new List<string>();
        scene.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        scene.AnalysisNoteCount = 2;

        Assert.Contains(nameof(Scene.AnalysisNoteCount), changedProperties);
        Assert.Contains(nameof(Scene.HasAnalysisNotes), changedProperties);
    }

    [Fact]
    public void AnalysisNoteCount_SameValue_DoesNotRaisePropertyChanged()
    {
        var scene = new Scene { Title = "Test" };
        scene.AnalysisNoteCount = 0; // already 0

        var changedProperties = new List<string>();
        scene.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        scene.AnalysisNoteCount = 0; // same value
        Assert.Empty(changedProperties);
    }

    [Fact]
    public void AnalysisNoteCount_NotSerializedToJson()
    {
        var scene = new Scene { Title = "Test" };
        scene.AnalysisNoteCount = 5;

        var json = System.Text.Json.JsonSerializer.Serialize(scene);
        Assert.DoesNotContain("AnalysisNoteCount", json);
        Assert.DoesNotContain("analysisNoteCount", json);
    }
}
