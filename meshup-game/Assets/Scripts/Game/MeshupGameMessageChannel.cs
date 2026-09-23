using System;
using Meshup.Multiplayer;
using Ubiq.Messaging;

namespace Meshup.Game
{
    /// <summary>Owns registration, routing, and sending of game messages.</summary>
    internal sealed class MeshupGameMessageChannel : IDisposable
    {
        private readonly MeshupGameCoordinator owner;
        private readonly UbiqRoomSession session;
        private readonly Func<MeshupGameCoordinator.MessageKind,
            MeshupGameCoordinator.GameMessage, bool> isAuthoritative;
        private readonly Action<MeshupGameCoordinator.MessageKind,
            MeshupGameCoordinator.GameMessage> applyAuthoritative;
        private readonly Action<MeshupGameCoordinator.MessageKind,
            MeshupGameCoordinator.GameMessage> handleHostCommand;
        private readonly NetworkContext context;

        public MeshupGameMessageChannel(MeshupGameCoordinator owner,
            UbiqRoomSession session,
            Func<MeshupGameCoordinator.MessageKind,
                MeshupGameCoordinator.GameMessage, bool> isAuthoritative,
            Action<MeshupGameCoordinator.MessageKind,
                MeshupGameCoordinator.GameMessage> applyAuthoritative,
            Action<MeshupGameCoordinator.MessageKind,
                MeshupGameCoordinator.GameMessage> handleHostCommand)
        {
            this.owner = owner;
            this.session = session;
            this.isAuthoritative = isAuthoritative;
            this.applyAuthoritative = applyAuthoritative;
            this.handleHostCommand = handleHostCommand;
            context = NetworkScene.Register(owner);
        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage networkMessage)
        {
            var message = networkMessage.FromJson<
                MeshupGameCoordinator.GameMessage>();
            if (message == null)
            {
                return;
            }
            var kind = (MeshupGameCoordinator.MessageKind)message.kind;
            if (isAuthoritative(kind, message))
            {
                if (string.Equals(message.creatorPeerId,
                    session?.CreatorPeerId, StringComparison.Ordinal))
                {
                    applyAuthoritative(kind, message);
                }
                return;
            }
            if (session?.IsRoomCreator == true)
            {
                handleHostCommand(kind, message);
            }
        }

        public void Send(MeshupGameCoordinator.GameMessage message)
        {
            if (context.Scene != null)
            {
                context.SendJson(message);
            }
        }

        public void Dispose()
        {
            if (context.Scene != null)
            {
                context.Scene.RemoveProcessor(context.Id, owner.ProcessMessage);
            }
        }
    }
}
