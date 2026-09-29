using System.Collections.Generic;
using Meshup.Multiplayer;
using Ubiq.Samples;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Lobby
{
    public sealed class RoomTotemPanel : MonoBehaviour
    {
        private static readonly string[] RoomNameAdjectives =
        {
            "Amber", "Brave", "Bright", "Calm", "Clever", "Cozy",
            "Gentle", "Golden", "Happy", "Lucky", "Moonlit", "Quiet",
            "Silver", "Sunny", "Tiny", "Velvet"
        };

        private static readonly string[] RoomNameNouns =
        {
            "Badger", "Bear", "Comet", "Dragon", "Fox", "Lantern",
            "Otter", "Owl", "Rabbit", "Rocket", "Sparrow", "Star",
            "Tiger", "Whale", "Willow", "Wren"
        };

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextEntry roomNameEntry;
        [SerializeField] private Button createButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform roomListContent;
        [SerializeField] private RoomListItemView roomListItemTemplate;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject noRoomsMessage;
        [SerializeField] private TextEntry usernameEntry;
        [SerializeField] private TextEntry joinCodeEntry;
        [SerializeField] private Text displayedUsernameText;
        [SerializeField] private Button setNameButton;
        [SerializeField] private Button joinCodeButton;
        [SerializeField] private PanelSwitcher panelSwitcher;
        [SerializeField] private float refreshInterval = 2f;
        [SerializeField] private bool allowClose = true;
        [SerializeField] private bool lockPlayerInputWhenOpen = true;

        private readonly List<RoomListItemView> spawnedItems = new();
        private UbiqRoomSession session;
        private LobbyFirstPersonController player;
        private float nextRefreshTime;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;
        public RectTransform PanelRoot => panelRoot != null
            ? panelRoot.transform as RectTransform : null;
        public string GeneratedRoomName => roomNameEntry != null ? roomNameEntry.text.text : string.Empty;

        private void Awake()
        {
            if (panelRoot == null || roomNameEntry == null
                || createButton == null || closeButton == null
                || roomListContent == null || roomListItemTemplate == null
                || statusText == null || noRoomsMessage == null
                || usernameEntry == null || joinCodeEntry == null
                || displayedUsernameText == null || setNameButton == null
                || joinCodeButton == null || panelSwitcher == null)
            {
                enabled = false;
                Debug.LogError("[MeshUp] Lobby menu references are incomplete.", this);
                return;
            }

            createButton.onClick.AddListener(CreateRoom);
            setNameButton.onClick.AddListener(ApplyEnteredUsername);
            joinCodeButton.onClick.AddListener(JoinRoomByCode);
            SetRoomName(GenerateRoomName());
            closeButton.gameObject.SetActive(allowClose);
            if (allowClose)
            {
                closeButton.onClick.AddListener(Close);
            }
            panelRoot.SetActive(false);
            roomListItemTemplate.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (allowClose && Input.GetKeyDown(KeyCode.Escape) && !IsBusy())
            {
                Close();
                return;
            }

            if (session != null
                && session.State == RoomSessionState.LobbyReady
                && Time.unscaledTime >= nextRefreshTime)
            {
                nextRefreshTime = Time.unscaledTime + refreshInterval;
                session.RefreshRooms();
            }
        }

        public void Open(LobbyFirstPersonController lobbyPlayer)
        {
            if (!enabled || panelRoot == null || IsOpen)
            {
                return;
            }

            player = lobbyPlayer;
            if (lockPlayerInputWhenOpen)
            {
                player?.SetInputEnabled(false);
            }
            // The nested Canvas owns the menu graphics and both raycasters.
            // Bind its camera before enabling it so desktop and XR rays agree.
            var canvas = panelRoot.GetComponent<Canvas>();
            canvas.worldCamera = GetComponent<Canvas>().worldCamera ?? Camera.main;
            panelRoot.SetActive(true);
            BindSession();
            if (session != null)
            {
                SetUsernameText(session.LocalDisplayName);
            }
            nextRefreshTime = Time.unscaledTime + refreshInterval;
            RenderState(session != null
                ? session.State
                : RoomSessionState.Error);
            RebuildRoomList(session?.Rooms);

            if (session != null && session.State == RoomSessionState.LobbyReady)
            {
                session.RefreshRooms();
            }
        }

        public void Close()
        {
            if (!allowClose || !IsOpen || IsBusy())
            {
                return;
            }

            UnbindSession();
            panelRoot.SetActive(false);
            if (lockPlayerInputWhenOpen)
            {
                player?.SetInputEnabled(true);
            }
            player = null;
        }

        private void CreateRoom()
        {
            if (session == null)
            {
                ShowError("The room service is unavailable.");
                return;
            }

            ApplyUsername(CurrentUsername());
            session.CreateRoom(roomNameEntry.text.text);
        }

        private void ApplyUsername(string value)
        {
            if (session == null)
            {
                return;
            }

            SetUsernameText(session.SetLocalDisplayName(value));
        }

        private static string GenerateRoomName()
        {
            var adjective = RoomNameAdjectives[Random.Range(0, RoomNameAdjectives.Length)];
            var noun = RoomNameNouns[Random.Range(0, RoomNameNouns.Length)];
            return $"{adjective} {noun}";
        }

        private void BindSession()
        {
            UnbindSession();
            session = UbiqRoomSession.Instance;
            if (session == null)
            {
                ShowError("Connecting to the room service…");
                return;
            }

            session.StateChanged += RenderState;
            session.RoomsChanged += RebuildRoomList;
            session.ErrorOccurred += ShowError;
            SetUsernameText(session.LocalDisplayName);
        }

        private void UnbindSession()
        {
            if (session == null)
            {
                return;
            }

            session.StateChanged -= RenderState;
            session.RoomsChanged -= RebuildRoomList;
            session.ErrorOccurred -= ShowError;
            session = null;
        }

        private void RenderState(RoomSessionState state)
        {
            var ready = state == RoomSessionState.LobbyReady;
            var browsing = ready || state == RoomSessionState.Discovering;
            createButton.interactable = browsing;
            if (setNameButton != null)
            {
                setNameButton.interactable = !IsBusy(state);
            }
            if (joinCodeButton != null)
            {
                joinCodeButton.interactable = browsing;
            }
            closeButton.interactable = !IsBusy(state);
            foreach (var item in spawnedItems)
            {
                item.SetInteractable(browsing);
            }

            statusText.text = state switch
            {
                RoomSessionState.Connecting => "Connecting to the room service…",
                RoomSessionState.LobbyReady => string.IsNullOrEmpty(session?.LastError)
                    ? "MeshUp"
                    : session.LastError,
                RoomSessionState.Discovering => "Finding rooms…",
                RoomSessionState.Joining => "Joining room…",
                RoomSessionState.Publishing => "Publishing room…",
                RoomSessionState.LoadingGame => "Loading game…",
                RoomSessionState.Error => string.IsNullOrEmpty(session?.LastError)
                    ? "The room service is unavailable."
                    : session.LastError,
                _ => statusText.text
            };
        }

        private void RebuildRoomList(IReadOnlyList<RoomListing> listings)
        {
            var existingItems = new Dictionary<string, RoomListItemView>();
            foreach (var item in spawnedItems)
            {
                if (item != null && !string.IsNullOrEmpty(item.RoomKey))
                {
                    existingItems[item.RoomKey] = item;
                }
            }

            var updatedItems = new List<RoomListItemView>();

            if (listings != null)
            {
                foreach (var listing in listings)
                {
                    var roomKey = !string.IsNullOrEmpty(listing.Uuid)
                        ? listing.Uuid
                        : listing.JoinCode;
                    if (!existingItems.Remove(roomKey, out var item))
                    {
                        item = Instantiate(roomListItemTemplate, roomListContent);
                        item.gameObject.SetActive(true);
                    }

                    item.Bind(listing, session,
                        () => ApplyUsername(CurrentUsername()));
                    item.transform.SetAsLastSibling();
                    updatedItems.Add(item);
                }
            }

            foreach (var item in existingItems.Values)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            spawnedItems.Clear();
            spawnedItems.AddRange(updatedItems);

            noRoomsMessage.SetActive(listings == null || listings.Count == 0);
        }

        private void ShowError(string message)
        {
            statusText.text = message;
        }

        private void ApplyEnteredUsername()
        {
            ApplyUsername(CurrentUsername());
            panelSwitcher?.SwitchPanelToDefault();
        }

        private void JoinRoomByCode()
        {
            if (session == null)
            {
                ShowError("The room service is unavailable.");
                return;
            }

            ApplyUsername(CurrentUsername());
            session.JoinRoom(joinCodeEntry.text.text);
        }

        private string CurrentUsername()
        {
            var showingPlaceholder = usernameEntry.text.text == usernameEntry.defaultText
                && usernameEntry.text.color == usernameEntry.defaultTextColor;
            return showingPlaceholder
                ? session?.LocalDisplayName ?? string.Empty
                : usernameEntry.text.text;
        }

        private void SetUsernameText(string value)
        {
            usernameEntry.SetText(usernameEntry.defaultText,
                usernameEntry.defaultTextColor, true);
            displayedUsernameText.text = value;
        }

        private void SetRoomName(string value)
        {
            roomNameEntry.defaultText = value;
            roomNameEntry.SetText(value, roomNameEntry.entryTextColor, false);
        }

        private bool IsBusy()
        {
            return session != null && IsBusy(session.State);
        }

        private static bool IsBusy(RoomSessionState state)
        {
            return state is RoomSessionState.Joining
                or RoomSessionState.Publishing
                or RoomSessionState.LoadingGame
                or RoomSessionState.Leaving;
        }

        private void OnDestroy()
        {
            UnbindSession();
            createButton?.onClick.RemoveListener(CreateRoom);
            setNameButton?.onClick.RemoveListener(ApplyEnteredUsername);
            joinCodeButton?.onClick.RemoveListener(JoinRoomByCode);
            if (allowClose)
            {
                closeButton?.onClick.RemoveListener(Close);
            }
        }
    }
}
