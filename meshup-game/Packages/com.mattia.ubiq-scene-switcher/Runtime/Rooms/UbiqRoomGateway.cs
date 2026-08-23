using System;
using System.Collections.Generic;
using System.Threading;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.Events;

namespace Ubiq.SceneSwitcher
{
    public sealed class UbiqRoomGateway : IUbiqRoomGateway
    {
        private readonly RoomClient roomClient;
        private bool disposed;

        public event Action<RoomSummary> RoomJoined;

        public UbiqRoomGateway(RoomClient roomClient)
        {
            this.roomClient = roomClient != null
                ? roomClient
                : throw new ArgumentNullException(nameof(roomClient));
            roomClient.OnJoinedRoom.AddListener(HandleAnyRoomJoined);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (roomClient != null)
            {
                roomClient.OnJoinedRoom.RemoveListener(HandleAnyRoomJoined);
            }
        }

        public async Awaitable<GatewayRoomsResult> DiscoverAsync(string joinCode,
            float timeoutSeconds, CancellationToken token)
        {
            var normalizedCode = RoomCompatibility.NormalizeJoinCode(joinCode);
            List<RoomSummary> result = null;

            UnityAction<List<IRoom>, RoomsDiscoveredRequest> handler = (rooms, request) =>
            {
                if (!string.Equals(RoomCompatibility.NormalizeJoinCode(request.joincode),
                    normalizedCode, StringComparison.Ordinal))
                {
                    return;
                }

                result = new List<RoomSummary>(rooms.Count);
                foreach (var room in rooms)
                {
                    result.Add(RoomCompatibility.Copy(room));
                }
            };

            roomClient.OnRooms.AddListener(handler);
            try
            {
                roomClient.DiscoverRooms(normalizedCode);
                var failure = await WaitUntilAsync(() => result != null,
                    timeoutSeconds, token, RoomSceneFailureCode.ConnectionLost,
                    "Room discovery timed out.");
                return failure.Code == RoomSceneFailureCode.None
                    ? new GatewayRoomsResult(true, result, RoomSceneFailure.None)
                    : new GatewayRoomsResult(false, Array.Empty<RoomSummary>(), failure);
            }
            finally
            {
                roomClient.OnRooms.RemoveListener(handler);
            }
        }

        public Awaitable<GatewayRoomResult> CreateAsync(string name, bool publish,
            float timeoutSeconds, CancellationToken token)
        {
            return AwaitJoinAsync(() => roomClient.Join(name, publish),
                room => string.Equals(room.Name, name, StringComparison.Ordinal)
                    && room.IsPublished == publish,
                rejection => string.Equals(rejection.name, name, StringComparison.Ordinal)
                    && rejection.publish == publish,
                timeoutSeconds, token, RoomSceneFailureCode.JoinTimedOut,
                "Creating the room timed out.");
        }

        public Awaitable<GatewayRoomResult> JoinAsync(string joinCode,
            float timeoutSeconds, CancellationToken token)
        {
            var normalizedCode = RoomCompatibility.NormalizeJoinCode(joinCode);
            return AwaitJoinAsync(() => roomClient.Join(normalizedCode),
                room => string.Equals(room.JoinCode, normalizedCode,
                    StringComparison.OrdinalIgnoreCase),
                rejection => string.Equals(
                    RoomCompatibility.NormalizeJoinCode(rejection.joincode),
                    normalizedCode, StringComparison.Ordinal),
                timeoutSeconds, token, RoomSceneFailureCode.JoinTimedOut,
                "Joining the room timed out.");
        }

        public Awaitable<GatewayRoomResult> EnterPrivateAsync(float timeoutSeconds,
            CancellationToken token)
        {
            return AwaitJoinAsync(() => roomClient.Join(string.Empty, false),
                room => string.IsNullOrEmpty(room.Name) && !room.IsPublished,
                rejection => string.IsNullOrEmpty(rejection.name)
                    && !rejection.publish,
                timeoutSeconds, token, RoomSceneFailureCode.LeaveTimedOut,
                "Leaving the shared room timed out.");
        }

        public async Awaitable<GatewayRoomResult> PublishMetadataAsync(
            string applicationId, string sceneId, int protocolVersion,
            float timeoutSeconds, CancellationToken token)
        {
            RoomSummary updated = RoomSummary.Empty;
            bool Matches(IRoom room)
            {
                var summary = RoomCompatibility.Copy(room);
                if (!RoomCompatibility.IsCompatible(summary, applicationId,
                    sceneId, protocolVersion))
                {
                    return false;
                }
                updated = summary;
                return true;
            }

            UnityAction<IRoom> handler = room => _ = Matches(room);
            roomClient.OnRoomUpdated.AddListener(handler);
            try
            {
                roomClient.Room[RoomCompatibility.ApplicationKey] = applicationId;
                roomClient.Room[RoomCompatibility.SceneKey] = sceneId;
                roomClient.Room[RoomCompatibility.ProtocolKey] = protocolVersion.ToString();

                if (Matches(roomClient.Room))
                {
                    return new GatewayRoomResult(true, updated, RoomSceneFailure.None);
                }

                var failure = await WaitUntilAsync(() => !updated.IsEmpty,
                    timeoutSeconds, token, RoomSceneFailureCode.MetadataTimedOut,
                    "The room server did not confirm compatibility metadata.");
                return failure.Code == RoomSceneFailureCode.None
                    ? new GatewayRoomResult(true, updated, RoomSceneFailure.None)
                    : new GatewayRoomResult(false, RoomSummary.Empty, failure);
            }
            finally
            {
                roomClient.OnRoomUpdated.RemoveListener(handler);
            }
        }

        private async Awaitable<GatewayRoomResult> AwaitJoinAsync(Action command,
            Func<RoomSummary, bool> acceptsRoom,
            Func<Rejection, bool> acceptsRejection,
            float timeoutSeconds, CancellationToken token,
            RoomSceneFailureCode timeoutCode, string timeoutMessage)
        {
            RoomSummary joined = RoomSummary.Empty;
            Rejection? rejection = null;

            UnityAction<IRoom> joinedHandler = room =>
            {
                var candidate = RoomCompatibility.Copy(room);
                if (acceptsRoom(candidate)) joined = candidate;
            };
            UnityAction<Rejection> rejectedHandler = value =>
            {
                if (acceptsRejection(value)) rejection = value;
            };

            roomClient.OnJoinedRoom.AddListener(joinedHandler);
            roomClient.OnJoinRejected.AddListener(rejectedHandler);
            try
            {
                command();
                var failure = await WaitUntilAsync(
                    () => !joined.IsEmpty || rejection.HasValue,
                    timeoutSeconds, token, timeoutCode, timeoutMessage);

                if (failure.Code != RoomSceneFailureCode.None)
                {
                    return new GatewayRoomResult(false, RoomSummary.Empty, failure);
                }
                if (rejection.HasValue)
                {
                    return new GatewayRoomResult(false, RoomSummary.Empty,
                        new RoomSceneFailure(RoomSceneFailureCode.JoinRejected,
                            rejection.Value.reason ?? "The room server rejected the request."));
                }
                return new GatewayRoomResult(true, joined, RoomSceneFailure.None);
            }
            finally
            {
                roomClient.OnJoinedRoom.RemoveListener(joinedHandler);
                roomClient.OnJoinRejected.RemoveListener(rejectedHandler);
            }
        }

        private static async Awaitable<RoomSceneFailure> WaitUntilAsync(
            Func<bool> predicate, float timeoutSeconds, CancellationToken token,
            RoomSceneFailureCode timeoutCode, string timeoutMessage)
        {
            var started = Time.realtimeSinceStartupAsDouble;
            while (!predicate())
            {
                if (token.IsCancellationRequested)
                {
                    return new RoomSceneFailure(RoomSceneFailureCode.Cancelled,
                        "The operation was cancelled.");
                }
                if (Time.realtimeSinceStartupAsDouble - started >= timeoutSeconds)
                {
                    return new RoomSceneFailure(timeoutCode, timeoutMessage);
                }
                await Awaitable.NextFrameAsync();
            }
            return RoomSceneFailure.None;
        }

        private void HandleAnyRoomJoined(IRoom room)
        {
            RoomJoined?.Invoke(RoomCompatibility.Copy(room));
        }
    }
}
