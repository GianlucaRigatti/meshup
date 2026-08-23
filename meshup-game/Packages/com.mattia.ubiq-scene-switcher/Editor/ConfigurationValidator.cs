using System;
using System.Collections.Generic;
using System.Linq;
using Ubiq.Messaging;
using Ubiq.Rooms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ubiq.SceneSwitcher.Editor
{
    public static class ConfigurationValidator
    {
        public readonly struct Message
        {
            public MessageType Type { get; }
            public string Text { get; }

            public Message(MessageType type, string text)
            {
                Type = type;
                Text = text;
            }
        }

        public static IReadOnlyList<Message> Validate(RoomSceneSwitcher switcher)
        {
            var messages = new List<Message>();
            if (switcher.transform.parent != null)
                messages.Add(Error("The session object must be a scene root."));
            if (switcher.GetComponent<NetworkScene>() == null)
                messages.Add(Error("Add NetworkScene to the session root."));
            if (switcher.RoomClient == null)
                messages.Add(Error("Assign a RoomClient on the session root."));
            if (switcher.PlayerRigRoot == null
                || !switcher.PlayerRigRoot.IsChildOf(switcher.transform))
                messages.Add(Error("The player rig must be under the persistent session root."));
            if (string.IsNullOrWhiteSpace(switcher.ApplicationId))
                messages.Add(Error("Application ID cannot be empty."));
            if (switcher.ProtocolVersion < 1)
                messages.Add(Error("Protocol version must be positive."));

            ValidateSceneReference(switcher.PrivateScene, "Private", messages);
            ValidateSceneReference(switcher.MultiplayerScene, "Multiplayer", messages);
            if (switcher.PrivateScene != null && switcher.MultiplayerScene != null
                && switcher.PrivateScene.IsConfigured
                && switcher.PrivateScene.AssetGuid == switcher.MultiplayerScene.AssetGuid)
                messages.Add(Error("Private and multiplayer scenes must be different."));

            if (switcher.GetComponentsInChildren<RoomJoiner>(true).Length > 0)
                messages.Add(Error("Remove RoomJoiner components; the switcher owns all joins."));

            if (messages.Count == 0)
                messages.Add(new Message(MessageType.Info,
                    "Base configuration is valid. Use Validate Scene Contents for anchor checks."));
            return messages;
        }

        public static IReadOnlyList<Message> ValidateSceneContents(
            SerializableSceneReference reference, RoomSceneRole role)
        {
            var messages = new List<Message>();
            if (reference == null || !reference.IsConfigured)
            {
                messages.Add(Error($"The {role} scene is not configured."));
                return messages;
            }

            var existing = SceneManager.GetSceneByPath(reference.ScenePath);
            var openedHere = !existing.IsValid() || !existing.isLoaded;
            var scene = openedHere
                ? EditorSceneManager.OpenScene(reference.ScenePath, OpenSceneMode.Additive)
                : existing;
            try
            {
                var anchors = ComponentsInScene<RoomSceneAnchor>(scene).ToList();
                var matching = anchors.Count(anchor => anchor.Role == role);
                if (anchors.Count == 0)
                    messages.Add(Error($"{role} scene needs one {role} RoomSceneAnchor."));
                else if (anchors.Count > 1)
                    messages.Add(Error($"{role} scene must contain exactly one RoomSceneAnchor."));
                else if (matching == 0)
                    messages.Add(Error($"The {role} scene anchor has the wrong role."));

                if (role == RoomSceneRole.Multiplayer)
                {
                    if (ComponentsInScene<NetworkScene>(scene)
                        .Any(component => component.transform.parent == null))
                        messages.Add(Error("The multiplayer scene must not contain a root NetworkScene."));
                    if (ComponentsInScene<RoomClient>(scene).Any())
                        messages.Add(Error("The multiplayer scene must not contain a RoomClient."));
                    if (ComponentsInScene<AudioListener>(scene).Any())
                        messages.Add(new Message(MessageType.Warning,
                            "The multiplayer scene contains an AudioListener; the persistent rig should own it."));
                }
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }

            if (messages.Count == 0)
                messages.Add(new Message(MessageType.Info,
                    $"{role} scene contents are valid."));
            return messages;
        }

        public static bool IsInBuildSettings(string path) =>
            EditorBuildSettings.scenes.Any(scene => scene.enabled
                && string.Equals(scene.path, path, StringComparison.Ordinal));

        public static void AddConfiguredScenesToBuildSettings(RoomSceneSwitcher switcher)
        {
            var paths = EditorBuildSettings.scenes.Select(scene => scene.path).ToList();
            var scenes = EditorBuildSettings.scenes.ToList();
            Add(switcher.PrivateScene);
            Add(switcher.MultiplayerScene);
            EditorBuildSettings.scenes = scenes.ToArray();

            void Add(SerializableSceneReference reference)
            {
                if (reference == null || !reference.IsConfigured
                    || paths.Contains(reference.ScenePath)) return;
                scenes.Add(new EditorBuildSettingsScene(reference.ScenePath, true));
                paths.Add(reference.ScenePath);
            }
        }

        private static IEnumerable<T> ComponentsInScene<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        private static void ValidateSceneReference(SerializableSceneReference reference,
            string label, List<Message> messages)
        {
            if (reference == null || !reference.IsConfigured)
            {
                messages.Add(Error($"Assign the {label.ToLowerInvariant()} scene."));
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(reference.ScenePath) == null)
                messages.Add(Error($"{label} scene path is invalid."));
            else if (!IsInBuildSettings(reference.ScenePath))
                messages.Add(new Message(MessageType.Warning,
                    $"{label} scene is not enabled in Build Settings."));
        }

        private static Message Error(string text) => new(MessageType.Error, text);
    }
}
