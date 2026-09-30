using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ubiq.Avatars;
using Ubiq.Rooms;
using Ubiq.Voip;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private const string PlayerIdProperty = "meshup.player";
        private const string GameStartedProperty = "meshup.game.started";
        private const string DisplayNamePreference = "meshup.displayname";
        private const int DisplayNameCharacterLimit = 24;

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
        [SerializeField] private int protocolVersion = 2;

        [Header("Scenes")]
        [SerializeField] private string lobbySceneName = "SampleScene";
        [SerializeField] private string gameSceneName = "GameScene";

        [Header("Timeouts")]
        [SerializeField] private float discoveryTimeout = 10f;
        [SerializeField] private float roomOperationTimeout = 15f;
        [SerializeField] private float metadataTimeout = 10f;

        private readonly List<RoomListing> rooms = new();
        private IReadOnlyList<RoomListing> readOnlyRooms;
        private RoomAvatarPresentation avatarPresentation;
        private PendingOperation pendingOperation;
        private RoomListing pendingRoom;
        private string pendingRoomName = string.Empty;
        private string roomBeforeOperation = string.Empty;
        private string recoveryMessage = string.Empty;
        private int operationVersion;
        private bool subscribed;
        private bool awaitingGameRoomRejoin;

        public RoomSessionState State { get; private set; } = RoomSessionState.Connecting;
        public IReadOnlyList<RoomListing> Rooms => readOnlyRooms ??= rooms.AsReadOnly();
        public RoomListing CurrentRoom { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        // Gameplay uses a stable identity advertised in peer properties. Ubiq's
        // connection UUID changes on Reconnect(), but those properties survive.
        public string LocalPeerId => GetGameplayPeerId(roomClient?.Me);
        public string LocalDisplayName { get; private set; } = string.Empty;
        public string CreatorPeerId => roomClient?.Room?[CreatorProperty] ?? string.Empty;
        public bool IsRoomCreator => !string.IsNullOrEmpty(LocalPeerId)
            && string.Equals(LocalPeerId, CreatorPeerId, StringComparison.Ordinal);
        public bool GameStarted => IsGameStarted(roomClient?.Room);
        public int ParticipantCount => GetParticipantIds().Count;

        public event Action<RoomSessionState> StateChanged;
        public event Action<IReadOnlyList<RoomListing>> RoomsChanged;
        public event Action<string> ErrorOccurred;
        public event Action ParticipantsChanged;
        public event Action GameRoomRejoined;

        /// <summary>
        /// Lets a comfort transition cover the view before the game scene loads.
        /// The supplied callback must be invoked when loading may continue.
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

            var voipManager = GetComponentInChildren<
                VoipPeerConnectionManager>(true);
            if (voipManager != null)
            {
                if (voipManager.GetComponent<VoiceChatController>() == null)
                {
                    voipManager.gameObject.AddComponent<VoiceChatController>();
                }
                voipManager.gameObject.SetActive(true);
            }
            else
            {
                Debug.LogError("[MeshUp] Ubiq Voip Manager is missing.");
            }

            avatarPresentation = new RoomAvatarPresentation(avatarManager,
                gameSceneName);
            if (SceneManager.GetActiveScene().name != gameSceneName)
            {
                avatarPresentation.Deactivate();
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
            SetLocalDisplayName(PlayerPrefs.GetString(DisplayNamePreference, string.Empty));

            if (SceneManager.GetActiveScene().name == gameSceneName && roomClient.JoinedRoom)
            {
                avatarPresentation?.Activate();
                SetState(RoomSessionState.InGame);
                return;
            }

            EnterLobby();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Unsubscribe();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            avatarPresentation?.Dispose();
            Instance = null;
        }

        public bool RefreshRooms()
        {
            if (State == RoomSessionState.Error
                && SceneManager.GetActiveScene().name == lobbySceneName)
            {
                roomClient.Reconnect();
                EnterLobby();
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
            PrepareRoomOperation(PendingOperation.Create,
                RoomSessionState.Joining, roomName: trimmedName);
            var version = BeginTimeout();
            roomClient.Join(trimmedName, true);
            StartCoroutine(RoomOperationTimeout(version,
                "Creating the room timed out. Check the network connection and try again."));
            return true;
        }

        /// <summary>
        /// Updates the name advertised with this peer. Blank names receive a
        /// friendly random guest name so every participant is identifiable.
        /// </summary>
        public string SetLocalDisplayName(string displayName)
        {
            var normalized = NormalizeDisplayName(displayName);
            if (string.IsNullOrEmpty(normalized))
            {
                normalized = GenerateGuestDisplayName();
            }

            LocalDisplayName = normalized;
            PlayerPrefs.SetString(DisplayNamePreference, normalized);

            if (roomClient?.Me != null)
            {
                roomClient.Me[Ubiq.DisplayNameManager.KEY] = normalized;
            }

            ParticipantsChanged?.Invoke();
            return normalized;
        }

        public static string NormalizeDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return string.Empty;
            }

            var normalized = string.Join(" ", displayName
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= DisplayNameCharacterLimit
                ? normalized
                : normalized[..DisplayNameCharacterLimit].TrimEnd();
        }

        public static string GenerateGuestDisplayName()
        {
            return $"Guest {UnityEngine.Random.Range(1000, 10000)}";
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
            PrepareRoomOperation(PendingOperation.Join,
                RoomSessionState.Joining, room);
            var version = BeginTimeout();
            roomClient.Join(room.JoinCode);
            StartCoroutine(RoomOperationTimeout(version,
                "Joining the room timed out. Check the network connection and try again."));
            return true;
        }

        public bool JoinRoom(string joinCode)
        {
            var normalized = joinCode?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(normalized))
            {
                ReportError("Enter a room join code first.");
                return false;
            }

            return JoinRoom(new RoomListing(string.Empty, string.Empty,
                normalized));
        }

        public bool LeaveRoom()
        {
            if (State != RoomSessionState.InGame)
            {
                return false;
            }

            LastError = string.Empty;
            BeginPrivateRoom(PendingOperation.Leave,
                "Leaving the room timed out. The network connection was reset.");
            return true;
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            InitializePlayerIdentity();
            roomClient.OnJoinedRoom.AddListener(HandleJoinedRoom);
            roomClient.OnRoomUpdated.AddListener(HandleRoomUpdated);
            roomClient.OnJoinRejected.AddListener(HandleJoinRejected);
            roomClient.OnRooms.AddListener(HandleRoomsDiscovered);
            roomClient.OnPeerAdded.AddListener(HandlePeerChanged);
            roomClient.OnPeerRemoved.AddListener(HandlePeerChanged);
            roomClient.OnPeerUpdated.AddListener(HandlePeerChanged);
            subscribed = true;
        }

        private void InitializePlayerIdentity()
        {
            if (roomClient?.Me != null
                && string.IsNullOrEmpty(roomClient.Me[PlayerIdProperty]))
            {
                roomClient.Me[PlayerIdProperty] = roomClient.Me.uuid;
            }
        }

        private static string GetGameplayPeerId(IPeer peer)
        {
            if (peer == null) return string.Empty;
            var playerId = peer[PlayerIdProperty];
            return string.IsNullOrEmpty(playerId) ? peer.uuid ?? string.Empty : playerId;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == gameSceneName)
            {
                avatarPresentation?.Activate();
            }
            else if (scene.name == lobbySceneName)
            {
                avatarPresentation?.Deactivate();
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
            roomClient.OnPeerUpdated.RemoveListener(HandlePeerChanged);
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
                    .Select(GetGameplayPeerId)
                    .Where(uuid => !string.IsNullOrEmpty(uuid)));
            }

            return result.Distinct(StringComparer.Ordinal)
                .OrderBy(peerId => peerId, StringComparer.Ordinal).ToArray();
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
                    .Select(peer => new ParticipantInfo(GetGameplayPeerId(peer),
                        peer[Ubiq.DisplayNameManager.KEY], true))
                    .Where(participant => !string.IsNullOrEmpty(participant.PeerId)));
            }

            return result.GroupBy(participant => participant.PeerId,
                    StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(participant => participant.PeerId, StringComparer.Ordinal)
                .ToArray();
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

        private void PrepareRoomOperation(PendingOperation operation,
            RoomSessionState state, RoomListing room = null, string roomName = "")
        {
            pendingOperation = operation;
            pendingRoom = room;
            pendingRoomName = roomName;
            awaitingGameRoomRejoin = false;
            roomBeforeOperation = roomClient.Room?.UUID ?? string.Empty;
            SetState(state);
        }

        private void EnterLobby()
        {
            BeginPrivateRoom(PendingOperation.EnterInitialLobby,
                "Connecting to the room service timed out. Press Refresh to retry.");
        }

        private void BeginPrivateRoom(PendingOperation operation,
            string timeoutMessage, bool reconnect = false)
        {
            PrepareRoomOperation(operation, operation == PendingOperation.Leave
                ? RoomSessionState.Leaving
                : RoomSessionState.Connecting);
            if (reconnect)
            {
                roomClient.Reconnect();
            }

            var version = BeginTimeout();
            roomClient.Join(string.Empty, false);
            StartCoroutine(RoomOperationTimeout(version, timeoutMessage));
        }

        private void HandleJoinedRoom(IRoom room)
        {
            if (room == null)
            {
                return;
            }

            // Automatic Ubiq reconnects reset the room, then rejoin it without
            // a user operation. Resume the existing scene, including a started
            // match, and let gameplay request the state missed while offline.
            if (pendingOperation == PendingOperation.None
                && State == RoomSessionState.InGame && CurrentRoom != null)
            {
                if (string.IsNullOrEmpty(room.UUID))
                {
                    awaitingGameRoomRejoin = true;
                }
                else if (awaitingGameRoomRejoin
                    && string.Equals(room.UUID, CurrentRoom.Uuid,
                        StringComparison.Ordinal))
                {
                    awaitingGameRoomRejoin = false;
                    ParticipantsChanged?.Invoke();
                    GameRoomRejoined?.Invoke();
                }
                return;
            }

            switch (pendingOperation)
            {
                case PendingOperation.EnterInitialLobby:
                case PendingOperation.Recover:
                case PendingOperation.Leave:
                    if (IsNewPrivateRoom(room))
                    {
                        CompletePrivateRoom(pendingOperation == PendingOperation.Leave);
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

            HandleRoomOperationFailure(pendingOperation == PendingOperation.Leave
                ? $"Leaving the room was rejected: {reason}"
                : reason);
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
            avatarPresentation?.Deactivate();

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
            BeginPrivateRoom(PendingOperation.Recover,
                "Reconnecting to the lobby timed out. Press Refresh to retry.",
                reconnect: true);
        }

        private IEnumerator LoadScene(string sceneName, bool enteringGame,
            string messageAfterLoad = "")
        {
            operationVersion++;
            pendingOperation = PendingOperation.None;
            SetState(enteringGame
                ? RoomSessionState.LoadingGame
                : RoomSessionState.Leaving);

            var loaded = false;
            yield return RoomSceneLoader.Load(sceneName,
                enteringGame ? GameSceneTransitionRequested : null,
                result => loaded = result);
            if (!loaded)
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

            if (enteringGame)
            {
                avatarPresentation?.Activate();
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

            HandleRoomOperationFailure(message);
        }

        private void HandleRoomOperationFailure(string message)
        {
            switch (pendingOperation)
            {
                case PendingOperation.None:
                    return;
                case PendingOperation.EnterInitialLobby:
                case PendingOperation.Recover:
                    CancelPendingOperation();
                    SetState(RoomSessionState.Error);
                    ReportError(message);
                    break;
                case PendingOperation.Leave:
                    StartCoroutine(ForceReturnToLobby(message));
                    break;
                default:
                    BeginRecovery(message);
                    break;
            }
        }

        private IEnumerator ForceReturnToLobby(string message)
        {
            operationVersion++;
            pendingOperation = PendingOperation.None;
            recoveryMessage = string.Empty;
            CurrentRoom = null;
            avatarPresentation?.Deactivate();
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
