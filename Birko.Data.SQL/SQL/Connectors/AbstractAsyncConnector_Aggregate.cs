using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Birko.Data.Stores;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractAsyncConnector
    {
        /// <summary>
        /// Executes an aggregation query using SQL GROUP BY.
        /// Builds SELECT with aggregate functions, passes groupFields to CreateSelectCommand.
        /// </summary>
        public async IAsyncEnumerable<AggregateResult> SelectAggregateAsync<T>(
            Type type,
            AggregateQuery<T> query,
            [EnumeratorCancellation] CancellationToken ct = default)
            where T : Models.AbstractModel
        {
            var table = DataBase.LoadTable(type);
            if (table == null) yield break;

            var (fields, groupFields, conditions, orderFields) = BuildAggregateQueryParts(type, query);

            // TASK-833: an aggregate's groups span chunks, so it is never split; past the provider's limit it is refused
            // here, before the reader runs, rather than failing in the driver.
            var terms = conditions?.ToList();
            PlanInListChunks("aggregate", terms,
                (command, chunk) => CreateSelectCommand(command, new[] { table.Name }, fields, null, chunk,
                    groupFields.Count > 0 ? groupFields : null, orderFields, query.Limit, query.Offset),
                "it is an aggregate, whose groups would span chunks").ThrowIfRefused();

            await foreach (var row in RunReaderCommandAsync(
                async command =>
                {
                    CreateSelectCommand(
                        command,
                        new[] { table.Name },
                        fields,
                        null,
                        terms,
                        groupFields.Count > 0 ? groupFields : null,
                        orderFields,
                        query.Limit,
                        query.Offset);
                    await Task.CompletedTask;
                },
                async reader => new[] { ReadAggregateResult(reader) },
                ct))
            {
                foreach (var item in row) yield return (AggregateResult)item;
            }
        }

    }
}
