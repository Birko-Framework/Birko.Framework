using System;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    public enum DiscountType
    {
        Percentage = 0,
        FixedAmount = 1
    }

    /// <summary>
    /// Discount definition — percentage or fixed amount.
    /// </summary>
    public class Discount
        : AbstractLogModel
        , ILoadable<ViewModels.Discount>
    {
        public string Name { get; set; } = null!;
        public DiscountType Type { get; set; }
        public decimal Value { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual void LoadFrom(ViewModels.Discount data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Type = data.Type;
            Value = data.Value;
            ValidFrom = data.ValidFrom;
            ValidTo = data.ValidTo;
            IsActive = data.IsActive;
        }
    }
}
