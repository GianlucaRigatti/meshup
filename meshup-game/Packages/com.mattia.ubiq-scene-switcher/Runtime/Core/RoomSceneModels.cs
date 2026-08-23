using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace Ubiq.SceneSwitcher
{
    public enum RoomSceneState
    {
        Initializing,
        PrivateRoom,
        LoadingMultiplayerScene,
        JoiningRoom,
        PublishingRoomMetadata,
        MultiplayerRoom,
        LeavingRoom,
        LoadingPrivateScene,
        RecoveringConnection,
        Faulted
    }

    public enum RoomSceneFailureCode
    {
        None,
        Busy,
        InvalidConfiguration,
        InvalidRoomName,
        InvalidJoinCode,
        RoomNotFound,
        IncompatibleApplication,
        IncompatibleScene,
        IncompatibleProtocol,
        SceneLoadFailed,
        SceneAnchorMissing,
        SceneAnchorDuplicated,
        JoinRejected,
        JoinTimedOut,
        MetadataTimedOut,
        LeaveTimedOut,
        Cancelled,
        ConnectionLost,
        UnexpectedRoom
    }

    [Serializable]
    public readonly struct RoomSummary
    {
        public string Name { get; }
        public string Uuid { get; }
        public string JoinCode { get; }
        public bool IsPublished { get; }
        public string ApplicationId { get; }
        public string SceneId { get; }
        public int ProtocolVersion { get; }

        public RoomSummary(string name, string uuid, string joinCode,
            bool isPublished, string applicationId, string sceneId,
            int protocolVersion)
        {
            Name = name;
            Uuid = uuid;
            JoinCode = joinCode;
            IsPublished = isPublished;
            ApplicationId = applicationId;
            SceneId = sceneId;
            ProtocolVersion = protocolVersion;
        }

        public static RoomSummary Empty => new(string.Empty, string.Empty,
            string.Empty, false, string.Empty, string.Empty, 0);

        public bool IsEmpty => string.IsNullOrEmpty(Uuid);
    }

    [Serializable]
    public readonly struct RoomCreateOptions
    {
        public string Name { get; }
        public bool Publish { get; }

        public RoomCreateOptions(string name, bool publish)
        {
            Name = name;
            Publish = publish;
        }
    }

    [Serializable]
    public sealed class RoomSceneFailure
    {
        public static readonly RoomSceneFailure None =
            new(RoomSceneFailureCode.None, string.Empty);

        public RoomSceneFailureCode Code { get; }
        public string Message { get; }

        public RoomSceneFailure(RoomSceneFailureCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public override string ToString() => Code == RoomSceneFailureCode.None
            ? "None"
            : $"{Code}: {Message}";
    }

    public readonly struct RoomTransitionResult
    {
        public bool Succeeded { get; }
        public RoomSummary Room { get; }
        public RoomSceneFailure Failure { get; }

        public RoomTransitionResult(bool succeeded, RoomSummary room,
            RoomSceneFailure failure)
        {
            Succeeded = succeeded;
            Room = room;
            Failure = failure;
        }

        public static RoomTransitionResult Success(RoomSummary room) =>
            new(true, room, RoomSceneFailure.None);

        public static RoomTransitionResult Fail(RoomSceneFailure failure) =>
            new(false, RoomSummary.Empty, failure);
    }

    public readonly struct RoomListResult
    {
        public bool Succeeded { get; }
        public IReadOnlyList<RoomSummary> Rooms { get; }
        public RoomSceneFailure Failure { get; }

        public RoomListResult(bool succeeded, IReadOnlyList<RoomSummary> rooms,
            RoomSceneFailure failure)
        {
            Succeeded = succeeded;
            Rooms = rooms;
            Failure = failure;
        }
    }

    [Serializable] public sealed class RoomSceneStateEvent : UnityEvent<RoomSceneState> { }
    [Serializable] public sealed class RoomSummaryEvent : UnityEvent<RoomSummary> { }
    [Serializable] public sealed class RoomSceneFailureEvent : UnityEvent<RoomSceneFailure> { }
}
