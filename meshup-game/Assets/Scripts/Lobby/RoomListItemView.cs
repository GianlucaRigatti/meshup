using System;
using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Lobby
{
    public sealed class RoomListItemView : MonoBehaviour
    {
        [SerializeField] private Text roomNameText;
        [SerializeField] private Text joinCodeText;
        [SerializeField] private Button joinButton;

        private RoomListing listing;
        private UbiqRoomSession session;
        private Action beforeJoin;

        public string RoomKey => !string.IsNullOrEmpty(listing?.Uuid)
            ? listing.Uuid
            : listing?.JoinCode ?? string.Empty;

        public void Bind(RoomListing room, UbiqRoomSession roomSession,
            Action beforeJoin = null)
        {
            listing = room;
            session = roomSession;
            this.beforeJoin = beforeJoin;
            roomNameText.text = string.IsNullOrWhiteSpace(room.Name)
                ? "Unnamed Room"
                : room.Name;
            joinCodeText.text = $"Code: {room.JoinCode}";
            joinButton.onClick.RemoveListener(Join);
            joinButton.onClick.AddListener(Join);
            joinButton.interactable = true;
        }

        private void Join()
        {
            if (listing != null && session != null)
            {
                beforeJoin?.Invoke();
                session.JoinRoom(listing);
            }
        }

        public void SetInteractable(bool interactable)
        {
            joinButton.interactable = interactable;
        }

        private void OnDestroy()
        {
            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(Join);
            }
        }
    }
}
