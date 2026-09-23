using System;
using System.Collections.Generic;
using System.Linq;
using Ubiq.Avatars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UbiqAvatar = Ubiq.Avatars.Avatar;

namespace Meshup.Multiplayer
{
    /// <summary>Shows game avatars only in the game scene while keeping Ubiq subscribed.</summary>
    internal sealed class RoomAvatarPresentation : IDisposable
    {
        private readonly AvatarManager manager;
        private readonly string gameSceneName;
        private readonly GameObject gameAvatarPrefab;
        private readonly Dictionary<Renderer, bool> hiddenRenderers = new();

        public RoomAvatarPresentation(AvatarManager manager, string gameSceneName)
        {
            this.manager = manager;
            this.gameSceneName = gameSceneName;
            if (manager == null)
            {
                return;
            }

            // AvatarManager must stay active to receive peer updates in the lobby.
            manager.gameObject.SetActive(true);
            gameAvatarPrefab = manager.avatarPrefab;
            manager.OnAvatarCreated.AddListener(HandleAvatarCreated);
            manager.OnAvatarDestroyed.AddListener(HandleAvatarDestroyed);
        }

        public void Activate()
        {
            if (manager == null)
            {
                return;
            }
            manager.gameObject.SetActive(true);
            SetVisible(true);
            manager.avatarPrefab = gameAvatarPrefab;
        }

        public void Deactivate()
        {
            if (manager == null)
            {
                return;
            }
            SetVisible(false);
            manager.avatarPrefab = null;
        }

        private void HandleAvatarCreated(UbiqAvatar avatar)
        {
            if (SceneManager.GetActiveScene().name != gameSceneName)
            {
                SetVisible(avatar, false);
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
                hiddenRenderers.Remove(renderer);
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (var avatar in manager.Avatars.ToArray())
            {
                SetVisible(avatar, visible);
            }
        }

        private void SetVisible(UbiqAvatar avatar, bool visible)
        {
            if (avatar == null)
            {
                return;
            }
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (visible)
                {
                    if (hiddenRenderers.Remove(renderer, out var wasEnabled))
                    {
                        renderer.enabled = wasEnabled;
                    }
                }
                else if (!hiddenRenderers.ContainsKey(renderer))
                {
                    hiddenRenderers.Add(renderer, renderer.enabled);
                    renderer.enabled = false;
                }
            }
        }

        public void Dispose()
        {
            if (manager == null)
            {
                return;
            }
            manager.OnAvatarCreated.RemoveListener(HandleAvatarCreated);
            manager.OnAvatarDestroyed.RemoveListener(HandleAvatarDestroyed);
            hiddenRenderers.Clear();
        }
    }
}
