using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.InfluxDB.Stores
{
    /// <summary>
    /// Async InfluxDB data store for CRUD and bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncInfluxDBStore<T> : Data.Stores.AbstractAsyncBulkStore<T>, Data.Stores.ISettingsStore<Settings>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Gets the InfluxDB client.
        /// </summary>
        public InfluxDB.InfluxDBClient? Client { get; private set; }

        /// <summary>
        /// The settings for this store.
        /// </summary>
        protected Settings? _settings = null;

        /// <summary>
        /// Gets the measurement name for this entity type.
        /// </summary>
        protected virtual string MeasurementName => typeof(T).Name;

        /// <summary>
        /// Initializes a new instance of the AsyncInfluxDBStore class.
        /// </summary>
        public AsyncInfluxDBStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The InfluxDB settings to use.</param>
        public virtual void SetSettings(Settings settings)
        {
            if (settings != null)
            {
                _settings = settings;
                Client = new InfluxDB.InfluxDBClient(settings);
            }
        }

        /// <summary>
        /// Sets the connection settings via the ISettings interface.
        /// </summary>
        /// <param name="settings">The settings to use.</param>
        public virtual void SetSettings(Data.Stores.ISettings settings)
        {
            if (settings is Settings influxSettings)
            {
                SetSettings(influxSettings);
            }
        }

        /// <inheritdoc />
        public override async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            if (Client == null || _settings == null)
            {
                return null;
            }

            var flux = $"from(bucket: \"{_settings.Bucket}\") " +
                       $"|> range(start: 0) " +
                       $"|> filter(fn: (r) => r._measurement == \"{MeasurementName}\") " +
                       $"|> filter(fn: (r) => r.Guid == \"{guid}\") " +
                       $"|> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\") " +
                       $"|> last()";

            try
            {
                var queryApi = Client.GetQueryApi();
                var records = await queryApi.QueryAsync(flux, _settings.Organization, ct);
                if (records != null && records.Count > 0 && records[0].Records.Count > 0)
                {
                    return MapRecordToModel(records[0].Records.Last());
                }
                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read from InfluxDB. Guid: {guid}", ex);
            }
        }

        /// <inheritdoc />
        public override async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Client == null || _settings == null)
            {
                return null;
            }

            var flux = $"from(bucket: \"{_settings.Bucket}\") " +
                       $"|> range(start: 0) " +
                       $"|> filter(fn: (r) => r._measurement == \"{MeasurementName}\") " +
                       $"|> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\") " +
                       $"|> last()";

            try
            {
                var queryApi = Client.GetQueryApi();
                var records = await queryApi.QueryAsync(flux, _settings.Organization, ct);
                if (records != null && records.Count > 0 && records[0].Records.Count > 0)
                {
                    var items = records[0].Records.Select(r => MapRecordToModel(r)).Where(x => x != null)!;
                    if (filter != null)
                    {
                        var compiled = filter.Compile();
                        return items.FirstOrDefault(x => x != null && compiled(x));
                    }
                    return items.FirstOrDefault();
                }
                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read from InfluxDB", ex);
            }
        }

        /// <inheritdoc />
        public override async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (Client == null || _settings == null)
            {
                return 0;
            }

            if (filter != null)
            {
                // Filter requires in-memory evaluation since Flux can't translate C# expressions
                var items = await ReadAsync(filter, null, null, null, ct);
                return items.Count();
            }

            var flux = $"from(bucket: \"{_settings.Bucket}\") " +
                       $"|> range(start: 0) " +
                       $"|> filter(fn: (r) => r._measurement == \"{MeasurementName}\") " +
                       $"|> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\") " +
                       $"|> group() " +
                       $"|> distinct(column: \"Guid\") " +
                       $"|> count()";

            try
            {
                var queryApi = Client.GetQueryApi();
                var records = await queryApi.QueryAsync(flux, _settings.Organization, ct);
                if (records != null && records.Count > 0 && records[0].Records.Count > 0)
                {
                    var countValue = records[0].Records[0].GetValue();
                    if (countValue != null)
                    {
                        return Convert.ToInt64(countValue);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to count in InfluxDB", ex);
            }
        }

        /// <inheritdoc />
        public override async Task<Guid> CreateAsync(T data, Data.Stores.StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null)
            {
                return Guid.Empty;
            }

            data.Guid ??= Guid.NewGuid();
            processDelegate?.Invoke(data);

            try
            {
                var point = ModelToPoint(data);
                var writeApi = Client.GetWriteApiAsync();
                await writeApi.WritePointAsync(point, _settings.Bucket, _settings.Organization, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create in InfluxDB. Guid: {data.Guid}", ex);
            }

            return data.Guid.Value;
        }

        /// <inheritdoc />
        public override async Task UpdateAsync(T data, Data.Stores.StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null || data.Guid == null || data.Guid == Guid.Empty)
            {
                return;
            }

            processDelegate?.Invoke(data);

            try
            {
                var point = ModelToPoint(data);
                var writeApi = Client.GetWriteApiAsync();
                await writeApi.WritePointAsync(point, _settings.Bucket, _settings.Organization, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update in InfluxDB. Guid: {data.Guid}", ex);
            }
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(T data, CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null || data.Guid == null || data.Guid == Guid.Empty)
            {
                return;
            }

            try
            {
                var deleteApi = Client.GetDeleteApi();
                var predicate = $"_measurement=\"{MeasurementName}\" AND Guid=\"{data.Guid}\"";
                await deleteApi.Delete(
                    DateTime.UnixEpoch,
                    DateTime.UtcNow.AddYears(1),
                    predicate,
                    _settings.Bucket,
                    _settings.Organization);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete from InfluxDB. Guid: {data.Guid}", ex);
            }
        }

        /// <inheritdoc />
        public override async Task<Guid> SaveAsync(T data, Data.Stores.StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null)
            {
                return await Task.FromResult(Guid.Empty);
            }

            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                await CreateAsync(data, processDelegate, ct);
            }
            else
            {
                var point = ModelToPoint(data);
                var writeApi = Client.GetWriteApiAsync();
                await writeApi.WritePointAsync(point, _settings.Bucket, _settings.Organization, ct);
            }

            return data.Guid ?? Guid.Empty;
        }

        /// <inheritdoc />
        public override async Task InitAsync(CancellationToken ct = default)
        {
            if (Client == null || _settings == null)
            {
                return;
            }

            try
            {
                var bucketsApi = Client.GetBucketsApi();
                var bucket = await bucketsApi.FindBucketByNameAsync(_settings.Bucket);
                if (bucket == null)
                {
                    var orgsApi = Client.GetOrganizationsApi();
                    var orgs = await orgsApi.FindOrganizationsAsync(org: _settings.Organization);
                    var org = orgs.FirstOrDefault();
                    if (org != null)
                    {
                        await bucketsApi.CreateBucketAsync(_settings.Bucket, org.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize InfluxDB bucket '{_settings.Bucket}'", ex);
            }
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            if (Client == null || _settings == null)
            {
                return;
            }

            try
            {
                var bucketsApi = Client.GetBucketsApi();
                var bucket = await bucketsApi.FindBucketByNameAsync(_settings.Bucket);
                if (bucket != null)
                {
                    await bucketsApi.DeleteBucketAsync(bucket);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to destroy InfluxDB bucket '{_settings.Bucket}'", ex);
            }
        }

        #region Bulk Operations (IAsyncBulkStore<T>)

        /// <inheritdoc />
        public override async Task<IEnumerable<T>> ReadAsync(
            Expression<Func<T, bool>>? filter = null,
            Data.Stores.OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            if (Client == null || _settings == null)
            {
                return await Task.FromResult(Enumerable.Empty<T>());
            }

            var flux = $"from(bucket: \"{_settings.Bucket}\") " +
                       $"|> range(start: 0) " +
                       $"|> filter(fn: (r) => r._measurement == \"{MeasurementName}\") " +
                       $"|> pivot(rowKey: [\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\") " +
                       $"|> group() " +
                       $"|> sort(columns: [\"_time\"], desc: true) " +
                       $"|> unique(column: \"Guid\")";

            if (orderBy?.Fields.Count > 0)
            {
                var columns = string.Join(", ", orderBy.Fields.Select(f => $"\"{f.PropertyName}\""));
                var desc = orderBy.Fields[0].Descending ? "true" : "false";
                flux += $" |> sort(columns: [{columns}], desc: {desc})";
            }

            if (offset.HasValue)
            {
                flux += $" |> limit(n: {int.MaxValue}, offset: {offset.Value})";
            }

            if (limit.HasValue)
            {
                flux += $" |> limit(n: {limit.Value})";
            }

            try
            {
                var queryApi = Client.GetQueryApi();
                var records = await queryApi.QueryAsync(flux, _settings.Organization, ct);
                var items = new List<T>();
                if (records != null)
                {
                    foreach (var table in records)
                    {
                        foreach (var record in table.Records)
                        {
                            var model = MapRecordToModel(record);
                            if (model != null)
                            {
                                items.Add(model);
                            }
                        }
                    }
                }

                if (filter != null)
                {
                    var compiled = filter.Compile();
                    return items.Where(compiled).ToList();
                }
                return items;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read bulk from InfluxDB", ex);
            }
        }

        /// <inheritdoc />
        public override async Task CreateAsync(
            IEnumerable<T> data,
            Data.Stores.StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null)
            {
                return;
            }

            var itemsToCreate = data.Where(x => x != null).ToList();
            if (itemsToCreate.Count == 0)
            {
                return;
            }

            var points = new List<PointData>();
            foreach (var item in itemsToCreate)
            {
                item.Guid = item.Guid ?? Guid.NewGuid();
                storeDelegate?.Invoke(item);
                points.Add(ModelToPoint(item));
            }

            try
            {
                var writeApi = Client.GetWriteApiAsync();
                await writeApi.WritePointsAsync(points, _settings.Bucket, _settings.Organization, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to bulk create in InfluxDB", ex);
            }
        }

        /// <inheritdoc />
        public override async Task UpdateAsync(
            IEnumerable<T> data,
            Data.Stores.StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null)
            {
                return;
            }

            var itemsToUpdate = data.Where(x => x != null && x.Guid != null && x.Guid != Guid.Empty).ToList();
            if (itemsToUpdate.Count == 0)
            {
                return;
            }

            var points = new List<PointData>();
            foreach (var item in itemsToUpdate)
            {
                storeDelegate?.Invoke(item);
                points.Add(ModelToPoint(item));
            }

            try
            {
                var writeApi = Client.GetWriteApiAsync();
                await writeApi.WritePointsAsync(points, _settings.Bucket, _settings.Organization, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to bulk update in InfluxDB", ex);
            }
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            if (Client == null || _settings == null || data == null)
            {
                return;
            }

            var guids = data
                .Where(x => x != null && x.Guid != null && x.Guid != Guid.Empty)
                .Select(x => x.Guid!.Value)
                .ToList();

            if (guids.Count == 0)
            {
                return;
            }

            try
            {
                var deleteApi = Client.GetDeleteApi();
                foreach (var guid in guids)
                {
                    var predicate = $"_measurement=\"{MeasurementName}\" AND Guid=\"{guid}\"";
                    await deleteApi.Delete(
                        DateTime.UnixEpoch,
                        DateTime.UtcNow.AddYears(1),
                        predicate,
                        _settings.Bucket,
                        _settings.Organization);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to bulk delete from InfluxDB", ex);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Converts a model to an InfluxDB point.
        /// </summary>
        /// <param name="model">The model to convert.</param>
        /// <returns>An InfluxDB point data.</returns>
        protected virtual PointData ModelToPoint(T model)
        {
            var point = PointData.Measurement(MeasurementName)
                .Tag("Guid", model.Guid?.ToString() ?? string.Empty)
                .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Guid" && p.CanRead);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(model);
                if (value == null)
                {
                    continue;
                }

                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (propType == typeof(string))
                {
                    point = point.Field(prop.Name, (string)value);
                }
                else if (propType == typeof(int))
                {
                    point = point.Field(prop.Name, (int)value);
                }
                else if (propType == typeof(long))
                {
                    point = point.Field(prop.Name, (long)value);
                }
                else if (propType == typeof(float))
                {
                    point = point.Field(prop.Name, (float)value);
                }
                else if (propType == typeof(double))
                {
                    point = point.Field(prop.Name, (double)value);
                }
                else if (propType == typeof(decimal))
                {
                    point = point.Field(prop.Name, (double)(decimal)value);
                }
                else if (propType == typeof(bool))
                {
                    point = point.Field(prop.Name, (bool)value);
                }
                else if (propType == typeof(DateTime))
                {
                    point = point.Field(prop.Name, ((DateTime)value).ToString("o"));
                }
                else if (propType == typeof(Guid))
                {
                    point = point.Field(prop.Name, value.ToString());
                }
                else
                {
                    point = point.Field(prop.Name, value.ToString());
                }
            }

            return point;
        }

        /// <summary>
        /// Maps an InfluxDB FluxRecord back to a model instance.
        /// </summary>
        /// <param name="record">The flux record to map.</param>
        /// <returns>The mapped model instance, or null if mapping fails.</returns>
        protected virtual T? MapRecordToModel(global::InfluxDB.Client.Core.Flux.Domain.FluxRecord record)
        {
            try
            {
                var model = Activator.CreateInstance<T>();
                var values = record.Values;

                // Set Guid from tag
                if (values.ContainsKey("Guid"))
                {
                    var guidStr = values["Guid"]?.ToString();
                    if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out var guid))
                    {
                        model.Guid = guid;
                    }
                }

                var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.Name != "Guid" && p.CanWrite);

                foreach (var prop in properties)
                {
                    if (!values.ContainsKey(prop.Name))
                    {
                        continue;
                    }

                    var value = values[prop.Name];
                    if (value == null)
                    {
                        continue;
                    }

                    try
                    {
                        var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                        if (propType == typeof(string))
                        {
                            prop.SetValue(model, value.ToString());
                        }
                        else if (propType == typeof(int))
                        {
                            prop.SetValue(model, Convert.ToInt32(value));
                        }
                        else if (propType == typeof(long))
                        {
                            prop.SetValue(model, Convert.ToInt64(value));
                        }
                        else if (propType == typeof(float))
                        {
                            prop.SetValue(model, Convert.ToSingle(value));
                        }
                        else if (propType == typeof(double))
                        {
                            prop.SetValue(model, Convert.ToDouble(value));
                        }
                        else if (propType == typeof(decimal))
                        {
                            prop.SetValue(model, Convert.ToDecimal(value));
                        }
                        else if (propType == typeof(bool))
                        {
                            prop.SetValue(model, Convert.ToBoolean(value));
                        }
                        else if (propType == typeof(DateTime))
                        {
                            if (value is string dateStr)
                            {
                                prop.SetValue(model, DateTime.Parse(dateStr));
                            }
                            else
                            {
                                prop.SetValue(model, Convert.ToDateTime(value));
                            }
                        }
                        else if (propType == typeof(Guid))
                        {
                            if (Guid.TryParse(value.ToString(), out var guidValue))
                            {
                                prop.SetValue(model, guidValue);
                            }
                        }
                        else
                        {
                            prop.SetValue(model, Convert.ChangeType(value, propType));
                        }
                    }
                    catch
                    {
                        // Skip properties that fail to convert
                    }
                }

                return model;
            }
            catch
            {
                return default;
            }
        }

        #endregion
    }
}
