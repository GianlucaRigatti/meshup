using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ubiq.SceneSwitcher.UI
{
    public sealed class RoomBrowserPresenter : MonoBehaviour
    {
        [Header("Session")]
        [SerializeField] private RoomSceneSwitcher switcher;

        [Header("Private room")]
        [SerializeField] private GameObject browserPanel;
        [SerializeField] private Transform roomListRoot;
        [SerializeField] private RoomListItemView roomItemTemplate;
        [SerializeField] private InputField roomNameInput;
        [SerializeField] private Toggle publishToggle;
        [SerializeField] private InputField joinCodeInput;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button createButton;
        [SerializeField] private Button joinCodeButton;

        [Header("Multiplayer room")]
        [SerializeField] private GameObject currentRoomPanel;
        [SerializeField] private Text currentRoomName;
        [SerializeField] private Text currentJoinCode;
        [SerializeField] private Button leaveButton;

        [Header("Status")]
        [SerializeField] private GameObject busyOverlay;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private Text errorText;

        private readonly List<RoomListItemView> roomItems = new();
        private bool bound;

        private void Awake()
        {
            switcher ??= RoomSceneSwitcher.Instance;
            if (roomItemTemplate != null)
            {
                roomItemTemplate.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            TryBind();
        }

        private void Start()
        {
            if (isActiveAndEnabled) TryBind();
        }

        private void TryBind()
        {
            if (bound) return;
            switcher ??= RoomSceneSwitcher.Instance;
            if (switcher == null) return;

            switcher.StateChanged += HandleStateChanged;
            switcher.RoomsChanged += HandleRoomsChanged;
            switcher.CurrentRoomChanged += HandleCurrentRoomChanged;
            switcher.OperationFailed += HandleFailure;
            WireButtons(true);
            RenderState(switcher.State);
            RenderRooms(switcher.Rooms);
            RenderCurrentRoom(switcher.CurrentRoom);
            bound = true;
        }

        private void OnDisable()
        {
            if (bound && switcher != null)
            {
                switcher.StateChanged -= HandleStateChanged;
                switcher.RoomsChanged -= HandleRoomsChanged;
                switcher.CurrentRoomChanged -= HandleCurrentRoomChanged;
                switcher.OperationFailed -= HandleFailure;
            }
            WireButtons(false);
            bound = false;
        }

        public void DismissError()
        {
            if (errorPanel != null) errorPanel.SetActive(false);
        }

        private void WireButtons(bool add)
        {
            Wire(refreshButton, Refresh, add);
            Wire(createButton, Create, add);
            Wire(joinCodeButton, JoinCode, add);
            Wire(leaveButton, Leave, add);
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action,
            bool add)
        {
            if (button == null) return;
            if (add) button.onClick.AddListener(action);
            else button.onClick.RemoveListener(action);
        }

        private async void Refresh()
        {
            if (switcher != null) await switcher.RefreshRoomsAsync(destroyCancellationToken);
        }

        private async void Create()
        {
            if (switcher == null) return;
            await switcher.CreateAndEnterAsync(new RoomCreateOptions(
                roomNameInput != null ? roomNameInput.text : string.Empty,
                publishToggle == null || publishToggle.isOn), destroyCancellationToken);
        }

        private async void JoinCode()
        {
            if (switcher == null) return;
            await switcher.JoinByCodeAsync(
                joinCodeInput != null ? joinCodeInput.text : string.Empty,
                destroyCancellationToken);
        }

        private async void Join(RoomSummary room)
        {
            if (switcher != null) await switcher.JoinAsync(room, destroyCancellationToken);
        }

        private async void Leave()
        {
            if (switcher != null)
                await switcher.ReturnToPrivateSceneAsync(destroyCancellationToken);
        }

        private void HandleStateChanged(RoomSceneState state) => RenderState(state);
        private void HandleRoomsChanged(IReadOnlyList<RoomSummary> values) => RenderRooms(values);
        private void HandleCurrentRoomChanged(RoomSummary room) => RenderCurrentRoom(room);

        private void HandleFailure(RoomSceneFailure failure)
        {
            if (errorText != null) errorText.text = failure.Message;
            if (errorPanel != null) errorPanel.SetActive(true);
        }

        private void RenderState(RoomSceneState state)
        {
            var inPrivate = state == RoomSceneState.PrivateRoom;
            var inShared = state == RoomSceneState.MultiplayerRoom
                || state == RoomSceneState.RecoveringConnection;
            var busy = !inPrivate && !inShared && state != RoomSceneState.Faulted;

            if (browserPanel != null) browserPanel.SetActive(inPrivate);
            if (currentRoomPanel != null) currentRoomPanel.SetActive(inShared);
            if (busyOverlay != null) busyOverlay.SetActive(busy);
            if (statusText != null) statusText.text = Humanize(state);
            if (refreshButton != null) refreshButton.interactable = inPrivate;
            if (createButton != null) createButton.interactable = inPrivate;
            if (joinCodeButton != null) joinCodeButton.interactable = inPrivate;
            if (leaveButton != null) leaveButton.interactable = inShared;
            foreach (var item in roomItems) item.SetInteractable(inPrivate);
        }

        private void RenderRooms(IReadOnlyList<RoomSummary> values)
        {
            if (roomItemTemplate == null || roomListRoot == null) return;
            while (roomItems.Count < values.Count)
            {
                var item = Instantiate(roomItemTemplate, roomListRoot);
                item.gameObject.SetActive(true);
                roomItems.Add(item);
            }
            for (var index = 0; index < roomItems.Count; index++)
            {
                var visible = index < values.Count;
                roomItems[index].gameObject.SetActive(visible);
                if (visible) roomItems[index].Bind(values[index], Join);
            }
        }

        private void RenderCurrentRoom(RoomSummary room)
        {
            if (currentRoomName != null) currentRoomName.text = room.Name;
            if (currentJoinCode != null)
                currentJoinCode.text = room.JoinCode.ToUpperInvariant();
        }

        private static string Humanize(RoomSceneState state) => state switch
        {
            RoomSceneState.Initializing => "Connecting…",
            RoomSceneState.LoadingMultiplayerScene => "Loading multiplayer scene…",
            RoomSceneState.JoiningRoom => "Joining room…",
            RoomSceneState.PublishingRoomMetadata => "Publishing room…",
            RoomSceneState.LeavingRoom => "Leaving room…",
            RoomSceneState.LoadingPrivateScene => "Loading private room…",
            RoomSceneState.RecoveringConnection => "Reconnecting…",
            RoomSceneState.Faulted => "Room session unavailable",
            _ => string.Empty
        };

        public void Configure(RoomSceneSwitcher session, GameObject privatePanel,
            Transform listRoot, RoomListItemView itemTemplate, InputField nameInput,
            Toggle publicToggle, InputField codeInput, Button refresh, Button create,
            Button joinByCode, GameObject sharedPanel, Text sharedName, Text sharedCode,
            Button leave, GameObject busy, Text status, GameObject errors,
            Text errorLabel)
        {
            switcher = session;
            browserPanel = privatePanel;
            roomListRoot = listRoot;
            roomItemTemplate = itemTemplate;
            roomNameInput = nameInput;
            publishToggle = publicToggle;
            joinCodeInput = codeInput;
            refreshButton = refresh;
            createButton = create;
            joinCodeButton = joinByCode;
            currentRoomPanel = sharedPanel;
            currentRoomName = sharedName;
            currentJoinCode = sharedCode;
            leaveButton = leave;
            busyOverlay = busy;
            statusText = status;
            errorPanel = errors;
            errorText = errorLabel;
        }
    }
}
