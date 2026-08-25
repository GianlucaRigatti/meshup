using System.Collections.Generic;
using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Lobby
{
    public sealed class RoomTotemPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private InputField roomNameInput;
        [SerializeField] private Button createButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform roomListContent;
        [SerializeField] private RoomListItemView roomListItemTemplate;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject noRoomsMessage;
        [SerializeField] private float refreshInterval = 2f;

        private readonly List<RoomListItemView> spawnedItems = new();
        private UbiqRoomSession session;
        private LobbyFirstPersonController player;
        private float nextRefreshTime;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            transform.localScale = Vector3.one;
            createButton.onClick.AddListener(CreateRoom);
            refreshButton.onClick.AddListener(RefreshRooms);
            closeButton.onClick.AddListener(Close);
            panelRoot.SetActive(false);
            roomListItemTemplate.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) && !IsBusy())
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
            if (IsOpen)
            {
                return;
            }

            player = lobbyPlayer;
            player?.SetInputEnabled(false);
            panelRoot.SetActive(true);
            BindSession();
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
            if (!IsOpen || IsBusy())
            {
                return;
            }

            UnbindSession();
            panelRoot.SetActive(false);
            player?.SetInputEnabled(true);
            player = null;
        }

        private void CreateRoom()
        {
            if (session == null)
            {
                ShowError("The room service is unavailable.");
                return;
            }

            session.CreateRoom(roomNameInput.text);
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
            refreshButton.interactable = browsing || state == RoomSessionState.Error;
            closeButton.interactable = !IsBusy(state);
            roomNameInput.interactable = browsing;
            foreach (var item in spawnedItems)
            {
                item.SetInteractable(browsing);
            }

            statusText.text = state switch
            {
                RoomSessionState.Connecting => "Connecting to the room service…",
                RoomSessionState.LobbyReady => string.IsNullOrEmpty(session?.LastError)
                    ? "Choose a room or create a new one."
                    : session.LastError,
                RoomSessionState.Discovering => "Refreshing rooms…",
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

                    item.Bind(listing, session);
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
            closeButton?.onClick.RemoveListener(Close);
        }
    }
}
