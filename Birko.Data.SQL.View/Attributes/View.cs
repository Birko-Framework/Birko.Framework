using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.SQL.Attributes
{
    public enum ViewConnect
    {
        None,
        Check,
        CheckExisting,
    }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
    public class ViewAttribute : System.Attribute
    {
        public Type ModelLeft { get; private set; }
        public Type ModelRight { get; private set; }
        public object ModelProperyLeft { get; private set; }
        public object ModelProperyRight { get; private set; }
        public string? Name { get; internal set; }
        public ViewConnect Connect { get; internal set; }

        /// <summary>
        /// Controls how queries for this view are executed.
        /// Default is OnTheFly (generate SELECT with joins from attributes).
        /// Set to Persistent to query against a pre-created database VIEW,
        /// or Auto to try persistent first with fallback to on-the-fly.
        /// </summary>
        public ViewQueryMode QueryMode { get; set; } = ViewQueryMode.OnTheFly;

        public ViewAttribute(Type modelLeft, Type modelRight, object modelProperyLeft, object modelProperyRight, string? name = null, ViewConnect connect = ViewConnect.None)
        {
            ModelLeft = modelLeft;
            ModelRight = modelRight;
            ModelProperyLeft = modelProperyLeft;
            ModelProperyRight = modelProperyRight;
            Name = name;
            Connect = connect;
        }
    }
}
