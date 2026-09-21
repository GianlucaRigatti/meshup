using System;
using Meshup.Multiplayer;
using Meshup;
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

        /// <summary>
        /// Connects MeshUp's room-list behaviour to the controls authored in
        /// Ubiq's Browse Menu Control prefab.
        /// </summary>
        public void ConfigureFromUbiqSample()
        {
            var browseControl = GetComponent<Ubiq.Samples.BrowseMenuControl>();
            roomNameText = browseControl != null
                ? browseControl.Name
                : FindText("NameText");
            joinCodeText = browseControl != null
                ? browseControl.SceneName
                : FindText("SceneText");
            joinButton = FindChild("Next Button")?.GetComponent<Button>();

            var sampleJoin = GetComponentInChildren<
                Ubiq.Samples.BrowseMenuControlJoinButton>(true);
            if (sampleJoin != null)
            {
                sampleJoin.enabled = false;
                Destroy(sampleJoin);
            }
            if (browseControl != null)
            {
                browseControl.enabled = false;
            }
        }

        public void Bind(RoomListing room, UbiqRoomSession roomSession,
            Action beforeJoin = null)
        {
            listing = room;
            session = roomSession;
            this.beforeJoin = beforeJoin;
            if (roomNameText == null || joinCodeText == null
                || joinButton == null)
            {
                ConfigureFromUbiqSample();
            }
            roomNameText.text = string.IsNullOrWhiteSpace(room.Name)
                ? "Unnamed Room"
                : room.Name;
            joinCodeText.text = $"Join code: {room.JoinCode}";
            // The copied Ubiq control has a persistent listener that talks
            // directly to RoomClient. Replace it so MeshUp remains the sole
            // owner of room transitions and late-join validation.
            joinButton.onClick = new Button.ButtonClickedEvent();
            joinButton.onClick.AddListener(Join);
            joinButton.interactable = true;
        }

        private Text FindText(string childName)
        {
            return FindChild(childName)?.GetComponent<Text>();
        }

        private Transform FindChild(string childName)
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }
            return null;
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
