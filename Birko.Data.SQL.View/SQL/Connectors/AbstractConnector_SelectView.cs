using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        public IEnumerable<object> SelectView(Type type, LambdaExpression expr, IDictionary<string, bool>? orderFields = null, int? limit = null, int? offset = null)
        {
            foreach (var item in SelectView(type, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, orderFields, limit, offset))
            {
                yield return item;
            }
        }

        // TASK-128: the expression-keyed overloads pass the VIEW PROPERTY name, not a pre-resolved select
        // name. Resolution happens once, in the Select(Tables.View, …) funnel below, which is the only place
        // that knows whether the query targets a persistent view — and the two paths expose their columns
        // under different names. Pre-resolving here could only ever be right for one of them.
        public IEnumerable<object> SelectView<T, P>(Type type, LambdaExpression expr, IDictionary<Expression<Func<T, P>>, bool>? orderFields = null, int? limit = null, int? offset = null)
        {
            foreach (var item in SelectView(type, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, orderFields?.ToDictionary(x => DataBase.GetViewOrderKey(x.Key), x => x.Value), limit, offset))
            {
                yield return item;
            }
        }

        public IEnumerable<object> SelectView
            (Type type,
            IEnumerable<Conditions.Condition>? conditions = null,
            IDictionary<string, bool>? orderFields = null,
            int? limit = null,
            int? offset = null
        )
        {
            foreach (var item in Select(DataBase.LoadView(type), (fields, reader) => {
                    var data = Activator.CreateInstance(type, Array.Empty<object>());
                    DataBase.ReadView(reader, data!);
                    return data!;
            }, conditions, orderFields, limit, offset)) {
                yield return item;
            }
        }

        public IEnumerable<object> Select(
            Tables.View view,
            Func<IDictionary<int, string>, DbDataReader, object>? transformFunction = null,
            LambdaExpression? expr = null,
            IDictionary<string, bool>? orderFields = null,
            int? limit = null,
            int? offset = null
        )
        {
            foreach (var item in Select(view, transformFunction, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, orderFields, limit, offset))
            {
                yield return item;
            }
        }

        public IEnumerable<object> Select<T, P>(
            Tables.View view,
            Func<IDictionary<int, string>, DbDataReader, object>? transformFunction = null,
            LambdaExpression? expr = null,
            IDictionary<Expression<Func<T, P>>, bool>? orderFields = null,
            int? limit = null,
            int? offset = null
        )
        {
            foreach (var item in Select(view, transformFunction, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, orderFields?.ToDictionary(x => DataBase.GetViewOrderKey(x.Key), x => x.Value), limit, offset))
            {
                yield return item;
            }
        }

        public IEnumerable<object> Select(
            Tables.View view,
            Func<IDictionary<int, string>, DbDataReader, object>? transformFunction = null,
            IEnumerable<Conditions.Condition>? conditions = null,
            IDictionary<string, bool>? orderFields = null,
            int? limit = null,
            int? offset = null)
        {
            if (view == null)
            {
                yield break;
            }

            var usePersistent = ShouldUsePersistentView(view, name => ViewExists(name));

            // TASK-128: the single resolution point for view sort keys. It has to sit AFTER usePersistent is
            // known, because a persistent view exposes its columns under different names than the on-the-fly
            // join select does — and it has to sit here rather than in the command builders, since every view
            // read (both paths, both the string- and expression-keyed entry points) funnels through this
            // method. Resolving makes the ORDER BY clause unreachable from caller-supplied text.
            orderFields = DataBase.ResolveViewOrderFields(view, orderFields, usePersistent);

            if (usePersistent)
            {
                foreach (var items in RunReaderCommand((command) => {
                    command = CreatePersistentViewSelectCommand(command, view, conditions, orderFields, limit, offset);
                }, (reader) => new object[1] { transformFunction?.Invoke(view.GetPersistentViewSelectFields(), reader)! }))
                {
                    yield return items?.FirstOrDefault()!;
                }
            }
            else
            {
                foreach (var items in RunReaderCommand((command) => {
                    command = CreateSelectCommand(command, view, conditions, orderFields, limit, offset);
                }, (reader) => new object[1] { transformFunction?.Invoke(view.GetSelectFields(), reader)! }))
                {
                    yield return items?.FirstOrDefault()!;
                }
            }
        }
    }
}
