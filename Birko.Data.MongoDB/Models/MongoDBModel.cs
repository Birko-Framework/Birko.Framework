using Birko.Data.Models;

namespace Birko.Data.MongoDB.Models
{
    /// <summary>
    /// Base entity for the synchronous <see cref="Stores.MongoDBStore{T}"/> and its repositories.
    /// </summary>
    /// <remarks>
    /// This type declares no members of its own, deliberately. It used to re-declare
    /// <c>public override Guid? Guid</c> solely to carry <c>[BsonRepresentation(BsonType.String)]</c>,
    /// which made every derived model unserializable: <c>BsonClassMap</c> maps declared members per
    /// class, so the override and <see cref="AbstractModel.Guid"/> both claimed element name
    /// <c>Guid</c> and the map refused to freeze (TASK-214). The string representation now lives in
    /// <see cref="Serialization.MongoSerialization"/>, applied to the class map of the type that
    /// actually declares the member, so it holds for the async store's <see cref="AbstractModel"/>
    /// constraint too. The class is kept because it is the sync store's type constraint.
    /// </remarks>
    public class MongoDBModel : AbstractModel
    {
    }
}
