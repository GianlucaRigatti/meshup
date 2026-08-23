using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Ubiq.SceneSwitcher
{
    public readonly struct GatewayRoomResult
    {
        public bool Succeeded { get; }
        public RoomSummary Room { get; }
        public RoomSceneFailure Failure { get; }

        public GatewayRoomResult(bool succeeded, RoomSummary room,
            RoomSceneFailure failure)
        {
            Succeeded = succeeded;
            Room = room;
            Failure = failure;
        }
    }

    public readonly struct GatewayRoomsResult
    {
        public bool Succeeded { get; }
        public IReadOnlyList<RoomSummary> Rooms { get; }
        public RoomSceneFailure Failure { get; }

        public GatewayRoomsResult(bool succeeded, IReadOnlyList<RoomSummary> rooms,
            RoomSceneFailure failure)
        {
            Succeeded = succeeded;
            Rooms = rooms;
            Failure = failure;
        }
    }

    public interface IUbiqRoomGateway : IDisposable
    {
        event Action<RoomSummary> RoomJoined;

        Awaitable<GatewayRoomsResult> DiscoverAsync(string joinCode,
            float timeoutSeconds, CancellationToken token);
        Awaitable<GatewayRoomResult> CreateAsync(string name, bool publish,
            float timeoutSeconds, CancellationToken token);
        Awaitable<GatewayRoomResult> JoinAsync(string joinCode,
            float timeoutSeconds, CancellationToken token);
        Awaitable<GatewayRoomResult> EnterPrivateAsync(float timeoutSeconds,
            CancellationToken token);
        Awaitable<GatewayRoomResult> PublishMetadataAsync(string applicationId,
            string sceneId, int protocolVersion, float timeoutSeconds,
            CancellationToken token);
    }
}
