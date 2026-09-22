using System.Collections.Generic;
using Meshup.Multiplayer;
using Meshup;
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
        [SerializeField] private InputField usernameInput;
        [SerializeField] private Text roomNameText;
        [SerializeField] private Button createButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform roomListContent;
        [SerializeField] private RoomListItemView roomListItemTemplate;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject noRoomsMessage;
        [SerializeField] private GameObject ubiqKeyboardPrefab;
        [SerializeField] private GameObject ubiqMenuPrefab;
        [SerializeField] private float refreshInterval = 2f;
        [SerializeField] private bool allowClose = true;
        [SerializeField] private bool lockPlayerInputWhenOpen = true;

        private readonly List<RoomListItemView> spawnedItems = new();
        private UbiqRoomSession session;
        private LobbyFirstPersonController player;
        private float nextRefreshTime;
        private bool usesUbiqSampleMenu;
        private Text ubiqUsernameText;
        private Text ubiqJoinCodeText;
        private Text displayedUsernameText;
        private Button setNameButton;
        private Button joinCodeButton;
        private PanelSwitcher panelSwitcher;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;
        public string GeneratedRoomName => roomNameText != null ? roomNameText.text : string.Empty;

        private void Awake()
        {
            if (ubiqMenuPrefab != null)
            {
                BuildUbiqSampleMenu();
            }

            var canvas = GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                transform.localScale = Vector3.one;
            }
            if (usesUbiqSampleMenu)
            {
                createButton.onClick = new Button.ButtonClickedEvent();
            }
            createButton.onClick.AddListener(CreateRoom);
            refreshButton?.onClick.AddListener(RefreshRooms);
            if (!usesUbiqSampleMenu)
            {
                usernameInput.onEndEdit.AddListener(ApplyUsername);
                var nativeKeyboard = usernameInput.gameObject.GetComponent<
                    QuestNativeKeyboardInput>()
                    ?? usernameInput.gameObject.AddComponent<
                        QuestNativeKeyboardInput>();
                nativeKeyboard.Initialize(ApplyUsername, ubiqKeyboardPrefab,
                    panelRoot.transform as RectTransform);
                UbiqUiTheme.ApplyTo(panelRoot, true);
            }
            SetRoomName(GenerateRoomName());
            closeButton.gameObject.SetActive(allowClose);
            if (allowClose)
            {
                closeButton.onClick.AddListener(Close);
            }
            panelRoot.SetActive(false);
            roomListItemTemplate.gameObject.SetActive(false);
        }

        private void BuildUbiqSampleMenu()
        {
            var oldPanel = panelRoot;
            var staging = new GameObject("Ubiq Menu Staging");
            staging.SetActive(false);
            var instance = Instantiate(ubiqMenuPrefab, staging.transform, false);
            var canvasRoot = instance.transform.Find("Canvas");
            var mainPanel = canvasRoot?.Find("Main Panel");
            if (canvasRoot == null || mainPanel == null)
            {
                Destroy(staging);
                Debug.LogError("[MeshUp] The copied Ubiq Menu prefab no longer "
                    + "contains Canvas/Main Panel.");
                return;
            }

            canvasRoot.gameObject.SetActive(false);
            StripUbiqNetworkActions(mainPanel.gameObject);
            canvasRoot.SetParent(transform, false);
            canvasRoot.name = "Ubiq Sample Menu";
            panelRoot = canvasRoot.gameObject;
            usesUbiqSampleMenu = true;
            oldPanel?.SetActive(false);

            var rootRect = canvasRoot as RectTransform;
            if (rootRect != null)
            {
                rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.anchoredPosition = Vector2.zero;
                rootRect.localRotation = Quaternion.identity;
                // Ubiq authors this world-space Canvas at 0.005 units per
                // UI pixel. Compensate for the existing hologram Canvas scale
                // so the copied menu keeps its original physical size.
                const float ubiqWorldScale = 0.005f;
                var parentWorldScale = Mathf.Max(
                    Mathf.Abs(transform.lossyScale.x), 0.00001f);
                rootRect.localScale = Vector3.one
                    * (ubiqWorldScale / parentWorldScale);
            }

            var nestedCanvas = canvasRoot.GetComponent<Canvas>();
            var parentCanvas = GetComponent<Canvas>();
            nestedCanvas.worldCamera = parentCanvas.worldCamera;
            nestedCanvas.overrideSorting = true;
            nestedCanvas.sortingOrder = parentCanvas.sortingOrder + 1;

            panelSwitcher = mainPanel.GetComponent<PanelSwitcher>();

            var menuPanel = Find(mainPanel, "Menu Panel");
            var statusPanel = Find(menuPanel, "Status Panel");
            statusText = Find(statusPanel, "Title")?.GetComponent<Text>();
            closeButton = Find(statusPanel, "Close Button")?.GetComponent<Button>();
            displayedUsernameText = Find(menuPanel, "User Panel/NameText")
                ?.GetComponent<Text>();

            var setNamePanel = Find(mainPanel, "Set Name Panel");
            ubiqUsernameText = Find(setNamePanel, "Content/Text Input Area/Text")
                ?.GetComponent<Text>();
            var usernameEntry = ubiqUsernameText
                ?.GetComponentInParent<TextEntry>();
            if (usernameEntry != null)
            {
                usernameEntry.defaultText = "Name";
                usernameEntry.SetText("Name", usernameEntry.defaultTextColor,
                    true);
            }
            setNameButton = Find(setNamePanel,
                "Content/Text Input Area/Next Button")?.GetComponent<Button>();
            ResetButton(setNameButton, ApplySampleUsername);

            var newRoomPanel = Find(mainPanel, "New Room Panel");
            roomNameText = Find(newRoomPanel, "Content/Text Input Area/Text")
                ?.GetComponent<Text>();
            createButton = Find(newRoomPanel,
                "Content/Text Input Area/Next Button")?.GetComponent<Button>();

            var joinRoomPanel = Find(mainPanel, "Join Room Panel");
            ubiqJoinCodeText = Find(joinRoomPanel,
                "Content/Text Input Area/Text")?.GetComponent<Text>();
            joinCodeButton = Find(joinRoomPanel,
                "Content/Text Input Area/Next Button")?.GetComponent<Button>();
            ResetButton(joinCodeButton, JoinRoomByCode);

            var browsePanel = Find(mainPanel, "Browse Panel");
            roomListContent = Find(browsePanel,
                "Room List/Viewport/Controls");
            noRoomsMessage = Find(browsePanel, "No Rooms")?.gameObject;
            var joinedControl = Find(roomListContent,
                "Browse Menu Joined Control");
            joinedControl?.gameObject.SetActive(false);
            var template = Find(browsePanel, "Browse Menu Control Template");
            roomListItemTemplate = template.GetComponent<RoomListItemView>()
                ?? template.gameObject.AddComponent<RoomListItemView>();
            roomListItemTemplate.ConfigureFromUbiqSample();

            Find(menuPanel, "User Panel/User Customization Buttons /Avatar")
                ?.gameObject.SetActive(false);
            Find(menuPanel, "User Panel/System Buttons/System")
                ?.gameObject.SetActive(false);
            Find(menuPanel, "Current Room Panel/In Room")
                ?.gameObject.SetActive(false);
            Find(menuPanel, "Current Room Panel/Not In Room")
                ?.gameObject.SetActive(true);

            Destroy(staging);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            CorrectLegacyNamePlaceholder();

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

        private void LateUpdate()
        {
            if (IsOpen)
            {
                CorrectLegacyNamePlaceholder();
            }
        }

        public void Open(LobbyFirstPersonController lobbyPlayer)
        {
            if (IsOpen)
            {
                return;
            }

            player = lobbyPlayer;
            if (lockPlayerInputWhenOpen)
            {
                player?.SetInputEnabled(false);
            }
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
            if (!usesUbiqSampleMenu)
            {
                usernameInput.GetComponent<QuestNativeKeyboardInput>()
                    ?.HideKeyboard();
            }
            session.CreateRoom(roomNameText.text);
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

        private void RefreshRooms()
        {
            if (session == null)
            {
                BindSession();
            }

            if (session == null || !session.RefreshRooms())
            {
                if (session == null)
                {
                    ShowError("The room service is unavailable.");
                }
            }
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
            if (refreshButton != null)
            {
                refreshButton.interactable = browsing
                    || state == RoomSessionState.Error;
            }
            if (usernameInput != null && !usesUbiqSampleMenu)
            {
                usernameInput.interactable = !IsBusy(state);
            }
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
                    ? usesUbiqSampleMenu ? "MeshUp" : string.Empty
                    : session.LastError,
                RoomSessionState.Discovering => usesUbiqSampleMenu
                    ? "Finding rooms…"
                    : string.Empty,
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

        private void ApplySampleUsername()
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
            session.JoinRoom(ubiqJoinCodeText?.text);
        }

        private string CurrentUsername()
        {
            if (!usesUbiqSampleMenu)
            {
                return usernameInput?.text ?? string.Empty;
            }

            var entry = ubiqUsernameText?.GetComponent<TextEntry>();
            var showingPlaceholder = entry != null
                && ubiqUsernameText.text == entry.defaultText
                && ubiqUsernameText.color == entry.defaultTextColor;
            return showingPlaceholder
                ? session?.LocalDisplayName ?? string.Empty
                : ubiqUsernameText?.text ?? string.Empty;
        }

        private void CorrectLegacyNamePlaceholder()
        {
            if (!usesUbiqSampleMenu || ubiqUsernameText == null
                || ubiqUsernameText.text != "My Room")
            {
                return;
            }

            var entry = ubiqUsernameText.GetComponent<TextEntry>();
            if (entry != null && ubiqUsernameText.color.a < 0.9f)
            {
                entry.defaultText = "Name";
                entry.SetText("Name", entry.defaultTextColor, true);
            }
        }

        private void SetUsernameText(string value)
        {
            if (usesUbiqSampleMenu)
            {
                if (ubiqUsernameText != null)
                {
                    var entry = ubiqUsernameText.GetComponentInParent<TextEntry>();
                    if (entry != null)
                    {
                        entry.defaultText = "Name";
                        entry.SetText("Name", entry.defaultTextColor, true);
                    }
                    else
                    {
                        ubiqUsernameText.text = value;
                    }
                }
                if (displayedUsernameText != null)
                {
                    displayedUsernameText.text = value;
                }
            }
            else
            {
                usernameInput?.SetTextWithoutNotify(value);
            }
        }

        private void SetRoomName(string value)
        {
            var entry = usesUbiqSampleMenu
                ? roomNameText?.GetComponentInParent<TextEntry>()
                : null;
            if (entry != null)
            {
                entry.defaultText = value;
                entry.SetText(value, entry.entryTextColor, false);
            }
            else if (roomNameText != null)
            {
                roomNameText.text = value;
            }
        }

        private static void ResetButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
        }

        private static Transform Find(Transform root, string relativePath)
        {
            return root != null ? root.Find(relativePath) : null;
        }

        private static void StripUbiqNetworkActions(GameObject root)
        {
            Strip<BrowsePanelController>(root);
            Strip<CurrentRoomPanel>(root);
            Strip<CurrentRoomPanelControl>(root);
            Strip<JoinRoomButton>(root);
            Strip<LeaveRoomButton>(root);
            Strip<NewRoomButton>(root);
            Strip<Ubiq.Samples.Social.SetNameButton>(root);
            Strip<Ubiq.Samples.Social.NameTextEntry>(root);
            Strip<PeersPanelController>(root);
            Strip<PeersPanelControl>(root);
            Strip<Ubiq.Samples.Social.UserPanelController>(root);
        }

        private static void Strip<T>(GameObject root) where T : Behaviour
        {
            foreach (var component in root.GetComponentsInChildren<T>(true))
            {
                component.enabled = false;
                Destroy(component);
            }
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
            refreshButton?.onClick.RemoveListener(RefreshRooms);
            if (!usesUbiqSampleMenu)
            {
                usernameInput?.onEndEdit.RemoveListener(ApplyUsername);
            }
            setNameButton?.onClick.RemoveListener(ApplySampleUsername);
            joinCodeButton?.onClick.RemoveListener(JoinRoomByCode);
            if (allowClose)
            {
                closeButton?.onClick.RemoveListener(Close);
            }
        }
    }
}
