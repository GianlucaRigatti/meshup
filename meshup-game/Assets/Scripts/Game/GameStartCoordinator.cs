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
        // Temporary solo shortcut for testing the full opening sequence.
        private const KeyCode SoloTestKey = KeyCode.F6;
        private const string PoseidonClipResourcePath = "PoseidonGreeting";

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
        [SerializeField] private GameStartRegroupRing regroupRing;
        [SerializeField] private PlayerMovementAuthority player;
        [SerializeField] private GameStartDoorController[] doors =
            Array.Empty<GameStartDoorController>();

        [Header("Poseidon Audio")]
        [SerializeField] private Transform poseidon;
        [SerializeField] private Transform poseidonVisual;
        [SerializeField, Range(0f, 1f)] private float poseidonVolume = 0.65f;
        [SerializeField, Min(0f)] private float poseidonTriggerDistance = 5.6f;
        [SerializeField, Min(0f)] private float poseidonLightIntensity = 3.5f;

        [Header("Motion")]
        [SerializeField] private float walkSpeed = 1.5f;
        [SerializeField] private float walkAcceleration = 2.5f;
        [SerializeField, Min(0f), Tooltip(
            "Seconds between opening the first door and starting the group walk.")]
        private float doorOpeningDelay = 4f;
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
        private AudioSource poseidonAudioSource;
        private Light poseidonSpeakingLight;
        private Coroutine poseidonLightPulse;
        private bool poseidonCuePlayed;

        public bool IsIdle => phase == SequencePhase.Idle;
        public bool IsComplete => phase == SequencePhase.Complete;
        public bool IsRunning => phase is SequencePhase.Preparing
            or SequencePhase.Regrouping or SequencePhase.Walking
            or SequencePhase.WaitingForGroup;
        public string StatusMessage { get; private set; } = string.Empty;
        public float DoorOpeningDelay => Mathf.Max(0f, doorOpeningDelay);

        public event Action Completed;

        public void Configure(GameStartRoute formationRoute,
            GameStartRegroupRing waitingRoomRing,
            PlayerMovementAuthority localPlayer,
            GameStartDoorController[] routeDoors = null)
        {
            route = formationRoute;
            regroupRing = waitingRoomRing;
            player = localPlayer;
            doors = routeDoors ?? Array.Empty<GameStartDoorController>();
        }

        private void Start()
        {
            session = UbiqRoomSession.Instance;
            if (session == null || route == null || regroupRing == null
                || player == null)
            {
                enabled = false;
                Debug.LogError("[GameStart] Coordinator references are incomplete.");
                return;
            }

            context = NetworkScene.Register(this);
            contextRegistered = true;
            session.ParticipantsChanged += HandleParticipantsChanged;
            ConfigurePoseidonAudio();
        }

        private void Update()
        {
            if (Input.GetKeyDown(SoloTestKey))
            {
                TryStartSoloTest();
            }
        }

        private void ConfigurePoseidonAudio()
        {
            if (poseidon == null)
            {
                Debug.LogWarning("[GameStart] Poseidon is not assigned; "
                    + "the walk-by greeting will not play.", this);
                return;
            }

            var clip = Resources.Load<AudioClip>(PoseidonClipResourcePath);
            if (clip == null)
            {
                Debug.LogWarning($"[GameStart] Poseidon sound not found at "
                    + $"Resources/{PoseidonClipResourcePath}.", this);
                return;
            }

            var anchor = new GameObject("Poseidon Voice Anchor").transform;
            anchor.SetParent(poseidon, false);
            anchor.position = GetPoseidonHeadPosition();

            poseidonAudioSource = anchor.gameObject.AddComponent<AudioSource>();
            poseidonAudioSource.clip = clip;
            poseidonAudioSource.playOnAwake = false;
            poseidonAudioSource.loop = false;
            poseidonAudioSource.volume = poseidonVolume;
            poseidonAudioSource.spatialBlend = 1f;
            poseidonAudioSource.spread = 0f;
            poseidonAudioSource.minDistance = 6f;
            poseidonAudioSource.maxDistance = 22f;

            poseidonSpeakingLight = anchor.gameObject.AddComponent<Light>();
            poseidonSpeakingLight.type = LightType.Point;
            poseidonSpeakingLight.color = new Color(0.08f, 0.58f, 1f);
            poseidonSpeakingLight.range = 7f;
            poseidonSpeakingLight.intensity = 0f;
            poseidonSpeakingLight.shadows = LightShadows.None;
        }

        private Vector3 GetPoseidonHeadPosition()
        {
            var visual = poseidonVisual != null ? poseidonVisual : poseidon;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return poseidon.position + Vector3.up * 1.5f;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            var head = bounds.center;
            head.y = bounds.max.y - bounds.size.y * 0.14f;
            return head;
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

        public bool TryStartSoloTest()
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
            if (session.ParticipantCount != 1)
            {
                StatusMessage = "F6 solo start requires an empty room.";
                return false;
            }
            if (!session.TrySetGameStarted(true))
            {
                StatusMessage = "The room could not be locked for game start.";
                return false;
            }

            var message = new SequenceMessage
            {
                kind = (int)MessageKind.Prepare,
                sequenceId = Guid.NewGuid().ToString("N"),
                creatorId = session.LocalPeerId,
                roster = new[] { session.LocalPeerId }
            };
            BeginPreparation(message, true);
            return true;
        }

        /// <summary>
        /// Reports that this client's rig crossed the finish trigger at the
        /// glass-room entrance. The route endpoint is deliberately not used
        /// as the completion condition: stair collision recovery may leave a
        /// rig slightly offset from that authored point.
        /// </summary>
        public void NotifyLocalPlayerPassedFinish()
        {
            if (phase is not (SequencePhase.Walking
                or SequencePhase.WaitingForGroup)
                || session == null
                || string.IsNullOrEmpty(session.LocalPeerId)
                || arrivedPeers.Contains(session.LocalPeerId))
            {
                return;
            }

            AddPeer(arrivedPeers, session.LocalPeerId);
            BroadcastPeer(MessageKind.Arrived);
            StatusMessage = "Waiting for the group…";
            TryReleaseGroup();
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

        private void BeginPreparation(SequenceMessage message,
            bool allowSolo = false)
        {
            if (message.roster == null
                || message.roster.Length < (allowSolo ? 1 : 2)
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
            poseidonCuePlayed = false;
            StopPoseidonCue();
            CloseDoors();
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
            var formationTarget = route.GetSlotPosition(0f, localSlot,
                roster.Length);
            var joinDistance = regroupRing.GetClosestDistance(
                player.transform.position, out var target);
            var ringDirection = regroupRing.GetShortestDirectionToExit(
                joinDistance);
            var previous = player.transform.position;
            var blockedFor = 0f;
            StatusMessage = "Moving to the waiting-room ring…";
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
                    BroadcastCancel(
                        "A player could not reach the waiting-room ring.");
                    yield break;
                }
                yield return null;
            }

            var ringDistance = regroupRing.GetDistanceToExit(joinDistance,
                ringDirection);
            var ringTravelled = 0f;
            speed = 0f;
            blockedFor = 0f;
            previous = player.transform.position;
            StatusMessage = "Following the waiting-room ring…";
            while (ringTravelled < ringDistance
                && !IsReadyForFinalApproach(player.transform.position,
                    formationTarget, 2.25f, 3f, 0.65f))
            {
                speed = Mathf.MoveTowards(speed, regroupSpeed,
                    regroupAcceleration * Time.deltaTime);
                ringTravelled = Mathf.Min(ringDistance,
                    ringTravelled + speed * Time.deltaTime);
                target = regroupRing.Sample(joinDistance
                    + ringDirection * ringTravelled);
                player.MoveTowards(target, speed * Time.deltaTime * 1.35f);
                var moved = PlanarDistance(previous,
                    player.transform.position);
                blockedFor = ringDistance - ringTravelled > arrivalTolerance
                    && moved < 0.002f
                        ? blockedFor + Time.deltaTime
                        : 0f;
                previous = player.transform.position;
                if (blockedFor >= blockedTimeout)
                {
                    BroadcastCancel("The waiting-room ring was blocked.");
                    yield break;
                }
                yield return null;
            }

            target = formationTarget;
            speed = 0f;
            blockedFor = 0f;
            previous = player.transform.position;
            StatusMessage = "Moving to assigned place…";
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

        public static bool IsReadyForFinalApproach(Vector3 position,
            Vector3 assignedSlot, float closeRadius,
            float corridorLongitudinalRange, float lateralTolerance)
        {
            position.y = 0f;
            assignedSlot.y = 0f;
            var offset = position - assignedSlot;
            return offset.magnitude <= Mathf.Max(0f, closeRadius)
                || Mathf.Abs(offset.x) <= Mathf.Max(0f,
                    corridorLongitudinalRange)
                && Mathf.Abs(offset.z) <= Mathf.Max(0f, lateralTolerance);
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
            StatusMessage = "Opening the doors…";
            UpdateDoors(0f);
            motion = StartCoroutine(OpenDoorsThenWalk());
        }

        private IEnumerator OpenDoorsThenWalk()
        {
            var delay = DoorOpeningDelay;
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }
            StatusMessage = "Walking to the game room…";
            yield return WalkRoute();
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
                UpdateDoors(leaderDistance);
                var target = route.GetSlotPosition(leaderDistance,
                    localSlot, roster.Length);
                player.MoveTowards(target, speed * Time.deltaTime * 1.35f);
                TryPlayPoseidonCue();
                TrackBlocked(previous, target, ref blockedFor);
                previous = player.transform.position;
                if (blockedFor >= blockedTimeout)
                {
                    // CharacterController contact on a stair edge can make
                    // the distance-driven formation temporarily run ahead of
                    // the actual rig. Project back onto the route and retry
                    // from the player's real progress instead of cancelling
                    // the synchronized start for everybody.
                    var projected = route.GetClosestDistance(
                        player.transform.position, out _);
                    leaderDistance = GetResynchronizedLeaderDistance(
                        projected, localSlot, route.RowSpacing, route.Length);
                    speed = 0f;
                    blockedFor = 0f;
                    previous = player.transform.position;
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
            TryReleaseGroup();
        }

        private void TryPlayPoseidonCue()
        {
            if (poseidonCuePlayed || poseidonAudioSource == null
                || PlanarDistance(player.transform.position,
                    poseidon.position) > poseidonTriggerDistance)
            {
                return;
            }

            poseidonCuePlayed = true;
            poseidonAudioSource.Play();
            poseidonLightPulse = StartCoroutine(PulsePoseidonLight());
        }

        private IEnumerator PulsePoseidonLight()
        {
            if (poseidonSpeakingLight == null || poseidonAudioSource == null)
            {
                yield break;
            }

            var duration = poseidonAudioSource.clip.length;
            var elapsed = 0f;
            while (elapsed < duration && poseidonAudioSource.isPlaying)
            {
                elapsed += Time.deltaTime;
                var fadeIn = Mathf.Clamp01(elapsed / 0.18f);
                var fadeOut = Mathf.Clamp01((duration - elapsed) / 0.35f);
                var pulse = 0.82f + Mathf.Sin(elapsed * 13f) * 0.18f;
                poseidonSpeakingLight.intensity = poseidonLightIntensity
                    * fadeIn * fadeOut * pulse;
                yield return null;
            }

            poseidonSpeakingLight.intensity = 0f;
            poseidonLightPulse = null;
        }

        private void StopPoseidonCue()
        {
            poseidonAudioSource?.Stop();
            if (poseidonLightPulse != null)
            {
                StopCoroutine(poseidonLightPulse);
                poseidonLightPulse = null;
            }
            if (poseidonSpeakingLight != null)
            {
                poseidonSpeakingLight.intensity = 0f;
            }
        }

        public static float GetResynchronizedLeaderDistance(
            float projectedRouteDistance, int slotIndex, float rowSpacing,
            float routeLength)
        {
            var rowOffset = Mathf.Max(0, slotIndex / 2)
                * Mathf.Max(0f, rowSpacing);
            return Mathf.Clamp(projectedRouteDistance + rowOffset, 0f,
                Mathf.Max(0f, routeLength));
        }

        private void TryReleaseGroup()
        {
            if (phase is not (SequencePhase.Walking
                    or SequencePhase.WaitingForGroup)
                || !AllCurrentPeersAreIn(arrivedPeers))
            {
                return;
            }

            if (motion != null)
            {
                StopCoroutine(motion);
                motion = null;
            }
            phase = SequencePhase.Complete;
            StatusMessage = string.Empty;
            CloseDoors();
            player.SetLock(MovementLockReason.GameStartSequence, false);
            Completed?.Invoke();
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

        private void UpdateDoors(float leaderDistance)
        {
            foreach (var door in doors)
            {
                door?.SetFormationProgress(leaderDistance);
            }
        }

        private void CloseDoors()
        {
            foreach (var door in doors)
            {
                door?.Close();
            }
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
            else if (phase is SequencePhase.Walking
                or SequencePhase.WaitingForGroup)
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
            CloseDoors();
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
            StopPoseidonCue();
        }

        private void OnDisable()
        {
            player?.SetLock(MovementLockReason.GameStartSequence, false);
            CloseDoors();
            StopPoseidonCue();
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
