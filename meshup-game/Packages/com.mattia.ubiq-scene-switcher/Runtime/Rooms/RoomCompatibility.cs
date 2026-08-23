using System;
using System.Collections.Generic;
using Ubiq.Rooms;

namespace Ubiq.SceneSwitcher
{
    public static class RoomCompatibility
    {
        public const string ApplicationKey = "ubiq.scene-switcher.app";
        public const string SceneKey = "ubiq.scene-switcher.scene";
        public const string ProtocolKey = "ubiq.scene-switcher.protocol";

        public static RoomSummary Copy(IRoom room)
        {
            if (room == null)
            {
                return RoomSummary.Empty;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in room)
            {
                properties[property.Key] = property.Value;
            }

            properties.TryGetValue(ApplicationKey, out var applicationId);
            properties.TryGetValue(SceneKey, out var sceneId);
            properties.TryGetValue(ProtocolKey, out var protocolValue);
            _ = int.TryParse(protocolValue, out var protocolVersion);

            return new RoomSummary(
                room.Name ?? string.Empty,
                room.UUID ?? string.Empty,
                (room.JoinCode ?? string.Empty).ToLowerInvariant(),
                room.Publish,
                applicationId ?? string.Empty,
                sceneId ?? string.Empty,
                protocolVersion);
        }

        public static bool IsCompatible(RoomSummary room, string applicationId,
            string sceneId, int protocolVersion)
        {
            return !room.IsEmpty
                && string.Equals(room.ApplicationId, applicationId, StringComparison.Ordinal)
                && string.Equals(room.SceneId, sceneId, StringComparison.Ordinal)
                && room.ProtocolVersion == protocolVersion;
        }

        public static RoomSceneFailure GetCompatibilityFailure(RoomSummary room,
            string applicationId, string sceneId, int protocolVersion)
        {
            if (!string.Equals(room.ApplicationId, applicationId, StringComparison.Ordinal))
            {
                return new RoomSceneFailure(RoomSceneFailureCode.IncompatibleApplication,
                    "The room belongs to a different application.");
            }
            if (!string.Equals(room.SceneId, sceneId, StringComparison.Ordinal))
            {
                return new RoomSceneFailure(RoomSceneFailureCode.IncompatibleScene,
                    "The room targets a different Unity scene.");
            }
            if (room.ProtocolVersion != protocolVersion)
            {
                return new RoomSceneFailure(RoomSceneFailureCode.IncompatibleProtocol,
                    "The room uses a different protocol version.");
            }
            return RoomSceneFailure.None;
        }

        public static string NormalizeJoinCode(string joinCode) =>
            (joinCode ?? string.Empty).Trim().ToLowerInvariant();
    }
}
