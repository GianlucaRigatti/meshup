using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ubiq.Avatars;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.SceneManagement;
using UbiqAvatar = Ubiq.Avatars.Avatar;

namespace Meshup.Multiplayer
{
    [Serializable]
    public sealed class ParticipantInfo
    {
        public string PeerId;
        public string DisplayName;
        public bool Connected;

        public ParticipantInfo(string peerId, string displayName, bool connected)
        {
            PeerId = peerId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? ShortPeerId(PeerId)
                : displayName.Trim();
            Connected = connected;
        }

        private static string ShortPeerId(string peerId)
        {
            if (string.IsNullOrEmpty(peerId))
            {
                return "Player";
            }
            return $"Player {peerId[..Math.Min(6, peerId.Length)]}";
        }
    }

    public enum RoomSessionState
    {
        Connecting,
        LobbyReady,
        Discovering,
        Joining,
        Publishing,
        LoadingGame,
        InGame,
        Leaving,
        Error
    }

    [Serializable]
    public sealed class RoomListing
    {
        public string Name { get; }
        public string Uuid { get; }
        public string JoinCode { get; }

        public RoomListing(string name, string uuid, string joinCode)
        {
            Name = name ?? string.Empty;
            Uuid = uuid ?? string.Empty;
            JoinCode = joinCode ?? string.Empty;
        }

        internal static RoomListing FromRoom(IRoom room)
        {
            return room == null
                ? null
                : new RoomListing(room.Name, room.UUID, room.JoinCode);
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RoomClient))]
    public sealed class UbiqRoomSession : MonoBehaviour
    {
        private const string ApplicationProperty = "meshup.application";
        private const string ProtocolProperty = "meshup.protocol";
        private const string SceneProperty = "meshup.scene";
        private const string CreatorProperty = "meshup.creator";
        private const string GameStartedProperty = "meshup.game.started";
        private const float SceneTransitionTimeout = 5f;

        private enum PendingOperation
        {
            None,
            EnterInitialLobby,
            Create,
            Join,
            Recover,
            Leave
        }

        public static UbiqRoomSession Instance { get; private set; }

        [Header("Ubiq")]
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private AvatarManager avatarManager;

        [Header("Compatibility")]
        [SerializeField] private string applicationId = "meshup-game";
        [SerializeField] private int protocolVersion = 1;

        [Header("Scenes")]
        [SerializeField] private string lobbySceneName = "SampleScene";
        [SerializeField] private string gameSceneName = "GameScene";

        [Header("Timeouts")]
        [SerializeField] private float discoveryTimeout = 10f;
        [SerializeField] private float roomOperationTimeout = 15f;
        [SerializeField] private float metadataTimeout = 10f;

        private readonly List<RoomListing> rooms = new();
        private readonly Dictionary<Renderer, bool> hiddenAvatarRenderers = new();
        private IReadOnlyList<RoomListing> readOnlyRooms;
        private GameObject gameAvatarPrefab;
        private PendingOperation pendingOperation;
        private RoomListing pendingRoom;
        private string pendingRoomName = string.Empty;
        private string roomBeforeOperation = string.Empty;
        private string recoveryMessage = string.Empty;
        private int operationVersion;
        private bool subscribed;

        public RoomSessionState State { get; private set; } = RoomSessionState.Connecting;
        public IReadOnlyList<RoomListing> Rooms => readOnlyRooms ??= rooms.AsReadOnly();
        public RoomListing CurrentRoom { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public string LocalPeerId => roomClient?.Me?.uuid ?? string.Empty;
        public string CreatorPeerId => roomClient?.Room?[CreatorProperty] ?? string.Empty;
        public bool IsRoomCreator => !string.IsNullOrEmpty(LocalPeerId)
            && string.Equals(LocalPeerId, CreatorPeerId, StringComparison.Ordinal);
        public bool GameStarted => IsGameStarted(roomClient?.Room);
        public int ParticipantCount => (string.IsNullOrEmpty(LocalPeerId) ? 0 : 1)
            + (roomClient?.Peers?.Count(peer =>
                !string.IsNullOrEmpty(peer.uuid)) ?? 0);

        public event Action<RoomSessionState> StateChanged;
        public event Action<IReadOnlyList<RoomListing>> RoomsChanged;
        public event Action<string> ErrorOccurred;
        public event Action ParticipantsChanged;

        /// <summary>
        /// Raised after room membership has been confirmed and the game scene
        /// has started preloading. A listener may delay scene activation by
        /// invoking the supplied callback when its transition has fully covered
        /// the view. The callback is safe to invoke more than once.
        /// </summary>
        public event Action<Action> GameSceneTransitionRequested;

        private void Reset()
        {
            roomClient = GetComponent<RoomClient>();
            avatarManager = GetComponentInChildren<AvatarManager>(true);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }

            Instance = this;
            roomClient ??= GetComponent<RoomClient>();
            avatarManager ??= GetComponentInChildren<AvatarManager>(true);
            readOnlyRooms = rooms.AsReadOnly();
            SceneManager.sceneLoaded += HandleSceneLoaded;

            if (avatarManager != null)
            {
                // NetworkSpawner is created by AvatarManager.Start and must remain
                // subscribed while a room is joined. Disabling this object until
                // GameScene can lose PeerAdded/PeerUpdated events for peers that
                // were already in the room.
                avatarManager.gameObject.SetActive(true);
                gameAvatarPrefab = avatarManager.avatarPrefab;
                avatarManager.OnAvatarCreated.AddListener(HandleAvatarCreated);
                avatarManager.OnAvatarDestroyed.AddListener(HandleAvatarDestroyed);

                if (SceneManager.GetActiveScene().name != gameSceneName)
                {
                    avatarManager.avatarPrefab = null;
                    SetAvatarPresentationVisible(false);
                }
            }
        }

        private void Start()
        {
            if (Instance != this)
            {
                return;
            }

            Subscribe();
            roomClient.timeoutBehaviour = RoomClient.TimeoutBehaviour.ReconnectAndRejoin;

            if (SceneManager.GetActiveScene().name == gameSceneName && roomClient.JoinedRoom)
            {
                ActivateGameAvatars();
                SetState(RoomSessionState.InGame);
                return;
            }

            BeginPrivateRoom(PendingOperation.EnterInitialLobby);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Unsubscribe();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (avatarManager != null)
            {
                avatarManager.OnAvatarCreated.RemoveListener(HandleAvatarCreated);
                avatarManager.OnAvatarDestroyed.RemoveListener(HandleAvatarDestroyed);
            }
            Instance = null;
        }

        public bool RefreshRooms()
        {
            if (State == RoomSessionState.Error
                && SceneManager.GetActiveScene().name == lobbySceneName)
            {
                roomClient.Reconnect();
                BeginPrivateRoom(PendingOperation.EnterInitialLobby);
                return true;
            }

            if (State != RoomSessionState.LobbyReady)
            {
                return false;
            }

            LastError = string.Empty;
            pendingOperation = PendingOperation.None;
            SetState(RoomSessionState.Discovering);
            var version = BeginTimeout();
            roomClient.DiscoverRooms();
            StartCoroutine(DiscoveryTimeout(version));
            return true;
        }

        public bool CreateRoom(string roomName)
        {
            if (State != RoomSessionState.LobbyReady
                && State != RoomSessionState.Discovering)
            {
                ReportError("Rooms can only be created while the lobby is ready.");
                return false;
            }

            var trimmedName = (roomName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                ReportError("Enter a room name before creating a room.");
                return false;
            }

            LastError = string.Empty;
            pendingOperation = PendingOperation.Create;
            pendingRoomName = trimmedName;
            pendingRoom = null;
            roomBeforeOperation = roomClient.Room?.UUID ?? string.Empty;
            SetState(RoomSessionState.Joining);
            var version = BeginTimeout();
            roomClient.Join(trimmedName, true);
            StartCoroutine(RoomOperationTimeout(version,
                "Creating the room timed out. Check the network connection and try again."));
            return true;
        }

        public bool JoinRoom(RoomListing room)
        {
            if (State != RoomSessionState.LobbyReady
                && State != RoomSessionState.Discovering)
            {
                ReportError("Rooms can only be joined while the lobby is ready.");
                return false;
            }
            if (room == null || string.IsNullOrWhiteSpace(room.JoinCode))
            {
                ReportError("The selected room does not have a valid join code.");
                return false;
            }

            LastError = string.Empty;
            pendingOperation = PendingOperation.Join;
            pendingRoom = room;
            pendingRoomName = string.Empty;
            roomBeforeOperation = roomClient.Room?.UUID ?? string.Empty;
            SetState(RoomSessionState.Joining);
            var version = BeginTimeout();
            roomClient.Join(room.JoinCode);
            StartCoroutine(RoomOperationTimeout(version,
                "Joining the room timed out. Check the network connection and try again."));
            return true;
        }

        public bool LeaveRoom()
        {
            if (State != RoomSessionState.InGame)
            {
                return false;
            }

            LastError = string.Empty;
            pendingOperation = PendingOperation.Leave;
            roomBeforeOperation = roomClient.Room?.UUID ?? string.Empty;
            SetState(RoomSessionState.Leaving);
            var version = BeginTimeout();
            roomClient.Join(string.Empty, false);
            StartCoroutine(LeaveTimeout(version));
            return true;
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            roomClient.OnJoinedRoom.AddListener(HandleJoinedRoom);
            roomClient.OnRoomUpdated.AddListener(HandleRoomUpdated);
            roomClient.OnJoinRejected.AddListener(HandleJoinRejected);
            roomClient.OnRooms.AddListener(HandleRoomsDiscovered);
            roomClient.OnPeerAdded.AddListener(HandlePeerChanged);
            roomClient.OnPeerRemoved.AddListener(HandlePeerChanged);
            subscribed = true;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == gameSceneName)
            {
                ActivateGameAvatars();
            }
            else if (scene.name == lobbySceneName)
            {
                DeactivateGameAvatars();
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed || roomClient == null)
            {
                return;
            }

            roomClient.OnJoinedRoom.RemoveListener(HandleJoinedRoom);
            roomClient.OnRoomUpdated.RemoveListener(HandleRoomUpdated);
            roomClient.OnJoinRejected.RemoveListener(HandleJoinRejected);
            roomClient.OnRooms.RemoveListener(HandleRoomsDiscovered);
            roomClient.OnPeerAdded.RemoveListener(HandlePeerChanged);
            roomClient.OnPeerRemoved.RemoveListener(HandlePeerChanged);
            subscribed = false;
        }

        public IReadOnlyList<string> GetParticipantIds()
        {
            var result = new List<string>();
            if (!string.IsNullOrEmpty(LocalPeerId))
            {
                result.Add(LocalPeerId);
            }

            if (roomClient != null)
            {
                result.AddRange(roomClient.Peers
                    .Select(peer => peer.uuid)
                    .Where(uuid => !string.IsNullOrEmpty(uuid)));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public IReadOnlyList<ParticipantInfo> GetParticipants()
        {
            var result = new List<ParticipantInfo>();
            if (roomClient?.Me != null && !string.IsNullOrEmpty(LocalPeerId))
            {
                result.Add(new ParticipantInfo(LocalPeerId,
                    roomClient.Me[Ubiq.DisplayNameManager.KEY], true));
            }

            if (roomClient != null)
            {
                result.AddRange(roomClient.Peers
                    .Where(peer => !string.IsNullOrEmpty(peer.uuid))
                    .Select(peer => new ParticipantInfo(peer.uuid,
                        peer[Ubiq.DisplayNameManager.KEY], true)));
            }

            result.Sort((first, second) => string.Compare(first.PeerId,
                second.PeerId, StringComparison.Ordinal));
            return result;
        }

        public bool TrySetGameStarted(bool started)
        {
            if (roomClient?.Room == null || !IsRoomCreator)
            {
                return false;
            }

            roomClient.Room[GameStartedProperty] = started ? "true" : "false";
            return true;
        }

        private void BeginPrivateRoom(PendingOperation operation)
        {
            pendingOperation = operation;
            pendingRoom = null;
            pendingRoomName = string.Empty;
            roomBeforeOperation = roomClient.Room?.UUID ?? string.Empty;
            SetState(operation == PendingOperation.Leave
                ? RoomSessionState.Leaving
                : RoomSessionState.Connecting);
            var version = BeginTimeout();
            roomClient.Join(string.Empty, false);
            StartCoroutine(RoomOperationTimeout(version,
                "Connecting to the room service timed out. Press Refresh to retry."));
        }

        private void HandleJoinedRoom(IRoom room)
        {
            if (room == null)
            {
                return;
            }

            switch (pendingOperation)
            {
                case PendingOperation.EnterInitialLobby:
                case PendingOperation.Recover:
                    if (IsNewPrivateRoom(room))
                    {
                        CompletePrivateRoom(false);
                    }
                    break;

                case PendingOperation.Leave:
                    if (IsNewPrivateRoom(room))
                    {
                        CompletePrivateRoom(true);
                    }
                    break;

                case PendingOperation.Create:
                    if (room.Publish
                        && string.Equals(room.Name, pendingRoomName,
                            StringComparison.Ordinal))
                    {
                        room[ApplicationProperty] = applicationId;
                        room[ProtocolProperty] = protocolVersion.ToString();
                        room[SceneProperty] = gameSceneName;
                        room[CreatorProperty] = LocalPeerId;
                        room[GameStartedProperty] = "false";
                        CurrentRoom = RoomListing.FromRoom(room);
                        SetState(RoomSessionState.Publishing);
                        var version = BeginTimeout();
                        StartCoroutine(RoomOperationTimeout(version,
                            "Publishing room information timed out. Please try again."));
                    }
                    break;

                case PendingOperation.Join:
                    if (MatchesPendingRoom(room))
                    {
                        if (!IsCompatible(room))
                        {
                            BeginRecovery("That room is not compatible with this build.");
                            return;
                        }

                        if (IsGameStarted(room))
                        {
                            BeginRecovery("That game has already started.");
                            return;
                        }

                        CurrentRoom = RoomListing.FromRoom(room);
                        StartCoroutine(LoadScene(gameSceneName, true));
                    }
                    break;
            }
        }

        private void HandleRoomUpdated(IRoom room)
        {
            if (room != null && CurrentRoom != null
                && string.Equals(room.UUID, CurrentRoom.Uuid,
                    StringComparison.Ordinal))
            {
                CurrentRoom = RoomListing.FromRoom(room);
            }

            if (pendingOperation != PendingOperation.Create
                || State != RoomSessionState.Publishing
                || room == null
                || CurrentRoom == null
                || !string.Equals(room.UUID, CurrentRoom.Uuid,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (IsCompatible(room))
            {
                CurrentRoom = RoomListing.FromRoom(room);
                StartCoroutine(LoadScene(gameSceneName, true));
            }
        }

        private void HandleJoinRejected(Rejection rejection)
        {
            if (pendingOperation == PendingOperation.None)
            {
                return;
            }

            var reason = string.IsNullOrWhiteSpace(rejection.reason)
                ? "The room server rejected the request."
                : rejection.reason;

            if (pendingOperation == PendingOperation.EnterInitialLobby)
            {
                CancelPendingOperation();
                SetState(RoomSessionState.Error);
                ReportError(reason);
                return;
            }

            if (pendingOperation == PendingOperation.Recover)
            {
                CancelPendingOperation();
                SetState(RoomSessionState.Error);
                ReportError(reason);
                return;
            }

            if (pendingOperation == PendingOperation.Leave)
            {
                StartCoroutine(ForceReturnToLobby(
                    $"Leaving the room was rejected: {reason}"));
                return;
            }

            BeginRecovery(reason);
        }

        private void HandleRoomsDiscovered(List<IRoom> discovered,
            RoomsDiscoveredRequest request)
        {
            if (State != RoomSessionState.Discovering
                || !string.IsNullOrEmpty(request.joincode))
            {
                return;
            }

            operationVersion++;
            rooms.Clear();
            rooms.AddRange((discovered ?? new List<IRoom>())
                .Where(room => room.Publish && IsCompatible(room)
                    && !IsGameStarted(room))
                .Select(RoomListing.FromRoom)
                .OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(room => room.Uuid, StringComparer.Ordinal));
            RoomsChanged?.Invoke(Rooms);
            SetState(RoomSessionState.LobbyReady);
        }

        private bool IsCompatible(IRoom room)
        {
            return room != null
                && string.Equals(room[ApplicationProperty], applicationId,
                    StringComparison.Ordinal)
                && string.Equals(room[ProtocolProperty], protocolVersion.ToString(),
                    StringComparison.Ordinal)
                && string.Equals(room[SceneProperty], gameSceneName,
                    StringComparison.Ordinal);
        }

        private static bool IsGameStarted(IRoom room)
        {
            return room != null && IsStartedValue(room[GameStartedProperty]);
        }

        public static bool IsStartedValue(string value)
        {
            return string.Equals(value, "true",
                StringComparison.OrdinalIgnoreCase);
        }

        private void HandlePeerChanged(IPeer peer)
        {
            ParticipantsChanged?.Invoke();
        }

        private bool MatchesPendingRoom(IRoom room)
        {
            return pendingRoom != null
                && (string.Equals(room.UUID, pendingRoom.Uuid, StringComparison.Ordinal)
                    || string.Equals(room.JoinCode, pendingRoom.JoinCode,
                        StringComparison.Ordinal));
        }

        private bool IsNewPrivateRoom(IRoom room)
        {
            return !room.Publish
                && !string.IsNullOrEmpty(room.UUID)
                && (string.IsNullOrEmpty(roomBeforeOperation)
                    || !string.Equals(room.UUID, roomBeforeOperation,
                        StringComparison.Ordinal));
        }

        private void CompletePrivateRoom(bool returnToLobby)
        {
            operationVersion++;
            pendingOperation = PendingOperation.None;
            CurrentRoom = null;
            rooms.Clear();
            RoomsChanged?.Invoke(Rooms);
            DeactivateGameAvatars();

            var message = recoveryMessage;
            recoveryMessage = string.Empty;
            LastError = message;

            if (returnToLobby
                || SceneManager.GetActiveScene().name != lobbySceneName)
            {
                StartCoroutine(LoadScene(lobbySceneName, false, message));
                return;
            }

            SetState(RoomSessionState.LobbyReady);
            if (!string.IsNullOrEmpty(message))
            {
                ReportError(message);
            }
        }

        private void BeginRecovery(string message)
        {
            operationVersion++;
            recoveryMessage = message;
            pendingOperation = PendingOperation.Recover;
            pendingRoom = null;
            pendingRoomName = string.Empty;
            roomBeforeOperation = roomClient.Room?.UUID ?? string.Empty;
            SetState(RoomSessionState.Connecting);
            roomClient.Reconnect();
            var version = BeginTimeout();
            roomClient.Join(string.Empty, false);
            StartCoroutine(RoomOperationTimeout(version,
                "Reconnecting to the lobby timed out. Press Refresh to retry."));
        }

        private IEnumerator LoadScene(string sceneName, bool enteringGame,
            string messageAfterLoad = "")
        {
            operationVersion++;
            pendingOperation = PendingOperation.None;
            SetState(enteringGame
                ? RoomSessionState.LoadingGame
                : RoomSessionState.Leaving);

            var operation = SceneManager.LoadSceneAsync(sceneName,
                LoadSceneMode.Single);
            if (operation == null)
            {
                if (enteringGame)
                {
                    BeginRecovery($"The scene '{sceneName}' is not available in Build Settings.");
                }
                else
                {
                    SetState(RoomSessionState.Error);
                    ReportError($"The scene '{sceneName}' could not be loaded.");
                }
                yield break;
            }

            var transitionCompleted = true;
            if (enteringGame && GameSceneTransitionRequested != null)
            {
                transitionCompleted = false;
                var completionReported = false;
                void CompleteTransition()
                {
                    if (completionReported)
                    {
                        return;
                    }

                    completionReported = true;
                    transitionCompleted = true;
                }

                operation.allowSceneActivation = false;
                try
                {
                    GameSceneTransitionRequested.Invoke(CompleteTransition);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    CompleteTransition();
                }

                var transitionDeadline = Time.realtimeSinceStartup
                    + SceneTransitionTimeout;
                while (!transitionCompleted
                    && Time.realtimeSinceStartup < transitionDeadline)
                {
                    yield return null;
                }

                if (!transitionCompleted)
                {
                    Debug.LogError("[UbiqRoomSession] The game-entry transition "
                        + "did not complete in time. Continuing into the game scene.");
                    CompleteTransition();
                }

                while (operation.progress < 0.9f)
                {
                    yield return null;
                }

                operation.allowSceneActivation = true;
            }

            while (!operation.isDone)
            {
                yield return null;
            }

            if (enteringGame)
            {
                ActivateGameAvatars();
                SetState(RoomSessionState.InGame);
            }
            else
            {
                SetState(RoomSessionState.LobbyReady);
                if (!string.IsNullOrEmpty(messageAfterLoad))
                {
                    ReportError(messageAfterLoad);
                }
            }
        }

        private IEnumerator DiscoveryTimeout(int version)
        {
            yield return new WaitForSecondsRealtime(discoveryTimeout);
            if (version != operationVersion || State != RoomSessionState.Discovering)
            {
                yield break;
            }

            operationVersion++;
            SetState(RoomSessionState.LobbyReady);
            ReportError("Room discovery timed out. Check the connection and refresh again.");
        }

        private IEnumerator RoomOperationTimeout(int version, string message)
        {
            var seconds = State == RoomSessionState.Publishing
                ? metadataTimeout
                : roomOperationTimeout;
            yield return new WaitForSecondsRealtime(seconds);
            if (version != operationVersion)
            {
                yield break;
            }

            if (pendingOperation == PendingOperation.EnterInitialLobby)
            {
                CancelPendingOperation();
                SetState(RoomSessionState.Error);
                ReportError(message);
                yield break;
            }


            if (pendingOperation == PendingOperation.Recover)
            {
                CancelPendingOperation();
                SetState(RoomSessionState.Error);
                ReportError(message);
                yield break;
            }

            BeginRecovery(message);
        }

        private IEnumerator LeaveTimeout(int version)
        {
            yield return new WaitForSecondsRealtime(roomOperationTimeout);
            if (version == operationVersion && pendingOperation == PendingOperation.Leave)
            {
                yield return ForceReturnToLobby(
                    "Leaving the room timed out. The network connection was reset.");
            }
        }

        private IEnumerator ForceReturnToLobby(string message)
        {
            operationVersion++;
            pendingOperation = PendingOperation.None;
            recoveryMessage = string.Empty;
            CurrentRoom = null;
            DeactivateGameAvatars();
            roomClient.Reconnect();
            roomClient.Join(string.Empty, false);
            yield return LoadScene(lobbySceneName, false, message);
        }

        private int BeginTimeout()
        {
            operationVersion++;
            return operationVersion;
        }

        private void CancelPendingOperation()
        {
            operationVersion++;
            pendingOperation = PendingOperation.None;
            pendingRoom = null;
            pendingRoomName = string.Empty;
        }

        private void ActivateGameAvatars()
        {
            if (avatarManager != null)
            {
                avatarManager.gameObject.SetActive(true);
                SetAvatarPresentationVisible(true);
                avatarManager.avatarPrefab = gameAvatarPrefab;
            }
        }

        private void DeactivateGameAvatars()
        {
            if (avatarManager != null)
            {
                SetAvatarPresentationVisible(false);
                avatarManager.avatarPrefab = null;
            }
        }

        private void HandleAvatarCreated(UbiqAvatar avatar)
        {
            if (SceneManager.GetActiveScene().name != gameSceneName)
            {
                SetAvatarPresentationVisible(avatar, false);
            }
        }

        private void HandleAvatarDestroyed(UbiqAvatar avatar)
        {
            if (avatar == null)
            {
                return;
            }

            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                hiddenAvatarRenderers.Remove(renderer);
            }
        }

        private void SetAvatarPresentationVisible(bool visible)
        {
            foreach (var avatar in avatarManager.Avatars.ToArray())
            {
                SetAvatarPresentationVisible(avatar, visible);
            }
        }

        private void SetAvatarPresentationVisible(UbiqAvatar avatar, bool visible)
        {
            if (avatar == null)
            {
                return;
            }

            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (visible)
                {
                    if (hiddenAvatarRenderers.Remove(renderer,
                        out var wasEnabled))
                    {
                        renderer.enabled = wasEnabled;
                    }
                }
                else if (!hiddenAvatarRenderers.ContainsKey(renderer))
                {
                    hiddenAvatarRenderers.Add(renderer, renderer.enabled);
                    renderer.enabled = false;
                }
            }
        }

        private void SetState(RoomSessionState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private void ReportError(string message)
        {
            LastError = message ?? string.Empty;
            ErrorOccurred?.Invoke(LastError);
            if (!string.IsNullOrEmpty(LastError))
            {
                Debug.LogWarning($"[UbiqRoomSession] {LastError}");
            }
        }
    }
}
