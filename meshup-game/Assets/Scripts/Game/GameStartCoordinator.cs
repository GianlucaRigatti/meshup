using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meshup.Multiplayer;
using Ubiq.Messaging;
using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class GameStartCoordinator : MonoBehaviour
    {
        private enum SequencePhase
        {
            Idle,
            Preparing,
            Regrouping,
            Walking,
            WaitingForGroup,
            Complete
        }

        private enum MessageKind
        {
            Prepare,
            Ready,
            Aligned,
            Walk,
            Arrived,
            Cancel
        }

        [Serializable]
        private struct SequenceMessage
        {
            public int kind;
            public string sequenceId;
            public string creatorId;
            public string peerId;
            public string[] roster;
        }

        [Header("Scene")]
        [SerializeField] private GameStartRoute route;
        [SerializeField] private PlayerMovementAuthority player;

        [Header("Motion")]
        [SerializeField] private float walkSpeed = 1.5f;
        [SerializeField] private float walkAcceleration = 2.5f;
        [SerializeField] private float regroupSpeed = 2.2f;
        [SerializeField] private float regroupAcceleration = 5f;
        [SerializeField] private float arrivalTolerance = 0.08f;
        [SerializeField] private float preparationTimeout = 10f;
        [SerializeField] private float alignmentTimeout = 45f;
        [SerializeField] private float blockedTimeout = 3f;

        private readonly HashSet<string> readyPeers = new(StringComparer.Ordinal);
        private readonly HashSet<string> alignedPeers = new(StringComparer.Ordinal);
        private readonly HashSet<string> arrivedPeers = new(StringComparer.Ordinal);
        private NetworkContext context;
        private UbiqRoomSession session;
        private SequencePhase phase;
        private string sequenceId = string.Empty;
        private string creatorId = string.Empty;
        private string[] roster = Array.Empty<string>();
        private int localSlot = -1;
        private Coroutine motion;
        private bool contextRegistered;

        public bool IsIdle => phase == SequencePhase.Idle;
        public bool IsRunning => phase is SequencePhase.Preparing
            or SequencePhase.Regrouping or SequencePhase.Walking
            or SequencePhase.WaitingForGroup;
        public string StatusMessage { get; private set; } = string.Empty;

        public void Configure(GameStartRoute formationRoute,
            PlayerMovementAuthority localPlayer)
        {
            route = formationRoute;
            player = localPlayer;
        }

        private void Start()
        {
            session = UbiqRoomSession.Instance;
            if (session == null || route == null || player == null)
            {
                enabled = false;
                Debug.LogError("[GameStart] Coordinator references are incomplete.");
                return;
            }

            context = NetworkScene.Register(this);
            contextRegistered = true;
            session.ParticipantsChanged += HandleParticipantsChanged;
        }

        public bool TryStartSequence()
        {
            if (!IsIdle || session == null
                || session.State != RoomSessionState.InGame)
            {
                return false;
            }
            if (!session.IsRoomCreator)
            {
                StatusMessage = "Only the room creator can start the game.";
                return false;
            }

            var participants = session.GetParticipantIds().ToArray();
            if (participants.Length < 2)
            {
                StatusMessage = "Waiting for another player.";
                return false;
            }
            if (!session.TrySetGameStarted(true))
            {
                StatusMessage = "The room could not be locked for game start.";
                return false;
            }

            phase = SequencePhase.Preparing;
            StatusMessage = "Locking the room…";
            StartCoroutine(BeginAfterRoomLock());
            return true;
        }

        private IEnumerator BeginAfterRoomLock()
        {
            var elapsed = 0f;
            while (!session.GameStarted && elapsed < preparationTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!session.GameStarted)
            {
                CancelSequence("The room could not be locked for game start.",
                    true);
                yield break;
            }

            // Snapshot only after the lock is acknowledged. A peer that won
            // the race before the lock is therefore part of this roster;
            // peers arriving afterward reject the started room in the lobby.
            var participants = session.GetParticipantIds().ToArray();
            if (participants.Length < 2)
            {
                CancelSequence("Waiting for another player.", true);
                yield break;
            }

            Shuffle(participants, new System.Random());
            var message = new SequenceMessage
            {
                kind = (int)MessageKind.Prepare,
                sequenceId = Guid.NewGuid().ToString("N"),
                creatorId = session.LocalPeerId,
                roster = participants
            };
            BeginPreparation(message);
            Broadcast(message);
            StartCoroutine(PreparationDeadline(message.sequenceId));
        }

        public static void Shuffle<T>(T[] values, System.Random random)
        {
            if (values == null || random == null)
            {
                return;
            }
            for (var i = values.Length - 1; i > 0; i--)
            {
                var other = random.Next(i + 1);
                (values[i], values[other]) = (values[other], values[i]);
            }
        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage networkMessage)
        {
            var message = networkMessage.FromJson<SequenceMessage>();
            var kind = (MessageKind)message.kind;
            if (kind == MessageKind.Prepare)
            {
                if (phase == SequencePhase.Idle
                    && session != null
                    && string.Equals(message.creatorId, session.CreatorPeerId,
                        StringComparison.Ordinal))
                {
                    BeginPreparation(message);
                }
                return;
            }

            if (!string.Equals(message.sequenceId, sequenceId,
                StringComparison.Ordinal))
            {
                return;
            }

            switch (kind)
            {
                case MessageKind.Ready:
                    AddPeer(readyPeers, message.peerId);
                    if (AllCurrentPeersAreIn(readyPeers))
                    {
                        phase = SequencePhase.Regrouping;
                        StatusMessage = "Forming pairs…";
                    }
                    break;
                case MessageKind.Aligned:
                    AddPeer(alignedPeers, message.peerId);
                    TryBeginWalk();
                    break;
                case MessageKind.Walk:
                    BeginWalk();
                    break;
                case MessageKind.Arrived:
                    AddPeer(arrivedPeers, message.peerId);
                    TryReleaseGroup();
                    break;
                case MessageKind.Cancel:
                    // Any participant may detect a blocked route. Let the
                    // creator reopen the room even when another peer was the
                    // one that initiated cancellation.
                    CancelSequence(message.peerId ?? "Game start was cancelled.", true);
                    break;
            }
        }

        private void BeginPreparation(SequenceMessage message)
        {
            if (message.roster == null || message.roster.Length < 2
                || route.WaypointCount < 2)
            {
                return;
            }

            sequenceId = message.sequenceId;
            creatorId = message.creatorId;
            roster = message.roster;
            localSlot = Array.IndexOf(roster, session.LocalPeerId);
            if (localSlot < 0)
            {
                return;
            }

            readyPeers.Clear();
            alignedPeers.Clear();
            arrivedPeers.Clear();
            phase = SequencePhase.Preparing;
            StatusMessage = "Preparing players…";
            player.SetLock(MovementLockReason.GameStartSequence, true);
            AddPeer(readyPeers, session.LocalPeerId);
            BroadcastPeer(MessageKind.Ready);
            motion = StartCoroutine(Regroup());
            StartCoroutine(AlignmentDeadline(sequenceId));
        }

        private IEnumerator Regroup()
        {
            var speed = 0f;
            var target = route.GetSlotPosition(0f, localSlot, roster.Length);
            var previous = player.transform.position;
            var blockedFor = 0f;
            // CharacterController ground contact may keep the rig root a skin
            // width above or below the authored marker. Formation readiness is
            // therefore based on the floor plane; movement itself remains 3D
            // so the controller still follows stairs and collisions.
            while (PlanarDistance(player.transform.position, target)
                > arrivalTolerance)
            {
                speed = Mathf.MoveTowards(speed, regroupSpeed,
                    regroupAcceleration * Time.deltaTime);
                player.MoveTowards(target, speed * Time.deltaTime);
                TrackBlocked(previous, target, ref blockedFor);
                previous = player.transform.position;
                if (blockedFor >= blockedTimeout)
                {
                    BroadcastCancel("A player could not reach the lineup.");
                    yield break;
                }
                yield return null;
            }

            motion = null;
            AddPeer(alignedPeers, session.LocalPeerId);
            BroadcastPeer(MessageKind.Aligned);
            StatusMessage = "Waiting for the group…";
            TryBeginWalk();
        }

        private void TryBeginWalk()
        {
            if (phase is SequencePhase.Walking or SequencePhase.WaitingForGroup
                or SequencePhase.Complete || !AllCurrentPeersAreIn(alignedPeers))
            {
                return;
            }

            var message = CreatePeerMessage(MessageKind.Walk);
            Broadcast(message);
            BeginWalk();
        }

        private void BeginWalk()
        {
            if (phase is SequencePhase.Walking or SequencePhase.WaitingForGroup
                or SequencePhase.Complete)
            {
                return;
            }
            if (motion != null)
            {
                StopCoroutine(motion);
            }
            phase = SequencePhase.Walking;
            StatusMessage = "Walking to the game room…";
            motion = StartCoroutine(WalkRoute());
        }

        private IEnumerator WalkRoute()
        {
            var leaderDistance = 0f;
            var speed = 0f;
            var previous = player.transform.position;
            var blockedFor = 0f;
            while (leaderDistance < route.Length)
            {
                speed = Mathf.MoveTowards(speed, walkSpeed,
                    walkAcceleration * Time.deltaTime);
                leaderDistance = Mathf.Min(route.Length,
                    leaderDistance + speed * Time.deltaTime);
                var target = route.GetSlotPosition(leaderDistance,
                    localSlot, roster.Length);
                player.MoveTowards(target, speed * Time.deltaTime * 1.35f);
                TrackBlocked(previous, target, ref blockedFor);
                previous = player.transform.position;
                if (blockedFor >= blockedTimeout)
                {
                    BroadcastCancel("The corridor route was blocked.");
                    yield break;
                }
                yield return null;
            }

            var destination = route.GetSlotPosition(route.Length,
                localSlot, roster.Length);
            while (PlanarDistance(player.transform.position, destination)
                > arrivalTolerance)
            {
                player.MoveTowards(destination, walkSpeed * Time.deltaTime);
                yield return null;
            }

            motion = null;
            phase = SequencePhase.WaitingForGroup;
            StatusMessage = "Waiting for the group…";
            AddPeer(arrivedPeers, session.LocalPeerId);
            BroadcastPeer(MessageKind.Arrived);
            TryReleaseGroup();
        }

        private void TryReleaseGroup()
        {
            if (phase != SequencePhase.WaitingForGroup
                || !AllCurrentPeersAreIn(arrivedPeers))
            {
                return;
            }

            phase = SequencePhase.Complete;
            StatusMessage = string.Empty;
            player.SetLock(MovementLockReason.GameStartSequence, false);
        }

        private IEnumerator PreparationDeadline(string expectedSequence)
        {
            yield return new WaitForSecondsRealtime(preparationTimeout);
            if (string.Equals(expectedSequence, sequenceId,
                    StringComparison.Ordinal)
                && phase == SequencePhase.Preparing
                && !AllCurrentPeersAreIn(readyPeers))
            {
                BroadcastCancel("Not every player was ready in time.");
            }
        }

        private IEnumerator AlignmentDeadline(string expectedSequence)
        {
            yield return new WaitForSecondsRealtime(alignmentTimeout);
            if (string.Equals(expectedSequence, sequenceId,
                    StringComparison.Ordinal)
                && phase is SequencePhase.Preparing or SequencePhase.Regrouping
                && !AllCurrentPeersAreIn(alignedPeers))
            {
                BroadcastCancel("Players could not form up in time.");
            }
        }

        private void TrackBlocked(Vector3 previous, Vector3 target,
            ref float blockedFor)
        {
            var needsMovement = PlanarDistance(player.transform.position,
                target) > arrivalTolerance * 2f;
            var moved = PlanarDistance(previous, player.transform.position);
            blockedFor = needsMovement && moved < 0.002f
                ? blockedFor + Time.deltaTime
                : 0f;
        }

        private static float PlanarDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private bool AllCurrentPeersAreIn(HashSet<string> set)
        {
            if (roster.Length == 0 || session == null)
            {
                return false;
            }

            return HaveAllConnectedRosterPeersReported(roster,
                session.GetParticipantIds(), set);
        }

        public static bool HaveAllConnectedRosterPeersReported(
            IEnumerable<string> assignedRoster,
            IEnumerable<string> connectedPeers,
            IEnumerable<string> reportedPeers)
        {
            if (assignedRoster == null || connectedPeers == null
                || reportedPeers == null)
            {
                return false;
            }

            var connected = new HashSet<string>(connectedPeers,
                StringComparer.Ordinal);
            var reported = new HashSet<string>(reportedPeers,
                StringComparer.Ordinal);
            return assignedRoster.Where(connected.Contains)
                .All(reported.Contains);
        }

        private void HandleParticipantsChanged()
        {
            if (!IsRunning)
            {
                return;
            }
            if (phase is SequencePhase.Preparing or SequencePhase.Regrouping)
            {
                if (AllCurrentPeersAreIn(alignedPeers))
                {
                    TryBeginWalk();
                }
            }
            else if (phase == SequencePhase.WaitingForGroup)
            {
                TryReleaseGroup();
            }
        }

        private void BroadcastPeer(MessageKind kind)
        {
            Broadcast(CreatePeerMessage(kind));
        }

        private SequenceMessage CreatePeerMessage(MessageKind kind)
        {
            return new SequenceMessage
            {
                kind = (int)kind,
                sequenceId = sequenceId,
                creatorId = creatorId,
                peerId = session?.LocalPeerId ?? string.Empty
            };
        }

        private void BroadcastCancel(string reason)
        {
            var message = CreatePeerMessage(MessageKind.Cancel);
            message.peerId = reason;
            Broadcast(message);
            CancelSequence(reason, true);
        }

        private void Broadcast(SequenceMessage message)
        {
            if (contextRegistered && context.Scene != null)
            {
                context.SendJson(message);
            }
        }

        private static void AddPeer(HashSet<string> set, string peerId)
        {
            if (!string.IsNullOrEmpty(peerId))
            {
                set.Add(peerId);
            }
        }

        private void CancelSequence(string reason, bool reopenRoom)
        {
            if (motion != null)
            {
                StopCoroutine(motion);
                motion = null;
            }
            player?.SetLock(MovementLockReason.GameStartSequence, false);
            if (reopenRoom && session != null && session.IsRoomCreator)
            {
                session.TrySetGameStarted(false);
            }
            phase = SequencePhase.Idle;
            StatusMessage = reason ?? string.Empty;
            sequenceId = string.Empty;
            creatorId = string.Empty;
            roster = Array.Empty<string>();
            localSlot = -1;
            readyPeers.Clear();
            alignedPeers.Clear();
            arrivedPeers.Clear();
        }

        private void OnDisable()
        {
            player?.SetLock(MovementLockReason.GameStartSequence, false);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.ParticipantsChanged -= HandleParticipantsChanged;
            }
        }
    }
}
