using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Ubiq.Messaging;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ubiq.SceneSwitcher.Tests
{
    public sealed class UbiqRoomGatewayTests
    {
        private GameObject root;
        private RoomClient client;
        private UbiqRoomGateway gateway;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Gateway Test");
            root.SetActive(false);
            root.AddComponent<NetworkScene>();
            client = root.AddComponent<RoomClient>();
            gateway = new UbiqRoomGateway(client);
        }

        [TearDown]
        public void TearDown()
        {
            gateway.Dispose();
            Object.DestroyImmediate(root);
        }

        [UnityTest]
        public IEnumerator LeaveIgnoresDelayedSharedRoomResponse()
        {
            var completed = false;
            var result = default(GatewayRoomResult);
            Complete(gateway.EnterPrivateAsync(2f, CancellationToken.None));

            client.OnJoinedRoom.Invoke(new FakeRoom("Shared", "shared", "abcd", true));
            yield return null;
            Assert.That(completed, Is.False);

            client.OnJoinedRoom.Invoke(new FakeRoom(string.Empty, "private", "wxyz", false));
            while (!completed) yield return null;

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Room.Uuid, Is.EqualTo("private"));

            async void Complete(Awaitable<GatewayRoomResult> operation)
            {
                result = await operation;
                completed = true;
            }
        }

        [UnityTest]
        public IEnumerator JoinIgnoresResponseForAnotherCode()
        {
            var completed = false;
            var result = default(GatewayRoomResult);
            Complete(gateway.JoinAsync("abcd", 2f, CancellationToken.None));

            client.OnJoinedRoom.Invoke(new FakeRoom("Other", "other", "zzzz", true));
            yield return null;
            Assert.That(completed, Is.False);

            client.OnJoinedRoom.Invoke(new FakeRoom("Expected", "expected", "ABCD", true));
            while (!completed) yield return null;

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Room.Uuid, Is.EqualTo("expected"));

            async void Complete(Awaitable<GatewayRoomResult> operation)
            {
                result = await operation;
                completed = true;
            }
        }

        private sealed class FakeRoom : IRoom
        {
            private readonly Dictionary<string, string> properties = new();
            public string Name { get; }
            public string UUID { get; }
            public string JoinCode { get; }
            public bool Publish { get; }

            public FakeRoom(string name, string uuid, string code, bool publish)
            {
                Name = name;
                UUID = uuid;
                JoinCode = code;
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
