using Birko.Data.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Runtime.Serialization;

namespace Birko.Data.MongoDB.Models
{
    public class MongoDBModel : AbstractModel
    {
        [DataMember]
        [BsonRepresentation(BsonType.String)]
        public override Guid? Guid { get => base.Guid; set => base.Guid = value; }
    }
}
