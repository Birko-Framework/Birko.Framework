using ProtoBuf;

namespace Birko.Serialization.Tests.TestResources
{
    [ProtoContract]
    public class TestPayload
    {
        [ProtoMember(1)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Value { get; set; }

        [ProtoMember(3)]
        public bool IsActive { get; set; }
    }

    [ProtoContract]
    public class NestedPayload
    {
        [ProtoMember(1)]
        public string Title { get; set; } = string.Empty;

        [ProtoMember(2)]
        public List<string> Tags { get; set; } = new();

        [ProtoMember(3)]
        public TestPayload? Inner { get; set; }
    }
}
