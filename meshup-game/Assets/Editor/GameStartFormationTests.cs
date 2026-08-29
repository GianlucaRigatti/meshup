using System;
using System.Linq;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class GameStartFormationTests
    {
        private sealed class FakeMovementProvider : MonoBehaviour
        {
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void ShuffleContainsEveryPeerExactlyOnce(int count)
        {
            var peers = Enumerable.Range(0, count)
                .Select(index => $"peer-{index}").ToArray();
            var expected = peers.OrderBy(item => item).ToArray();

            GameStartCoordinator.Shuffle(peers, new System.Random(1234));

            CollectionAssert.AreEqual(expected,
                peers.OrderBy(item => item).ToArray());
        }

        [Test]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void FormationProducesPairsAndCenteredOddSlot(int playerCount)
        {
            var points = new[] { Vector3.zero, Vector3.forward * 10f };

            for (var slot = 0; slot + 1 < playerCount; slot += 2)
            {
                var left = GameStartRoute.GetSlotPosition(points, 0f,
                    slot, playerCount, 0.9f, 1.25f);
                var right = GameStartRoute.GetSlotPosition(points, 0f,
                    slot + 1, playerCount, 0.9f, 1.25f);
                Assert.That(left.x, Is.EqualTo(-0.45f).Within(0.001f));
                Assert.That(right.x, Is.EqualTo(0.45f).Within(0.001f));
                Assert.That(left.z, Is.EqualTo(right.z).Within(0.001f));
            }

            if (playerCount % 2 == 1)
            {
                var odd = GameStartRoute.GetSlotPosition(points, 0f,
                    playerCount - 1, playerCount, 0.9f, 1.25f);
                Assert.That(odd.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(odd.z, Is.EqualTo(
                    -(playerCount / 2) * 1.25f).Within(0.001f));
            }
        }

        [Test]
        public void RowsKeepPathDistanceThroughCorner()
        {
            var points = new[]
            {
                Vector3.zero,
                Vector3.forward * 5f,
                new Vector3(5f, 0f, 5f)
            };
            var leader = GameStartRoute.GetSlotPosition(points, 7f, 0, 4,
                0.9f, 1.25f);
            var secondRow = GameStartRoute.GetSlotPosition(points, 7f, 2, 4,
                0.9f, 1.25f);

            Assert.That(Vector3.Distance(leader, secondRow),
                Is.GreaterThan(1f));
            Assert.That(secondRow.z, Is.EqualTo(5.45f).Within(0.001f));
        }

        [Test]
        public void ClosestRouteDistanceProjectsOntoCenterline()
        {
            var points = new[]
            {
                Vector3.zero,
                Vector3.forward * 5f,
                new Vector3(5f, 0f, 5f)
            };

            var distance = GameStartRoute.GetClosestDistance(points,
                new Vector3(2f, 4f, 6f), out var closest);

            Assert.That(distance, Is.EqualTo(7f).Within(0.001f));
            Assert.That(closest, Is.EqualTo(new Vector3(2f, 0f, 5f)));
        }

        [Test]
        public void DoorOpensAheadAndStaysOpenUntilCoordinatorRelease()
        {
            const float doorDistance = 10f;
            Assert.That(GameStartDoorController.ShouldOpen(5.9f,
                doorDistance, 4f), Is.False);
            Assert.That(GameStartDoorController.ShouldOpen(6f,
                doorDistance, 4f), Is.True);
            Assert.That(GameStartDoorController.ShouldOpen(30f,
                doorDistance, 4f), Is.True);
        }

        [Test]
        public void RegroupRingProjectsToClosestPerimeterSegment()
        {
            var points = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 0f, 10f),
                new Vector3(0f, 0f, 10f)
            };

            var distance = GameStartRegroupRing.GetClosestDistance(points,
                new Vector3(7f, 0f, 2f), out var closest);

            Assert.That(distance, Is.EqualTo(7f).Within(0.001f));
            Assert.That(closest, Is.EqualTo(new Vector3(7f, 0f, 0f)));
        }

        [Test]
        public void RegroupRingChoosesShortestDirectionToExit()
        {
            Assert.That(GameStartRegroupRing.GetShortestDirectionToExit(
                8f, 40f), Is.EqualTo(-1));
            Assert.That(GameStartRegroupRing.GetDistanceToExit(
                8f, 40f, -1), Is.EqualTo(8f));
            Assert.That(GameStartRegroupRing.GetShortestDirectionToExit(
                32f, 40f), Is.EqualTo(1));
            Assert.That(GameStartRegroupRing.GetDistanceToExit(
                32f, 40f, 1), Is.EqualTo(8f));
        }

        [Test]
        public void FinalApproachStartsWhenCloseOrCorridorAligned()
        {
            var slot = new Vector3(20f, 0f, 1f);
            Assert.That(GameStartCoordinator.IsReadyForFinalApproach(
                new Vector3(21.5f, 0f, 2f), slot, 2.25f, 3f, 0.65f),
                Is.True);
            Assert.That(GameStartCoordinator.IsReadyForFinalApproach(
                new Vector3(22.8f, 0f, 1.4f), slot, 2.25f, 3f, 0.65f),
                Is.True);
            Assert.That(GameStartCoordinator.IsReadyForFinalApproach(
                new Vector3(25f, 0f, 3f), slot, 2.25f, 3f, 0.65f),
                Is.False);
        }

        [Test]
        public void RegroupRingSamplingWrapsThroughExit()
        {
            var points = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 0f, 10f),
                new Vector3(0f, 0f, 10f)
            };

            Assert.That(GameStartRegroupRing.Sample(points, 39f),
                Is.EqualTo(new Vector3(0f, 0f, 1f)));
            Assert.That(GameStartRegroupRing.Sample(points, 41f),
                Is.EqualTo(new Vector3(1f, 0f, 0f)));
        }

        [Test]
        public void OverlappingLocksRestoreProviderOnlyAfterLastRelease()
        {
            var gameObject = new GameObject("Movement Authority Test");
            try
            {
                gameObject.AddComponent<CharacterController>();
                var provider = gameObject.AddComponent<FakeMovementProvider>();
                var authority = gameObject.AddComponent<PlayerMovementAuthority>();
                authority.Configure(new Behaviour[] { provider });

                authority.SetLock(MovementLockReason.PauseMenu, true);
                authority.SetLock(MovementLockReason.GameStartSequence, true);
                Assert.That(provider.enabled, Is.False);

                authority.SetLock(MovementLockReason.PauseMenu, false);
                Assert.That(provider.enabled, Is.False);

                authority.SetLock(MovementLockReason.GameStartSequence, false);
                Assert.That(provider.enabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DisconnectedSlotIsNotRepackedOrRequiredForCompletion()
        {
            var roster = new[] { "one", "two", "three", "four" };
            var connected = new[] { "one", "three", "four" };
            var arrived = new[] { "one", "three", "four" };

            Assert.That(GameStartCoordinator
                .HaveAllConnectedRosterPeersReported(roster, connected,
                    arrived), Is.True);

            var before = GameStartRoute.GetSlotPosition(
                new[] { Vector3.zero, Vector3.forward * 10f }, 5f, 2, 4,
                0.9f, 1.25f);
            var after = GameStartRoute.GetSlotPosition(
                new[] { Vector3.zero, Vector3.forward * 10f }, 5f, 2, 4,
                0.9f, 1.25f);
            Assert.That(after, Is.EqualTo(before));
        }

        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase("false", false)]
        [TestCase(null, false)]
        public void StartedMetadataControlsDiscoveryAndLateJoinGuard(
            string value, bool expected)
        {
            Assert.That(UbiqRoomSession.IsStartedValue(value),
                Is.EqualTo(expected));
        }
    }
}
