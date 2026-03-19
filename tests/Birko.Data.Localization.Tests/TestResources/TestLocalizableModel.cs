using System.Collections.Generic;
using Birko.Data.Localization.Models;
using Birko.Data.Models;

namespace Birko.Data.Localization.Tests.TestResources;

public class TestLocalizableModel : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // not localizable

    public IReadOnlyList<string> GetLocalizableFields()
        => new[] { nameof(Name), nameof(Description) };

    public override AbstractModel CopyTo(AbstractModel? clone = null)
    {
        clone ??= new TestLocalizableModel();
        base.CopyTo(clone);
        if (clone is TestLocalizableModel target)
        {
            target.Name = Name;
            target.Description = Description;
            target.Code = Code;
        }
        return clone;
    }
}
