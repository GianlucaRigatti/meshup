using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Ubiq.Rooms;

namespace Ubiq.SceneSwitcher.Tests
{
    public sealed class RoomCompatibilityTests
    {
        [Test]
        public void CopyReadsKnownMetadataAndNormalizesCode()
        {
            var room = new FakeRoom("Room", "uuid", "AbCd", true)
            {
                [RoomCompatibility.ApplicationKey] = "app",
                [RoomCompatibility.SceneKey] = "scene",
                [RoomCompatibility.ProtocolKey] = "3"
            };

            var copy = RoomCompatibility.Copy(room);

            Assert.That(copy.Name, Is.EqualTo("Room"));
            Assert.That(copy.JoinCode, Is.EqualTo("abcd"));
            Assert.That(copy.ApplicationId, Is.EqualTo("app"));
            Assert.That(copy.SceneId, Is.EqualTo("scene"));
            Assert.That(copy.ProtocolVersion, Is.EqualTo(3));
            Assert.That(copy.IsPublished, Is.True);
        }

        [Test]
        public void CopyToleratesMissingAndMalformedMetadata()
        {
            var room = new FakeRoom("Room", "uuid", "code", false)
            {
                [RoomCompatibility.ProtocolKey] = "not-an-integer"
            };

            var copy = RoomCompatibility.Copy(room);

            Assert.That(copy.ApplicationId, Is.Empty);
            Assert.That(copy.SceneId, Is.Empty);
            Assert.That(copy.ProtocolVersion, Is.Zero);
        }

        [TestCase("app", "scene", 1, true)]
        [TestCase("other", "scene", 1, false)]
        [TestCase("app", "other", 1, false)]
        [TestCase("app", "scene", 2, false)]
        public void StrictCompatibilityRequiresAllValues(string app, string scene,
            int protocol, bool expected)
        {
            var room = new RoomSummary("Room", "uuid", "code", true,
                "app", "scene", 1);
            Assert.That(RoomCompatibility.IsCompatible(room, app, scene, protocol),
                Is.EqualTo(expected));
        }

        [TestCase(" ABCD ", "abcd")]
        [TestCase(null, "")]
        [TestCase("", "")]
        public void JoinCodeNormalizationIsDeterministic(string input, string expected)
        {
            Assert.That(RoomCompatibility.NormalizeJoinCode(input), Is.EqualTo(expected));
        }

        [Test]
        public void CopyDoesNotRetainTheSourceRoom()
        {
            var room = new FakeRoom("Original", "uuid", "code", true)
            {
                [RoomCompatibility.ApplicationKey] = "app"
            };
            var copy = RoomCompatibility.Copy(room);

            room[RoomCompatibility.ApplicationKey] = "changed";

            Assert.That(copy.ApplicationId, Is.EqualTo("app"));
        }

        [Test]
        public void CompatibilityFailureIdentifiesTheFirstMismatch()
        {
            var room = new RoomSummary("Room", "uuid", "code", true,
                "wrong-app", "wrong-scene", 99);

            var failure = RoomCompatibility.GetCompatibilityFailure(room,
                "app", "scene", 1);

            Assert.That(failure.Code,
                Is.EqualTo(RoomSceneFailureCode.IncompatibleApplication));
            Assert.That(failure.Message, Is.Not.Empty);
        }

        [Test]
        public void EmptySummaryIsNotCompatible()
        {
            Assert.That(RoomCompatibility.IsCompatible(RoomSummary.Empty,
                string.Empty, string.Empty, 0), Is.False);
        }

        private sealed class FakeRoom : IRoom
        {
            private readonly Dictionary<string, string> properties = new();

            public string Name { get; }
            public string UUID { get; }
            public string JoinCode { get; }
            public bool Publish { get; }

            public FakeRoom(string name, string uuid, string joinCode, bool publish)
            {
                Name = name;
                UUID = uuid;
                JoinCode = joinCode;
                Publish = publish;
            }

            public string this[string key]
            {
                get => properties.TryGetValue(key, out var value) ? value : null;
                set => properties[key] = value;
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                properties.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
