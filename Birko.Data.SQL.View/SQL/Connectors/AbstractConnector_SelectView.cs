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

        public IEnumerable<object> SelectView<T, P>(Type type, LambdaExpression expr, IDictionary<Expression<Func<T, P>>, bool>? orderFields = null, int? limit = null, int? offset = null)
        {
            foreach (var item in SelectView(type, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, orderFields?.ToDictionary(x => DataBase.GetViewField(x.Key).GetSelectName(true), x => x.Value), limit, offset))
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
                    DataBase.ReadView(reader, data);
                    return data;
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
            foreach (var item in Select(view, transformFunction, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, orderFields?.ToDictionary(x => DataBase.GetViewField(x.Key).GetSelectName(true), x => x.Value), limit, offset))
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
            if (view != null)
            {
                foreach (var items in RunReaderCommand((command) => {
                    command = CreateSelectCommand(command, view, conditions, orderFields, limit, offset);
                }, (reader) => new object[1] { transformFunction?.Invoke(view.GetSelectFields(), reader) ?? null })) 
                {
                    yield return items?.FirstOrDefault();
                }
            }
        }
    }
}
