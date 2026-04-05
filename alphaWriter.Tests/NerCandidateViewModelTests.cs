using alphaWriter.Models;
using alphaWriter.ViewModels;
using Xunit;

namespace alphaWriter.Tests;

public class NerCandidateViewModelTests
{
    [Fact]
    public void IsSelected_DefaultsToTrue()
    {
        var vm = new NerCandidateViewModel { Name = "Elena", Type = NerEntityType.Character, Count = 3 };
        Assert.True(vm.IsSelected);
    }

    [Fact]
    public void CountLabel_PluralCount_ShowsCorrectLabel()
    {
        var vm = new NerCandidateViewModel { Name = "Elena", Type = NerEntityType.Character, Count = 5 };
        Assert.Equal("found 5\u00d7", vm.CountLabel);
    }

    [Fact]
    public void CountLabel_SingleCount_ShowsCorrectLabel()
    {
        var vm = new NerCandidateViewModel { Name = "Dr. Webb", Type = NerEntityType.Character, Count = 1 };
        Assert.Equal("found 1\u00d7", vm.CountLabel);
    }

    [Fact]
    public void IsSelected_CanBeToggledToFalse()
    {
        var vm = new NerCandidateViewModel { Name = "X", Type = NerEntityType.Location, Count = 2 };
        vm.IsSelected = false;
        Assert.False(vm.IsSelected);
    }
}
