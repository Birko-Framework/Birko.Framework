using Birko.Xaml.Core.Data;
using Birko.Xaml.Core.Localization;
using Birko.Xaml.Core.Mvvm;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

// ── Test doubles ────────────────────────────────────────────────────────────
public sealed class Person
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Trivial in-memory <see cref="ICrudDataSource{T}"/> — the port the VMs depend on.
/// (A real app adapts a Birko.Data store to this same port in its own assembly.)</summary>
public sealed class FakePeople : ICrudDataSource<Person>
{
    private readonly Dictionary<Guid, Person> _store = new();

    public Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Person>>(_store.Values.ToList());

    public Task<Person?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<Guid> SaveAsync(Person item, CancellationToken ct = default)
    {
        if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
        _store[item.Id] = item;
        return Task.FromResult(item.Id);
    }

    public Task DeleteAsync(Person item, CancellationToken ct = default)
    {
        _store.Remove(item.Id);
        return Task.CompletedTask;
    }

    public Person NewInstance() => new();

    public int Count => _store.Count;
}

public sealed class PeopleCrudVm : CrudViewModelBase<Person>
{
    public PeopleCrudVm(ICrudDataSource<Person> data, II18n? i18n = null) : base(data, i18n) { }
}

public sealed class PersonDetailVm : DetailPageViewModel<Person>
{
    public PersonDetailVm(ICrudDataSource<Person> data) : base(data) { }
}

// ── Tests ────────────────────────────────────────────────────────────────────
public class ViewModelTests
{
    private static async Task<(PeopleCrudVm vm, FakePeople data)> SeededAsync(int n)
    {
        var data = new FakePeople();
        for (int i = 0; i < n; i++) await data.SaveAsync(new Person { Name = $"P{i}" });
        var vm = new PeopleCrudVm(data, new I18n());
        await vm.LoadAsync();
        return (vm, data);
    }

    [Fact]
    public async Task Load_populates_items()
    {
        var (vm, _) = await SeededAsync(3);
        vm.Items.Should().HaveCount(3);
        vm.IsLoaded.Should().BeTrue();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Create_then_save_persists_and_refreshes()
    {
        var (vm, data) = await SeededAsync(1);
        vm.CreateCommand.Execute(null);
        vm.EditingItem.Should().NotBeNull();
        vm.EditingItem!.Name = "New";

        await vm.SaveEditingCommand.ExecuteAsync(null);

        data.Count.Should().Be(2);
        vm.Items.Should().HaveCount(2);
        vm.EditingItem.Should().BeNull("editing ends after save");
    }

    [Fact]
    public async Task Delete_removes_selected()
    {
        var (vm, data) = await SeededAsync(2);
        vm.SelectedItem = vm.Items[0];
        await vm.DeleteCommand.ExecuteAsync(null);
        data.Count.Should().Be(1);
        vm.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Create_command_is_gated_by_permission()
    {
        var (vm, _) = await SeededAsync(0);
        vm.CanCreate.Should().BeTrue();
        vm.CreateCommand.CanExecute(null).Should().BeTrue();

        vm.CanCreate = false;
        vm.CreateCommand.CanExecute(null).Should().BeFalse("permission flag must gate the command");
    }

    [Fact]
    public async Task Delete_command_requires_selection_and_permission()
    {
        var (vm, _) = await SeededAsync(2);
        vm.DeleteCommand.CanExecute(null).Should().BeFalse("nothing selected yet");

        vm.SelectedItem = vm.Items[0];
        vm.DeleteCommand.CanExecute(null).Should().BeTrue();

        vm.CanDelete = false;
        vm.DeleteCommand.CanExecute(null).Should().BeFalse("permission revoked");
    }

    [Fact]
    public void Base_vm_reemits_on_locale_change()
    {
        var i18n = new I18n();
        i18n.AddLocale("en", new Dictionary<string, string> { ["t"] = "Title" });
        i18n.AddLocale("sk", new Dictionary<string, string> { ["t"] = "Titulok" });
        var vm = new PeopleCrudVm(new FakePeople(), i18n);

        bool reemitted = false;
        vm.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName)) reemitted = true; };

        vm.L("t").Should().Be("Title");
        i18n.SetLocale("sk");

        reemitted.Should().BeTrue("VM must re-raise so localized bindings refresh");
        vm.L("t").Should().Be("Titulok");
    }

    [Fact]
    public async Task Detail_vm_loads_and_gates_save()
    {
        var data = new FakePeople();
        var id = await data.SaveAsync(new Person { Name = "Existing" });
        var vm = new PersonDetailVm(data);

        vm.SaveCommand.CanExecute(null).Should().BeFalse("no model loaded");
        await vm.LoadAsync(id);
        vm.Model!.Name.Should().Be("Existing");
        vm.SaveCommand.CanExecute(null).Should().BeTrue();

        vm.CanSave = false;
        vm.SaveCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task List_vm_filters_client_side()
    {
        var data = new FakePeople();
        await data.SaveAsync(new Person { Name = "Alice" });
        await data.SaveAsync(new Person { Name = "Bob" });
        var vm = new ListPageViewModel<Person>(data) { SearchMatch = (p, q) => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) };
        await vm.LoadAsync();

        vm.Filtered.Should().HaveCount(2);
        vm.SearchText = "ali";
        vm.Filtered.Should().ContainSingle(p => p.Name == "Alice");
    }
}
