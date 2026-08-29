using System;
using System.Collections.Generic;
using System.Linq;
using Meshup.Multiplayer;
using Ubiq.Messaging;
using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupGameCoordinator : MonoBehaviour
    {
        private enum MessageKind
        {
            Snapshot,
            PrivateWords,
            MimeCrossing,
            SelectWord,
            StartTimer,
            Guess,
            GenerationRequest,
            GenerationAuthorized,
            GenerationComplete,
            ObjectTransform,
            RequestSnapshot
        }

        [Serializable]
        private sealed class GameMessage
        {
            public int kind;
            public string creatorPeerId;
            public string senderPeerId;
            public string targetPeerId;
            public string requestId;
            public string text;
            public string error;
            public int intValue;
            public string[] words;
            public MeshupMatchSnapshot snapshot;
            public MeshupGeneratedObjectState generatedObject;
        }

        [Serializable]
        private sealed class RuntimeConfiguration
        {
            public string assetServerBaseUrl;
        }

        [Header("Scene")]
        [SerializeField] private GameStartCoordinator gameStart;
        [SerializeField] private PlayerMovementAuthority localPlayer;
        [SerializeField] private Collider invisibleWall;
        [SerializeField] private Transform guesserMonitor;
        [SerializeField] private Transform mimeTerminal;
        [SerializeField] private Transform generatorAnchor;
        [SerializeField] private GameObject generatorButton;
        [SerializeField] private ParticleSystem generatorParticles;

        [Header("Services")]
        [SerializeField] private string assetServerBaseUrl =
            "http://127.0.0.1:8000";

        private readonly List<MeshupGeneratedObjectState> generatedStates = new();
        private readonly Dictionary<string, MeshupGeneratedObject> generatedObjects =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> pendingAudio =
            new(StringComparer.Ordinal);
        private UbiqRoomSession session;
        private MeshupMatchState hostState;
        private MeshupMatchSnapshot snapshot = new();
        private MimeWordService wordService;
        private NetworkContext context;
        private bool contextRegistered;
        private MeshupGameView view;
        private MetaGuessTranscriber transcriber;
        private MeshupAssetGeneratorClient generatorClient;
        private string[] privateWordOptions = Array.Empty<string>();
        private string privateSelectedWord = string.Empty;
        private string activeGenerationRequest = string.Empty;
        private string transientMessage = string.Empty;
        private float previousWallSide;
        private int crossingSentVersion = -1;
        private bool originalWallEnabled;

        public MeshupMatchSnapshot CurrentSnapshot => snapshot;
        public bool CanRecordGeneratorLocally => IsLocalMime
            && CurrentPhase == MeshupGamePhase.Preparation
            && snapshot.generationTokens > 0 && !snapshot.generationPending;
        public bool CanManipulateGeneratedObjectsLocally => IsLocalMime
            && CurrentPhase is MeshupGamePhase.Preparation
                or MeshupGamePhase.TimedGuessing;
        public bool CanGuessLocally => !IsLocalMime
            && CurrentPhase == MeshupGamePhase.TimedGuessing;

        private bool IsLocalMime => session != null
            && snapshot.mimePeerId == session.LocalPeerId;
        private MeshupGamePhase CurrentPhase =>
            (MeshupGamePhase)snapshot.phase;

        public void Configure(GameStartCoordinator startCoordinator,
            PlayerMovementAuthority player, Collider wall, Transform monitor,
            Transform terminal, Transform assetAnchor, GameObject assetButton,
            ParticleSystem particles)
        {
            gameStart = startCoordinator;
            localPlayer = player;
            invisibleWall = wall;
            guesserMonitor = monitor;
            mimeTerminal = terminal;
            generatorAnchor = assetAnchor;
            generatorButton = assetButton;
            generatorParticles = particles;
        }

        private void Start()
        {
            session = UbiqRoomSession.Instance;
            if (session == null || gameStart == null || localPlayer == null
                || invisibleWall == null || guesserMonitor == null
                || mimeTerminal == null || generatorAnchor == null
                || generatorButton == null)
            {
                enabled = false;
                Debug.LogError("[MeshUp] Game coordinator references are incomplete.");
                return;
            }

            wordService = MimeWordService.LoadDefault();
            LoadRuntimeConfiguration();
            context = NetworkScene.Register(this);
            contextRegistered = true;
            originalWallEnabled = invisibleWall.enabled;
            previousWallSide = WallSide;
            session.ParticipantsChanged += HandleParticipantsChanged;
            gameStart.Completed += HandleWalkCompleted;

            view = gameObject.AddComponent<MeshupGameView>();
            view.Build(guesserMonitor, mimeTerminal, ChooseWord, StartRound);
            transcriber = gameObject.AddComponent<MetaGuessTranscriber>();
            transcriber.Configure(() => CanGuessLocally);
            transcriber.TranscriptionReceived += SubmitGuess;
            transcriber.ErrorOccurred += ReportLocalMessage;
            generatorClient = generatorButton.GetComponent<
                MeshupAssetGeneratorClient>()
                ?? generatorButton.AddComponent<MeshupAssetGeneratorClient>();
            generatorClient.Configure(this);
            SetParticleState(false);
            Render();

            if (gameStart.IsComplete)
            {
                HandleWalkCompleted();
            }
            else
            {
                Send(new GameMessage
                {
                    kind = (int)MessageKind.RequestSnapshot,
                    senderPeerId = session.LocalPeerId
                });
            }
        }

        private void Update()
        {
            if (session == null)
            {
                return;
            }
            UpdateWallAndCrossing();
            if (session.IsRoomCreator && hostState != null
                && hostState.Tick(Time.unscaledDeltaTime))
            {
                BroadcastSnapshot();
            }
        }

        private void HandleWalkCompleted()
        {
            if (!session.IsRoomCreator || hostState != null)
            {
                return;
            }
            hostState = new MeshupMatchState();
            hostState.Begin(session.GetParticipants());
            BroadcastSnapshot();
        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage networkMessage)
        {
            var message = networkMessage.FromJson<GameMessage>();
            var kind = (MessageKind)message.kind;
            if (kind is MessageKind.Snapshot or MessageKind.PrivateWords
                or MessageKind.GenerationAuthorized
                or MessageKind.ObjectTransform)
            {
                if (!string.Equals(message.creatorPeerId,
                    session?.CreatorPeerId, StringComparison.Ordinal))
                {
                    return;
                }
                ProcessAuthoritativeMessage(kind, message);
                return;
            }
            if (session?.IsRoomCreator == true && hostState != null)
            {
                ProcessHostCommand(kind, message);
            }
        }

        private void ProcessAuthoritativeMessage(MessageKind kind,
            GameMessage message)
        {
            switch (kind)
            {
                case MessageKind.Snapshot:
                    if (message.snapshot != null
                        && message.snapshot.version >= snapshot.version)
                    {
                        snapshot = message.snapshot;
                        SetParticleState(snapshot.generationPending);
                        ReconcileGeneratedObjects();
                        Render();
                    }
                    break;
                case MessageKind.PrivateWords:
                    if (message.targetPeerId == session.LocalPeerId)
                    {
                        privateWordOptions = message.words ?? Array.Empty<string>();
                        privateSelectedWord = message.text ?? string.Empty;
                        Render();
                    }
                    break;
                case MessageKind.GenerationAuthorized:
                    if (message.targetPeerId == session.LocalPeerId
                        && pendingAudio.Remove(message.requestId, out var wav))
                    {
                        generatorClient.UploadAuthorized(message.requestId, wav,
                            assetServerBaseUrl);
                    }
                    break;
                case MessageKind.ObjectTransform:
                    if (message.generatedObject != null
                        && generatedObjects.TryGetValue(
                            message.generatedObject.objectId, out var instance))
                    {
                        instance.ApplyState(message.generatedObject, false);
                    }
                    break;
            }
        }

        private void ProcessHostCommand(MessageKind kind, GameMessage message)
        {
            if (string.IsNullOrEmpty(message.senderPeerId))
            {
                return;
            }
            switch (kind)
            {
                case MessageKind.MimeCrossing:
                    if (message.intValue > 0)
                    {
                        var options = wordService.GetDistinctRandomVerbs(2,
                            new System.Random());
                        if (hostState.MimeEntered(message.senderPeerId, options))
                        {
                            SendPrivateWords(message.senderPeerId, options, "");
                            BroadcastSnapshot();
                        }
                    }
                    else if (hostState.MimeExited(message.senderPeerId))
                    {
                        ClearGeneratedObjects();
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.SelectWord:
                    if (hostState.SelectWord(message.senderPeerId,
                        message.intValue))
                    {
                        SendPrivateWords(message.senderPeerId,
                            Array.Empty<string>(), hostState.SelectedWord);
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.StartTimer:
                    if (hostState.StartTimer(message.senderPeerId))
                    {
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.Guess:
                    if (hostState.SubmitGuess(message.senderPeerId, message.text))
                    {
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.GenerationRequest:
                    if (hostState.TryBeginGeneration(message.senderPeerId))
                    {
                        activeGenerationRequest = message.requestId;
                        BroadcastSnapshot();
                        Send(new GameMessage
                        {
                            kind = (int)MessageKind.GenerationAuthorized,
                            creatorPeerId = session.LocalPeerId,
                            targetPeerId = message.senderPeerId,
                            requestId = message.requestId
                        });
                        if (message.senderPeerId == session.LocalPeerId)
                        {
                            ProcessAuthoritativeMessage(
                                MessageKind.GenerationAuthorized,
                                new GameMessage
                                {
                                    targetPeerId = session.LocalPeerId,
                                    requestId = message.requestId
                                });
                        }
                    }
                    break;
                case MessageKind.GenerationComplete:
                    if (message.senderPeerId == hostState.MimePeerId
                        && message.requestId == activeGenerationRequest)
                    {
                        activeGenerationRequest = string.Empty;
                        hostState.EndGeneration();
                        if (!string.IsNullOrWhiteSpace(message.text))
                        {
                            generatedStates.Add(new MeshupGeneratedObjectState
                            {
                                objectId = Guid.NewGuid().ToString("N"),
                                url = message.text,
                                position = generatorAnchor.position
                                    + generatorAnchor.up * 0.4f,
                                rotation = Quaternion.identity,
                                scale = Vector3.one
                            });
                        }
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.ObjectTransform:
                    if (message.senderPeerId == hostState.MimePeerId
                        && message.generatedObject != null)
                    {
                        var state = generatedStates.FirstOrDefault(item =>
                            item.objectId == message.generatedObject.objectId);
                        if (state != null)
                        {
                            state.position = message.generatedObject.position;
                            state.rotation = message.generatedObject.rotation;
                            state.scale = message.generatedObject.scale;
                            BroadcastObjectTransform(state);
                        }
                    }
                    break;
                case MessageKind.RequestSnapshot:
                    BroadcastSnapshot();
                    if (message.senderPeerId == hostState.MimePeerId)
                    {
                        SendPrivateWords(message.senderPeerId,
                            hostState.WordOptions, hostState.SelectedWord);
                    }
                    break;
            }
        }

        private void ChooseWord(int option)
        {
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.SelectWord,
                intValue = option
            });
        }

        private void StartRound()
        {
            SendCommand(new GameMessage { kind = (int)MessageKind.StartTimer });
        }

        private void SubmitGuess(string transcription)
        {
            if (!CanGuessLocally)
            {
                return;
            }
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.Guess,
                text = transcription
            });
        }

        public void RequestGeneration(byte[] wav)
        {
            if (!CanRecordGeneratorLocally || wav == null || wav.Length == 0)
            {
                return;
            }
            var requestId = Guid.NewGuid().ToString("N");
            pendingAudio[requestId] = wav;
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.GenerationRequest,
                requestId = requestId
            });
        }

        public void CompleteGeneration(string requestId, string url, string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                ReportLocalMessage(error);
            }
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.GenerationComplete,
                requestId = requestId,
                text = url ?? string.Empty,
                error = error ?? string.Empty
            });
        }

        public void SubmitObjectTransform(string objectId, Transform value,
            bool final)
        {
            var state = new MeshupGeneratedObjectState
            {
                objectId = objectId,
                position = value.position,
                rotation = value.rotation,
                scale = value.localScale
            };
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.ObjectTransform,
                intValue = final ? 1 : 0,
                generatedObject = state
            });
        }

        public void ReportLocalMessage(string message)
        {
            transientMessage = message ?? string.Empty;
            Render();
        }

        private void UpdateWallAndCrossing()
        {
            var allowMime = IsLocalMime && CurrentPhase is
                MeshupGamePhase.CallingMime or MeshupGamePhase.ChoosingWord
                or MeshupGamePhase.Preparation or MeshupGamePhase.TimedGuessing
                or MeshupGamePhase.Result;
            invisibleWall.enabled = originalWallEnabled && !allowMime;
            var side = WallSide;
            if (crossingSentVersion != snapshot.version && IsLocalMime)
            {
                if (CurrentPhase == MeshupGamePhase.CallingMime
                    && previousWallSide >= 0f && side < 0f)
                {
                    crossingSentVersion = snapshot.version;
                    SendCommand(new GameMessage
                    {
                        kind = (int)MessageKind.MimeCrossing,
                        intValue = 1
                    });
                }
                else if (CurrentPhase == MeshupGamePhase.Result
                    && previousWallSide <= 0f && side > 0f)
                {
                    crossingSentVersion = snapshot.version;
                    SendCommand(new GameMessage
                    {
                        kind = (int)MessageKind.MimeCrossing,
                        intValue = -1
                    });
                }
            }
            previousWallSide = side;
        }

        private float WallSide => invisibleWall.transform
            .InverseTransformPoint(localPlayer.transform.position).z;

        private void SendCommand(GameMessage message)
        {
            message.senderPeerId = session.LocalPeerId;
            if (session.IsRoomCreator && hostState != null)
            {
                ProcessHostCommand((MessageKind)message.kind, message);
            }
            else
            {
                Send(message);
            }
        }

        private void BroadcastSnapshot()
        {
            snapshot = hostState.CreateSnapshot(generatedStates);
            ProcessAuthoritativeMessage(MessageKind.Snapshot,
                new GameMessage { snapshot = snapshot });
            Send(new GameMessage
            {
                kind = (int)MessageKind.Snapshot,
                creatorPeerId = session.LocalPeerId,
                snapshot = snapshot
            });
        }

        private void SendPrivateWords(string target, string[] words,
            string selectedWord)
        {
            var message = new GameMessage
            {
                kind = (int)MessageKind.PrivateWords,
                creatorPeerId = session.LocalPeerId,
                targetPeerId = target,
                words = words,
                text = selectedWord
            };
            if (target == session.LocalPeerId)
            {
                ProcessAuthoritativeMessage(MessageKind.PrivateWords, message);
            }
            Send(message);
        }

        private void BroadcastObjectTransform(MeshupGeneratedObjectState state)
        {
            var message = new GameMessage
            {
                kind = (int)MessageKind.ObjectTransform,
                creatorPeerId = session.LocalPeerId,
                generatedObject = state
            };
            ProcessAuthoritativeMessage(MessageKind.ObjectTransform, message);
            Send(message);
        }

        private void Send(GameMessage message)
        {
            if (contextRegistered && context.Scene != null)
            {
                context.SendJson(message);
            }
        }

        private void HandleParticipantsChanged()
        {
            if (!session.IsRoomCreator || hostState == null)
            {
                return;
            }
            var connected = new HashSet<string>(session.GetParticipantIds(),
                StringComparer.Ordinal);
            var changed = false;
            foreach (var player in hostState.Players.Where(item =>
                item.connected && !connected.Contains(item.peerId)).ToArray())
            {
                var wasMime = player.peerId == hostState.MimePeerId;
                changed |= hostState.Disconnect(player.peerId);
                if (wasMime)
                {
                    ClearGeneratedObjects();
                }
            }
            if (changed)
            {
                BroadcastSnapshot();
            }
        }

        private async void LoadGeneratedObject(MeshupGeneratedObjectState state)
        {
            var gameObject = new GameObject();
            var generated = gameObject.AddComponent<MeshupGeneratedObject>();
            generatedObjects[state.objectId] = generated;
            if (!await generated.Initialize(this, state))
            {
                generatedObjects.Remove(state.objectId);
                Destroy(gameObject);
                ReportLocalMessage("A generated model could not be loaded.");
            }
        }

        private void ReconcileGeneratedObjects()
        {
            var expected = new HashSet<string>(snapshot.generatedObjects
                .Select(item => item.objectId), StringComparer.Ordinal);
            foreach (var pair in generatedObjects.ToArray())
            {
                if (!expected.Contains(pair.Key))
                {
                    Destroy(pair.Value.gameObject);
                    generatedObjects.Remove(pair.Key);
                }
            }
            foreach (var state in snapshot.generatedObjects)
            {
                if (generatedObjects.TryGetValue(state.objectId, out var instance))
                {
                    instance.ApplyState(state, false);
                }
                else
                {
                    LoadGeneratedObject(state);
                }
            }
        }

        private void ClearGeneratedObjects()
        {
            generatedStates.Clear();
            foreach (var instance in generatedObjects.Values)
            {
                if (instance != null)
                {
                    Destroy(instance.gameObject);
                }
            }
            generatedObjects.Clear();
            pendingAudio.Clear();
            activeGenerationRequest = string.Empty;
        }

        private void SetParticleState(bool active)
        {
            if (generatorParticles == null)
            {
                return;
            }
            if (active && !generatorParticles.isPlaying)
            {
                generatorParticles.Play(true);
            }
            else if (!active && generatorParticles.isPlaying)
            {
                generatorParticles.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Render()
        {
            view?.Render(snapshot, session?.LocalPeerId ?? string.Empty,
                privateWordOptions, privateSelectedWord, transientMessage);
        }

        private void LoadRuntimeConfiguration()
        {
            var configuration = Resources.Load<TextAsset>(
                "Game/meshup_game_config");
            if (configuration == null)
            {
                return;
            }
            try
            {
                var parsed = JsonUtility.FromJson<RuntimeConfiguration>(
                    configuration.text);
                if (!string.IsNullOrWhiteSpace(parsed?.assetServerBaseUrl))
                {
                    assetServerBaseUrl = parsed.assetServerBaseUrl.Trim();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MeshUp] Invalid game configuration: "
                    + exception.Message);
            }
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.ParticipantsChanged -= HandleParticipantsChanged;
            }
            if (gameStart != null)
            {
                gameStart.Completed -= HandleWalkCompleted;
            }
            if (transcriber != null)
            {
                transcriber.TranscriptionReceived -= SubmitGuess;
                transcriber.ErrorOccurred -= ReportLocalMessage;
            }
            if (invisibleWall != null)
            {
                invisibleWall.enabled = originalWallEnabled;
            }
            if (contextRegistered && context.Scene != null)
            {
                context.Scene.RemoveProcessor(context.Id, ProcessMessage);
            }
        }
    }
}
