using System.Collections.Generic;
using Birko.Data.ViewModels;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

public class ViewModelTests
{
    private sealed class TestViewModel : ViewModel
    {
        private string? _name;

        public string? Name
        {
            get => _name;
            set
            {
                _name = value;
                RaisePropertyChanged();
            }
        }

        public void RaiseExplicit(string propertyName) => RaisePropertyChanged(propertyName);
    }

    [Fact]
    public void RaisePropertyChanged_uses_caller_member_name()
    {
        var vm = new TestViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "x";

        raised.Should().ContainSingle().Which.Should().Be(nameof(TestViewModel.Name));
    }

    [Fact]
    public void RaisePropertyChanged_with_explicit_name_forwards_it()
    {
        var vm = new TestViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.RaiseExplicit("Custom");

        raised.Should().ContainSingle().Which.Should().Be("Custom");
    }

    [Fact]
    public void RaisePropertyChanged_without_subscribers_does_not_throw()
    {
        var vm = new TestViewModel();

        var act = () => vm.Name = "x";

        act.Should().NotThrow();
    }
}
