using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ubiq.Messaging;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Ubiq.SceneSwitcher
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkScene), typeof(RoomClient))]
    public sealed class RoomSceneSwitcher : MonoBehaviour
    {
        public static RoomSceneSwitcher Instance { get; private set; }

        [Header("Scenes")]
        [SerializeField] private SerializableSceneReference privateScene = new();
        [SerializeField] private SerializableSceneReference multiplayerScene = new();
        [SerializeField] private Transform playerRigRoot;

        [Header("Compatibility")]
        [SerializeField] private string applicationId = "com.company.product";
        [Min(1)] [SerializeField] private int protocolVersion = 1;

        [Header("Networking")]
        [SerializeField] private RoomClient roomClient;
        [Min(0.25f)] [SerializeField] private float discoveryRefreshInterval = 2f;
        [Min(1f)] [SerializeField] private float discoveryTimeout = 10f;
        [Min(1f)] [SerializeField] private float joinTimeout = 15f;
        [Min(1f)] [SerializeField] private float metadataTimeout = 10f;
        [Min(1f)] [SerializeField] private float sceneLoadTimeout = 30f;
        [SerializeField] private bool autoRefreshRooms = true;

        [Header("Presentation (optional)")]
        [Tooltip("A MonoBehaviour implementing IRoomSceneTransitionView.")]
        [SerializeField] private MonoBehaviour transitionView;

        [Header("Inspector Events")]
        [SerializeField] private RoomSceneStateEvent onStateChanged = new();
        [SerializeField] private UnityEvent onRoomsChanged = new();
        [SerializeField] private RoomSummaryEvent onEnteredRoom = new();
        [SerializeField] private UnityEvent onReturnedToPrivateScene = new();
        [SerializeField] private RoomSceneFailureEvent onOperationFailed = new();

        private readonly List<RoomSummary> rooms = new();
        private IReadOnlyList<RoomSummary> readOnlyRooms;
        private IUbiqRoomGateway gateway;
        private IRoomSceneTransitionView TransitionView =>
            transitionView as IRoomSceneTransitionView;
        private bool transitionActive;
        private bool refreshActive;
        private bool initialized;
        private double nextRefreshTime;
        private string desiredSharedRoomUuid = string.Empty;
        private RoomSummary currentRoom = RoomSummary.Empty;

        public RoomSceneState State { get; private set; } = RoomSceneState.Initializing;
        public RoomSummary CurrentRoom => currentRoom;
        public IReadOnlyList<RoomSummary> Rooms => readOnlyRooms ??= rooms.AsReadOnly();
        public bool IsBusy => transitionActive;
        public string ApplicationId => applicationId;
        public int ProtocolVersion => protocolVersion;
        public SerializableSceneReference PrivateScene => privateScene;
        public SerializableSceneReference MultiplayerScene => multiplayerScene;
        public Transform PlayerRigRoot => playerRigRoot;
        public RoomClient RoomClient => roomClient;

        public event Action<RoomSceneState> StateChanged;
        public event Action<IReadOnlyList<RoomSummary>> RoomsChanged;
        public event Action<RoomSummary> CurrentRoomChanged;
        public event Action<RoomSceneFailure> OperationFailed;

        private void Reset()
        {
            roomClient = GetComponent<RoomClient>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            readOnlyRooms = rooms.AsReadOnly();
            roomClient ??= GetComponent<RoomClient>();
        }

        private async void Start()
        {
            if (Instance != this)
            {
                return;
            }

            var validationFailure = ValidateRuntimeConfiguration();
            if (validationFailure.Code != RoomSceneFailureCode.None)
            {
                FailPermanently(validationFailure);
                return;
            }

            gateway = new UbiqRoomGateway(roomClient);
            gateway.RoomJoined += HandleObservedRoomJoined;
            roomClient.timeoutBehaviour = RoomClient.TimeoutBehaviour.ReconnectAndRejoin;

            var anchorFailure = ApplyAnchor(RoomSceneRole.Private);
            if (anchorFailure.Code != RoomSceneFailureCode.None)
            {
                FailPermanently(anchorFailure);
                return;
            }

            var privateResult = await gateway.EnterPrivateAsync(joinTimeout,
                destroyCancellationToken);
            if (!privateResult.Succeeded)
            {
                FailPermanently(privateResult.Failure);
                return;
            }

            SetCurrentRoom(privateResult.Room);
            initialized = true;
            SetState(RoomSceneState.PrivateRoom);
            nextRefreshTime = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (!initialized || !autoRefreshRooms || State != RoomSceneState.PrivateRoom
                || transitionActive || refreshActive
                || Time.realtimeSinceStartupAsDouble < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.realtimeSinceStartupAsDouble + discoveryRefreshInterval;
            AutoRefreshAsync();
        }

        private async void AutoRefreshAsync()
        {
            await RefreshRoomsAsync(destroyCancellationToken);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            if (gateway != null)
            {
                gateway.RoomJoined -= HandleObservedRoomJoined;
                gateway.Dispose();
            }
            Instance = null;
        }

        public async Awaitable<RoomListResult> RefreshRoomsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!initialized || gateway == null)
            {
                return RoomListFailure(RoomSceneFailureCode.InvalidConfiguration,
                    "The room session has not finished initializing.");
            }
            if (State != RoomSceneState.PrivateRoom)
            {
                return RoomListFailure(RoomSceneFailureCode.InvalidConfiguration,
                    "Rooms can only be browsed from the private scene.");
            }
            if (refreshActive || transitionActive)
            {
                return RoomListFailure(RoomSceneFailureCode.Busy,
                    "Room discovery is already in progress.");
            }

            refreshActive = true;
            try
            {
                var result = await gateway.DiscoverAsync(string.Empty,
                    discoveryTimeout, cancellationToken);
                if (!result.Succeeded)
                {
                    ReportFailure(result.Failure);
                    return new RoomListResult(false, Rooms, result.Failure);
                }

                rooms.Clear();
                rooms.AddRange(result.Rooms
                    .Where(IsCompatible)
                    .OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(room => room.Uuid, StringComparer.Ordinal));
                RoomsChanged?.Invoke(Rooms);
                onRoomsChanged.Invoke();
                return new RoomListResult(true, Rooms, RoomSceneFailure.None);
            }
            finally
            {
                refreshActive = false;
            }
        }

        public async Awaitable<RoomTransitionResult> CreateAndEnterAsync(
            RoomCreateOptions options, CancellationToken cancellationToken = default)
        {
            var name = (options.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                return TransitionFailure(RoomSceneFailureCode.InvalidRoomName,
                    "Enter a non-empty room name.");
            }
            if (!TryBeginPrivateTransition(out var busyResult))
            {
                return busyResult;
            }

            try
            {
                var loadFailure = await LoadConfiguredSceneAsync(multiplayerScene,
                    RoomSceneRole.Multiplayer, RoomSceneState.LoadingMultiplayerScene,
                    cancellationToken);
                if (loadFailure.Code != RoomSceneFailureCode.None)
                {
                    return await RollBackToPrivateAsync(loadFailure);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return await RollBackToPrivateAsync(CancelledFailure());
                }

                SetState(RoomSceneState.JoiningRoom);
                SetStatus("Creating room…");
                var createResult = await gateway.CreateAsync(name, options.Publish,
                    joinTimeout, cancellationToken);
                if (!createResult.Succeeded)
                {
                    return await RollBackToPrivateAsync(createResult.Failure);
                }

                desiredSharedRoomUuid = createResult.Room.Uuid;
                SetState(RoomSceneState.PublishingRoomMetadata);
                SetStatus("Publishing room…");
                var metadataResult = await gateway.PublishMetadataAsync(applicationId,
                    multiplayerScene.AssetGuid, protocolVersion, metadataTimeout,
                    cancellationToken);
                if (!metadataResult.Succeeded)
                {
                    return await RollBackToPrivateAsync(metadataResult.Failure);
                }

                SetCurrentRoom(metadataResult.Room);
                SetState(RoomSceneState.MultiplayerRoom);
                await FadeInSafelyAsync();
                onEnteredRoom.Invoke(metadataResult.Room);
                return RoomTransitionResult.Success(metadataResult.Room);
            }
            finally
            {
                transitionActive = false;
                SetStatus(string.Empty);
            }
        }

        public async Awaitable<RoomTransitionResult> JoinAsync(RoomSummary room,
            CancellationToken cancellationToken = default)
        {
            var compatibilityFailure = RoomCompatibility.GetCompatibilityFailure(room,
                applicationId, multiplayerScene.AssetGuid, protocolVersion);
            if (compatibilityFailure.Code != RoomSceneFailureCode.None)
            {
                ReportFailure(compatibilityFailure);
                return RoomTransitionResult.Fail(compatibilityFailure);
            }
            if (string.IsNullOrWhiteSpace(room.JoinCode))
            {
                return TransitionFailure(RoomSceneFailureCode.InvalidJoinCode,
                    "The selected room has no join code.");
            }
            if (!TryBeginPrivateTransition(out var busyResult))
            {
                return busyResult;
            }

            try
            {
                return await JoinCoreAsync(room, cancellationToken);
            }
            finally
            {
                transitionActive = false;
                SetStatus(string.Empty);
            }
        }

        public async Awaitable<RoomTransitionResult> JoinByCodeAsync(string joinCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedCode = RoomCompatibility.NormalizeJoinCode(joinCode);
            if (string.IsNullOrEmpty(normalizedCode))
            {
                return TransitionFailure(RoomSceneFailureCode.InvalidJoinCode,
                    "Enter a join code.");
            }
            if (!TryBeginPrivateTransition(out var busyResult))
            {
                return busyResult;
            }

            try
            {
                SetStatus("Looking up room…");
                var discovery = await gateway.DiscoverAsync(normalizedCode,
                    discoveryTimeout, cancellationToken);
                if (!discovery.Succeeded)
                {
                    ReportFailure(discovery.Failure);
                    return RoomTransitionResult.Fail(discovery.Failure);
                }

                var matchingCode = discovery.Rooms
                    .Where(room => string.Equals(room.JoinCode, normalizedCode,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matchingCode.Count == 0)
                {
                    return TransitionFailure(RoomSceneFailureCode.RoomNotFound,
                        "No room was found for that join code.");
                }
                if (matchingCode.Count > 1)
                {
                    return TransitionFailure(RoomSceneFailureCode.UnexpectedRoom,
                        "The server returned more than one room for that join code.");
                }

                var room = matchingCode[0];
                var compatibilityFailure = RoomCompatibility.GetCompatibilityFailure(room,
                    applicationId, multiplayerScene.AssetGuid, protocolVersion);
                if (compatibilityFailure.Code != RoomSceneFailureCode.None)
                {
                    ReportFailure(compatibilityFailure);
                    return RoomTransitionResult.Fail(compatibilityFailure);
                }

                return await JoinCoreAsync(room, cancellationToken);
            }
            finally
            {
                transitionActive = false;
                SetStatus(string.Empty);
            }
        }

        public async Awaitable<RoomTransitionResult> ReturnToPrivateSceneAsync(
            CancellationToken cancellationToken = default)
        {
            if (transitionActive || refreshActive)
            {
                return TransitionFailure(RoomSceneFailureCode.Busy,
                    "Another room transition is already in progress.");
            }
            if (State != RoomSceneState.MultiplayerRoom
                && State != RoomSceneState.RecoveringConnection)
            {
                return TransitionFailure(RoomSceneFailureCode.InvalidConfiguration,
                    "The session is not in a multiplayer room.");
            }

            transitionActive = true;
            try
            {
                SetState(RoomSceneState.LeavingRoom);
                SetStatus("Leaving room…");
                desiredSharedRoomUuid = string.Empty;
                var leaveResult = await gateway.EnterPrivateAsync(joinTimeout,
                    cancellationToken);
                if (!leaveResult.Succeeded)
                {
                    ReportFailure(leaveResult.Failure);
                    SetState(RoomSceneState.MultiplayerRoom);
                    return RoomTransitionResult.Fail(leaveResult.Failure);
                }

                var loadFailure = await LoadConfiguredSceneAsync(privateScene,
                    RoomSceneRole.Private, RoomSceneState.LoadingPrivateScene,
                    CancellationToken.None);
                if (loadFailure.Code != RoomSceneFailureCode.None)
                {
                    FailPermanently(loadFailure);
                    return RoomTransitionResult.Fail(loadFailure);
                }

                SetCurrentRoom(leaveResult.Room);
                SetState(RoomSceneState.PrivateRoom);
                await FadeInSafelyAsync();
                onReturnedToPrivateScene.Invoke();
                nextRefreshTime = Time.realtimeSinceStartupAsDouble;
                return RoomTransitionResult.Success(leaveResult.Room);
            }
            finally
            {
                transitionActive = false;
                SetStatus(string.Empty);
            }
        }

        private async Awaitable<RoomTransitionResult> JoinCoreAsync(RoomSummary requested,
            CancellationToken cancellationToken)
        {
            var loadFailure = await LoadConfiguredSceneAsync(multiplayerScene,
                RoomSceneRole.Multiplayer, RoomSceneState.LoadingMultiplayerScene,
                cancellationToken);
            if (loadFailure.Code != RoomSceneFailureCode.None)
            {
                return await RollBackToPrivateAsync(loadFailure);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return await RollBackToPrivateAsync(CancelledFailure());
            }

            SetState(RoomSceneState.JoiningRoom);
            SetStatus("Joining room…");
            desiredSharedRoomUuid = requested.Uuid;
            var joinResult = await gateway.JoinAsync(requested.JoinCode, joinTimeout,
                cancellationToken);
            if (!joinResult.Succeeded)
            {
                return await RollBackToPrivateAsync(joinResult.Failure);
            }
            if (!string.Equals(joinResult.Room.Uuid, requested.Uuid,
                StringComparison.Ordinal))
            {
                return await RollBackToPrivateAsync(new RoomSceneFailure(
                    RoomSceneFailureCode.UnexpectedRoom,
                    "The server joined a different room than the one requested."));
            }

            SetCurrentRoom(joinResult.Room);
            SetState(RoomSceneState.MultiplayerRoom);
            await FadeInSafelyAsync();
            onEnteredRoom.Invoke(joinResult.Room);
            return RoomTransitionResult.Success(joinResult.Room);
        }

        private async Awaitable<RoomTransitionResult> RollBackToPrivateAsync(
            RoomSceneFailure originalFailure)
        {
            desiredSharedRoomUuid = string.Empty;
            SetState(RoomSceneState.LeavingRoom);
            SetStatus("Returning to private room…");
            var privateResult = await gateway.EnterPrivateAsync(joinTimeout,
                CancellationToken.None);

            var loadFailure = await LoadConfiguredSceneAsync(privateScene,
                RoomSceneRole.Private, RoomSceneState.LoadingPrivateScene,
                CancellationToken.None);
            if (loadFailure.Code != RoomSceneFailureCode.None)
            {
                FailPermanently(loadFailure);
                return RoomTransitionResult.Fail(loadFailure);
            }

            if (privateResult.Succeeded)
            {
                SetCurrentRoom(privateResult.Room);
            }
            SetState(RoomSceneState.PrivateRoom);
            await FadeInSafelyAsync();
            ReportFailure(originalFailure);
            return RoomTransitionResult.Fail(originalFailure);
        }

        private async Awaitable<RoomSceneFailure> LoadConfiguredSceneAsync(
            SerializableSceneReference sceneReference, RoomSceneRole expectedRole,
            RoomSceneState loadingState, CancellationToken cancellationToken)
        {
            SetState(loadingState);
            SetStatus(expectedRole == RoomSceneRole.Private
                ? "Loading private room…"
                : "Loading multiplayer scene…");

            try
            {
                if (TransitionView != null)
                {
                    await TransitionView.FadeOutAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return CancelledFailure();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(sceneReference.ScenePath,
                    LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.SceneLoadFailed,
                    exception.Message);
            }

            if (operation == null)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.SceneLoadFailed,
                    $"Unity could not start loading '{sceneReference.ScenePath}'.");
            }

            var started = Time.realtimeSinceStartupAsDouble;
            var cancellationObserved = false;
            while (!operation.isDone)
            {
                cancellationObserved |= cancellationToken.IsCancellationRequested;
                await Awaitable.NextFrameAsync();
            }

            if (cancellationObserved || cancellationToken.IsCancellationRequested)
            {
                return CancelledFailure();
            }
            if (Time.realtimeSinceStartupAsDouble - started > sceneLoadTimeout)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.SceneLoadFailed,
                    $"Loading '{sceneReference.ScenePath}' exceeded the configured timeout.");
            }

            return ApplyAnchor(expectedRole);
        }

        private RoomSceneFailure ApplyAnchor(RoomSceneRole expectedRole)
        {
            var activeScene = SceneManager.GetActiveScene();
            var anchors = new List<RoomSceneAnchor>();
            foreach (var root in activeScene.GetRootGameObjects())
            {
                anchors.AddRange(root.GetComponentsInChildren<RoomSceneAnchor>(true));
            }
            if (anchors.Count == 0)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.SceneAnchorMissing,
                    $"Scene '{activeScene.path}' has no {expectedRole} RoomSceneAnchor.");
            }
            if (anchors.Count > 1)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.SceneAnchorDuplicated,
                    $"Scene '{activeScene.path}' has more than one {expectedRole} RoomSceneAnchor.");
            }
            if (anchors[0].Role != expectedRole)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.InvalidConfiguration,
                    $"Scene '{activeScene.path}' has a RoomSceneAnchor with the wrong role.");
            }

            var spawn = anchors[0].PlayerSpawn;
            playerRigRoot.SetPositionAndRotation(spawn.position, spawn.rotation);
            foreach (var behaviour in playerRigRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IPlayerRigReset resetter)
                {
                    resetter.ResetAt(spawn);
                }
            }
            return RoomSceneFailure.None;
        }

        private RoomSceneFailure ValidateRuntimeConfiguration()
        {
            if (transform.parent != null)
            {
                return Invalid("RoomSceneSwitcher must be on a root GameObject.");
            }
            if (roomClient == null || GetComponent<NetworkScene>() == null)
            {
                return Invalid("The session root requires NetworkScene and RoomClient.");
            }
            if (privateScene == null || multiplayerScene == null
                || !privateScene.IsConfigured || !multiplayerScene.IsConfigured)
            {
                return Invalid("Assign both the private and multiplayer scenes.");
            }
            if (string.Equals(privateScene.AssetGuid, multiplayerScene.AssetGuid,
                StringComparison.Ordinal))
            {
                return Invalid("Private and multiplayer scenes must be different.");
            }
            if (playerRigRoot == null || !playerRigRoot.IsChildOf(transform))
            {
                return Invalid("The player rig root must be a child of the persistent session root.");
            }
            if (string.IsNullOrWhiteSpace(applicationId) || protocolVersion < 1)
            {
                return Invalid("Application ID must be non-empty and protocol version positive.");
            }
            if (transitionView != null && TransitionView == null)
            {
                return Invalid("Transition View must implement IRoomSceneTransitionView.");
            }
            if (!string.Equals(SceneManager.GetActiveScene().path, privateScene.ScenePath,
                StringComparison.Ordinal))
            {
                return Invalid("The application must start in the configured private scene.");
            }
            return RoomSceneFailure.None;
        }

        private bool TryBeginPrivateTransition(out RoomTransitionResult failureResult)
        {
            if (transitionActive || refreshActive)
            {
                failureResult = TransitionFailure(RoomSceneFailureCode.Busy,
                    "Another room transition is already in progress.");
                return false;
            }
            if (!initialized || State != RoomSceneState.PrivateRoom)
            {
                failureResult = TransitionFailure(
                    RoomSceneFailureCode.InvalidConfiguration,
                    "Rooms can only be entered from the private scene.");
                return false;
            }

            transitionActive = true;
            failureResult = default;
            return true;
        }

        private bool IsCompatible(RoomSummary room) =>
            RoomCompatibility.IsCompatible(room, applicationId,
                multiplayerScene.AssetGuid, protocolVersion);

        private void HandleObservedRoomJoined(RoomSummary room)
        {
            if (transitionActive)
            {
                return;
            }

            if (State == RoomSceneState.MultiplayerRoom && room.IsEmpty)
            {
                SetState(RoomSceneState.RecoveringConnection);
                return;
            }
            if (State == RoomSceneState.RecoveringConnection
                && !room.IsEmpty
                && string.Equals(room.Uuid, desiredSharedRoomUuid,
                    StringComparison.Ordinal))
            {
                SetCurrentRoom(room);
                SetState(RoomSceneState.MultiplayerRoom);
            }
        }

        private void SetState(RoomSceneState state)
        {
            if (State == state)
            {
                return;
            }
            State = state;
            StateChanged?.Invoke(state);
            onStateChanged.Invoke(state);
        }

        private void SetCurrentRoom(RoomSummary room)
        {
            currentRoom = room;
            CurrentRoomChanged?.Invoke(room);
        }

        private void SetStatus(string message)
        {
            TransitionView?.SetStatus(message);
        }

        private async Awaitable FadeInSafelyAsync()
        {
            if (TransitionView == null)
            {
                return;
            }
            try
            {
                await TransitionView.FadeInAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private RoomListResult RoomListFailure(RoomSceneFailureCode code, string message)
        {
            var failure = new RoomSceneFailure(code, message);
            ReportFailure(failure);
            return new RoomListResult(false, Rooms, failure);
        }

        private RoomTransitionResult TransitionFailure(RoomSceneFailureCode code,
            string message)
        {
            var failure = new RoomSceneFailure(code, message);
            ReportFailure(failure);
            return RoomTransitionResult.Fail(failure);
        }

        private static RoomSceneFailure CancelledFailure() =>
            new(RoomSceneFailureCode.Cancelled, "The operation was cancelled.");

        private static RoomSceneFailure Invalid(string message) =>
            new(RoomSceneFailureCode.InvalidConfiguration, message);

        private void ReportFailure(RoomSceneFailure failure)
        {
            OperationFailed?.Invoke(failure);
            onOperationFailed.Invoke(failure);
        }

        private void FailPermanently(RoomSceneFailure failure)
        {
            SetState(RoomSceneState.Faulted);
            ReportFailure(failure);
            Debug.LogError(failure.ToString(), this);
        }
    }
}
