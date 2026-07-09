using System;
using Birko.Data.MongoDB.ChangeStreams;
using Birko.Data.MongoDB.Models;
using FluentAssertions;
using MongoDB.Bson;
using Xunit;

namespace Birko.Data.MongoDB.Tests.ChangeStreams;

/// <summary>
/// CR-H072: the change-stream mapper only set DocumentKey when the BSON _id was a native Guid, which
/// never happens for the framework's string-represented / ObjectId-keyed models, so DocumentKey was
/// permanently null. These tests pin the resolver that replaced that logic.
/// </summary>
public class ChangeStreamDocumentKeyResolverTests
{
    [Fact]
    public void NativeGuidId_IsReturned()
    {
        var guid = Guid.NewGuid();
        var key = new BsonDocument("_id", new BsonBinaryData(guid, GuidRepresentation.Standard));

        ChangeStreamDocumentKeyResolver.Resolve(key, null).Should().Be(guid);
    }

    [Fact]
    public void StringGuidId_IsParsed()
    {
        var guid = Guid.NewGuid();
        var key = new BsonDocument("_id", guid.ToString());

        ChangeStreamDocumentKeyResolver.Resolve(key, null).Should().Be(guid);
    }

    [Fact]
    public void ObjectIdId_FallsBackToFullDocumentGuid()
    {
        // The canonical MongoDBModel case: _id is an auto-generated ObjectId, the Guid lives on the
        // document. Insert/update-with-lookup/replace deliver the full document.
        var guid = Guid.NewGuid();
        var key = new BsonDocument("_id", ObjectId.GenerateNewId());
        var doc = new MongoDBModel { Guid = guid };

        ChangeStreamDocumentKeyResolver.Resolve(key, doc).Should().Be(guid);
    }

    [Fact]
    public void NonGuidStringId_FallsBackToFullDocumentGuid()
    {
        var guid = Guid.NewGuid();
        var key = new BsonDocument("_id", "not-a-guid");
        var doc = new MongoDBModel { Guid = guid };

        ChangeStreamDocumentKeyResolver.Resolve(key, doc).Should().Be(guid);
    }

    [Fact]
    public void NullDocumentKey_FallsBackToFullDocumentGuid()
    {
        var guid = Guid.NewGuid();
        var doc = new MongoDBModel { Guid = guid };

        ChangeStreamDocumentKeyResolver.Resolve(null, doc).Should().Be(guid);
    }

    [Fact]
    public void ObjectIdId_WithoutFullDocument_ReturnsNull()
    {
        // Delete events deliver no full document; an ObjectId _id cannot yield the Guid.
        var key = new BsonDocument("_id", ObjectId.GenerateNewId());

        ChangeStreamDocumentKeyResolver.Resolve(key, null).Should().BeNull();
    }

    [Fact]
    public void EmptyGuidOnFullDocument_IsTreatedAsAbsent()
    {
        var key = new BsonDocument("_id", ObjectId.GenerateNewId());
        var doc = new MongoDBModel { Guid = Guid.Empty };

        ChangeStreamDocumentKeyResolver.Resolve(key, doc).Should().BeNull();
    }

    [Fact]
    public void NativeGuidId_WinsOverFullDocument()
    {
        var idGuid = Guid.NewGuid();
        var docGuid = Guid.NewGuid();
        var key = new BsonDocument("_id", new BsonBinaryData(idGuid, GuidRepresentation.Standard));
        var doc = new MongoDBModel { Guid = docGuid };

        ChangeStreamDocumentKeyResolver.Resolve(key, doc).Should().Be(idGuid);
    }
}
