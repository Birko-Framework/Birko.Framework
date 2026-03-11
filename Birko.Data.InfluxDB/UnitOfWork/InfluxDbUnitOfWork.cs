using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace Birko.Data.InfluxDB.UnitOfWork;

/// <summary>
/// Collects InfluxDB points for batched writing.
/// </summary>
public sealed class BatchPointContext
{
    internal readonly List<PointData> Points = new();

    /// <summary>
    /// The bucket to write to.
    /// </summary>
    public string Bucket { get; }

    /// <summary>
    /// The organization to write to.
    /// </summary>
    public string Organization { get; }

    internal BatchPointContext(string bucket, string organization)
    {
        Bucket = bucket;
        Organization = organization;
    }

    /// <summary>
    /// Enqueues a point for batch writing.
    /// </summary>
    public void AddPoint(PointData point)
    {
        Points.Add(point);
    }

    /// <summary>
    /// Enqueues multiple points for batch writing.
    /// </summary>
    public void AddPoints(IEnumerable<PointData> points)
    {
        Points.AddRange(points);
    }
}

/// <summary>
/// InfluxDB "Unit of Work" — collects points and writes them as a single batch.
/// NOTE: InfluxDB has limited transaction support. This batches writes for efficiency,
/// but individual points may fail independently.
/// </summary>
public sealed class InfluxDbUnitOfWork : IUnitOfWork<BatchPointContext>
{
    private readonly global::InfluxDB.Client.InfluxDBClient _client;
    private readonly string _bucket;
    private readonly string _organization;
    private BatchPointContext? _context;
    private bool _disposed;

    public bool IsActive => _context is not null;
    public BatchPointContext? Context => _context;

    public InfluxDbUnitOfWork(global::InfluxDB.Client.InfluxDBClient client, string bucket, string organization)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
        _organization = organization ?? throw new ArgumentNullException(nameof(organization));
    }

    /// <summary>
    /// Creates from a configured store.
    /// </summary>
    public static InfluxDbUnitOfWork FromStore<T>(Stores.AsyncInfluxDBStore<T> store)
        where T : Data.Models.AbstractModel
    {
        var client = store.Client
            ?? throw new InvalidOperationException("Store client is not initialized. Call SetSettings() first.");
        var settings = (Stores.Settings?)typeof(Stores.AsyncInfluxDBStore<T>)
            .GetField("_settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(store)
            ?? throw new InvalidOperationException("Store settings are not initialized.");
        return new InfluxDbUnitOfWork(client.Client, settings.Bucket, settings.Organization);
    }

    public Task BeginAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive)
            throw new TransactionAlreadyActiveException();

        _context = new BatchPointContext(_bucket, _organization);
        return Task.CompletedTask;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        if (_context!.Points.Count > 0)
        {
            var writeApi = _client.GetWriteApiAsync();
            await writeApi.WritePointsAsync(_context.Points, _bucket, _organization, ct);
        }

        _context = null;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
            throw new NoActiveTransactionException();

        // Discard collected points — nothing was written yet.
        _context = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _context = null;
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _context = null;
        }
    }
}
