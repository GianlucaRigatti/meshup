using System;
using System.Collections;
using System.Reflection;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using Ubiq.Rooms;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class GameStartMovementTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private GameStartCoordinator coordinator;
        private PlayerMovementAuthority player;
        private UbiqRoomSession session;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Opening movement test");
            var sessionObject = new GameObject("Inactive room session");
            sessionObject.transform.SetParent(root.transform);
            sessionObject.SetActive(false);
            session = sessionObject.AddComponent<UbiqRoomSession>();
            Set(session, "roomClient", sessionObject.GetComponent<RoomClient>());
            var route = root.AddComponent<GameStartRoute>();
            route.Configure(new[] { Marker(Vector3.zero), Marker(Vector3.back * 10f) }, 0.9f, 1.25f);
            var ring = root.AddComponent<GameStartRegroupRing>();
            ring.Configure(new[]
            {
                Marker(Vector3.zero), Marker(Vector3.right * 10f),
                Marker(new Vector3(10f, 0f, 10f)), Marker(Vector3.forward * 10f)
            });
            player = Marker(Vector3.zero).gameObject.AddComponent<PlayerMovementAuthority>();
            player.GetComponent<CharacterController>().enabled = false;
            player.SetLock(MovementLockReason.GameStartSequence, true);
            coordinator = root.AddComponent<GameStartCoordinator>();
            Set(coordinator, "session", session);
            Set(coordinator, "route", route);
            Set(coordinator, "regroupRing", ring);
            Set(coordinator, "player", player);
            Set(coordinator, "roster", new[] { session.LocalPeerId, "peer" });
            Set(coordinator, "localSlot", 0);
            var phaseType = typeof(GameStartCoordinator).GetNestedType("SequencePhase", BindingFlags.NonPublic);
            Set(coordinator, "phase", Enum.Parse(phaseType, "Regrouping"));
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        [TestCase(5f, 5f, "A player could not reach the waiting-room ring.")]
        [TestCase(5f, 0f, "The waiting-room ring was blocked.")]
        [TestCase(0f, 0f, "A player could not reach the lineup.")]
        public void BlockedRegroupStageCancelsWithoutReportingAlignment(float x, float z, string error)
        {
            player.transform.position = new Vector3(x, 0f, z);
            Set(coordinator, "regroupSpeed", 0f);
            Set(coordinator, "blockedTimeout", 0f);

            RunRegroup();

            Assert.That(coordinator.IsIdle, Is.True);
            Assert.That(coordinator.StatusMessage, Is.EqualTo(error));
            Assert.That(player.ManualMovementBlocked, Is.False);
            Assert.That((IEnumerable)Get(coordinator, "alignedPeers"), Is.Empty);
        }

        [Test]
        public void PlayerAlreadyInPlaceStartsDoorDelayWithoutMovingOrUnlocking()
        {
            Set(coordinator, "roster", new[] { session.LocalPeerId });
            player.transform.position = Vector3.up * 0.4f;

            RunRegroup();

            Assert.That(coordinator.StatusMessage, Is.EqualTo("Opening the doors…"));
            Assert.That(player.BodyPosition, Is.EqualTo(Vector3.up * 0.4f));
            Assert.That(player.ActiveLocks, Is.EqualTo(MovementLockReason.GameStartSequence));
            Assert.That((IEnumerable)Get(coordinator, "alignedPeers"), Does.Contain(session.LocalPeerId));
        }

        private void RunRegroup() => coordinator.StartCoroutine((IEnumerator)
            typeof(GameStartCoordinator).GetMethod("Regroup", Private).Invoke(coordinator, null));

        private Transform Marker(Vector3 position)
        {
            var marker = new GameObject("Marker").transform;
            marker.SetParent(root.transform);
            marker.position = position;
            return marker;
        }

        private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    }
}
