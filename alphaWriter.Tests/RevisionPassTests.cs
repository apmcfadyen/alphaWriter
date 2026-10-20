using System.Reflection;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Phase 3: stepping through notes, revision passes and progress history.</summary>
public class RevisionPassTests
{
    private readonly Book _book = new() { Title = "B" };
    private readonly Scene _s1 = new() { Title = "One", Content = "<p>One.</p>" };
    private readonly Scene _s2 = new() { Title = "Two", Content = "<p>Two.</p>" };

    public RevisionPassTests()
    {
        var chapter = new Chapter { Title = "C" };
        chapter.Scenes.Add(_s1);
        chapter.Scenes.Add(_s2);
        _book.Chapters.Add(chapter);
    }

    private WriterViewModel CreateViewModel(params NlpNote[] notes)
    {
        var bookService = new Mock<IBookService>();
        bookService.Setup(s => s.SaveBooksAsync(It.IsAny<List<Book>>())).Returns(Task.CompletedTask);
        var vm = new WriterViewModel(bookService.Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);
        Set(vm, "selectedBook", _book);
        Set(vm, "_allNotes", notes.ToList());
        vm.GetType().GetMethod("ApplyNoteFilters", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
        return vm;
    }

    private static void Set(object target, string name, object? value) =>
        typeof(WriterViewModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static NlpNote Note(string rule, string fp, Scene scene, int start) => new()
    {
        RuleId = rule, Fingerprint = fp, SceneId = scene.Id, Start = start, Length = 3,
        AnchorText = "x", Category = NlpNoteCategory.LineEditor, Message = fp
    };

    private static string? Current(WriterViewModel vm) =>
        ((NlpNote?)typeof(WriterViewModel).GetField("_currentNote", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(vm))?.Fingerprint;

    [Fact]
    public void StepNote_FollowsReadingOrderAndWraps()
    {
        // Listed out of order on purpose: stepping follows the book, not the list
        var vm = CreateViewModel(Note("a", "late", _s2, 0), Note("a", "second", _s1, 50), Note("a", "first", _s1, 5));

        vm.StepNote(+1);
        Assert.Equal("first", Current(vm));
        Assert.Same(_s1, vm.SelectedScene);
        vm.StepNote(+1);
        Assert.Equal("second", Current(vm));
        vm.StepNote(+1);
        Assert.Equal("late", Current(vm));
        Assert.Same(_s2, vm.SelectedScene);
        vm.StepNote(+1);
        Assert.Equal("first", Current(vm));   // wraps
        vm.StepNote(-1);
        Assert.Equal("late", Current(vm));
    }

    [Fact]
    public void StepNote_AfterCurrentNoteIsIgnored_ContinuesFromItsPlace()
    {
        var vm = CreateViewModel(Note("a", "n1", _s1, 5), Note("a", "n2", _s1, 50), Note("a", "n3", _s2, 0));
        vm.StepNote(+1);
        vm.StepNote(+1);   // on n2
        vm.ApplyNoteAction("dismiss", "n2", "a", _s1.Id, null);
        vm.StepNote(+1);
        Assert.Equal("n3", Current(vm));
    }

    [Fact]
    public void Pass_OnlyStepsThroughItsRuleAndNarrowsTheMarks()
    {
        var vm = CreateViewModel(Note("a", "a1", _s1, 5), Note("b", "b1", _s1, 10), Note("a", "a2", _s2, 0));
        vm.RebuildPassOptions();
        Assert.Equal(2, vm.PassOptions.Single(o => o.RuleId == "a").Count);

        vm.StartPassCommand.Execute(vm.PassOptions.Single(o => o.RuleId == "a"));
        Assert.True(vm.IsPassActive);
        Assert.Equal("a1", vm.CurrentPassNote?.Fingerprint);
        Assert.Equal("Note 1 of 2", vm.PassProgressText);
        Assert.DoesNotContain("\"b\"", vm.BuildAnnotationsJson(_s1.Id));
        Assert.All(vm.ReviseNoteGroups.SelectMany(g => g), n => Assert.Equal("a", n.RuleId));

        vm.NextNoteCommand.Execute(null);
        Assert.Equal("a2", vm.CurrentPassNote?.Fingerprint);

        vm.IgnorePassNoteCommand.Execute(null);   // a2 hidden: back round to a1
        Assert.Equal("a1", vm.CurrentPassNote?.Fingerprint);
        Assert.Equal("Note 1 of 1 · 1 handled", vm.PassProgressText);

        vm.EndPassCommand.Execute(null);
        Assert.False(vm.IsPassActive);
        Assert.Contains("\"b\"", vm.BuildAnnotationsJson(_s1.Id));
    }

    [Fact]
    public void Pass_WhenEverythingIsHandled_SaysSo()
    {
        var vm = CreateViewModel(Note("a", "a1", _s1, 5));
        vm.RebuildPassOptions();
        vm.StartPassCommand.Execute(vm.PassOptions[0]);
        vm.IgnorePassNoteCommand.Execute(null);
        Assert.Null(vm.CurrentPassNote);
        Assert.Equal("All 1 handled. Nice work.", vm.PassProgressText);
    }

    [Fact]
    public void History_RecordsVisibleCountsAndShowsTheTrend()
    {
        var vm = CreateViewModel(Note("a", "a1", _s1, 5), Note("a", "a2", _s1, 9), Note("b", "b1", _s2, 0));
        vm.RecordAnalysisSnapshot();
        Assert.Equal(string.Empty, vm.ProgressSummary);   // nothing to compare yet

        vm.ApplyNoteAction("dismiss", "a2", "a", _s1.Id, null);
        vm.RecordAnalysisSnapshot();

        var latest = _book.AnalysisHistory[^1];
        Assert.Equal(2, latest.Total);
        Assert.Equal(1, latest.ByRule["a"]);
        Assert.StartsWith("Down 1 since", vm.ProgressSummary);
        Assert.EndsWith("(3 → 2)", vm.ProgressSummary);

        vm.RebuildPassOptions();
        Assert.Equal("was 2", vm.PassOptions.Single(o => o.RuleId == "a").Trend);
        Assert.False(vm.PassOptions.Single(o => o.RuleId == "b").HasTrend);
    }

    [Fact]
    public void History_IsCapped()
    {
        var vm = CreateViewModel(Note("a", "a1", _s1, 5));
        for (int i = 0; i < 40; i++) vm.RecordAnalysisSnapshot();
        Assert.Equal(30, _book.AnalysisHistory.Count);
    }

    [Theory]
    [InlineData("next")]
    [InlineData("prev")]
    public void Shortcut_F8StepsThroughNotes(string key)
    {
        var vm = CreateViewModel(Note("a", "only", _s1, 5));
        Assert.True(vm.HandleShortcut(key));
        Assert.Equal("only", Current(vm));
    }
}
