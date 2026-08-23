using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ubiq.SceneSwitcher.UI
{
    public sealed class RoomListItemView : MonoBehaviour
    {
        [SerializeField] private Text roomName;
        [SerializeField] private Text joinCode;
        [SerializeField] private Button joinButton;

        private RoomSummary room;
        private Action<RoomSummary> joinRequested;

        public void Bind(RoomSummary value, Action<RoomSummary> callback)
        {
            room = value;
            joinRequested = callback;
            if (roomName != null) roomName.text = room.Name;
            if (joinCode != null) joinCode.text = room.JoinCode.ToUpperInvariant();
            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(Join);
                joinButton.onClick.AddListener(Join);
                joinButton.interactable = !string.IsNullOrEmpty(room.JoinCode);
            }
        }

        public void SetInteractable(bool value)
        {
            if (joinButton != null) joinButton.interactable = value;
        }

        private void Join() => joinRequested?.Invoke(room);

        public void Configure(Text nameLabel, Text codeLabel, Button button)
        {
            roomName = nameLabel;
            joinCode = codeLabel;
            joinButton = button;
        }
    }
}
