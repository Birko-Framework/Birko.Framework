using System;
using Birko.Data.Models;
using Birko.Data.MongoDB.Models;

namespace Birko.Framework.Examples
{
    /// <summary>
    /// Example model for SQL, JSON, and ElasticSearch store demonstrations.
    /// Extends AbstractLogModel which provides Guid, CreatedAt, UpdatedAt.
    /// </summary>
    public class ExampleProduct : AbstractLogModel
    {
        public string? Name { get; set; }
        public decimal Price { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public int StockQuantity { get; set; }
    }

    /// <summary>
    /// Example model for MongoDB store demonstrations.
    /// Extends MongoDBModel which provides BSON-compatible Guid serialization.
    /// </summary>
    public class ExampleUser : MongoDBModel
    {
        public string? Username { get; set; }
        public string? Email { get; set; }
        public int Age { get; set; }
    }

    /// <summary>
    /// Example model for sync/tenant demonstrations.
    /// </summary>
    public class ExampleDocument : AbstractLogModel
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public Guid? TenantGuid { get; set; }
    }
}
