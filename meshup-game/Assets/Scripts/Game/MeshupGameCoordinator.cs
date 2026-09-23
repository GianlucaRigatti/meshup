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
        // TODO: Remove this temporary binding after the fireworks are approved.
        private const KeyCode FireworksTestKey = KeyCode.F8;
        private const KeyCode GeneratorAudioTestKey = KeyCode.F7;
        private const KeyCode GeneratorFailureAudioTestKey = KeyCode.F9;

        internal enum MessageKind
        {
            Snapshot,
            PrivateWords,
            MimeCrossing,
            SelectWord,
            StartTimer,
            Guess,
            GuessFeedback,
            GenerationRequest,
            GenerationAuthorized,
            GenerationComplete,
            ObjectTransform,
            RequestSnapshot,
            SelectSize
        }

        [Serializable]
        internal sealed class GameMessage
        {
            public int kind;
            public string creatorPeerId;
            public string senderPeerId;
            public string targetPeerId;
            public string requestId;
            public string text;
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
        [SerializeField] private Transform monitorUiFrontMount;
        [SerializeField] private Transform monitorUiBackMount;
        [SerializeField] private Transform mimeTerminal;
        [SerializeField] private Transform terminalUiMount;
        [SerializeField] private Transform generatorAnchor;
        [SerializeField] private GameObject generatorButton;
        [SerializeField] private ParticleSystem generatorParticles;
        [SerializeField] private GameObject smallSizeButton;
        [SerializeField] private GameObject mediumSizeButton;
        [SerializeField] private GameObject extraLargeSizeButton;

        [Header("Services")]
        [SerializeField] private string assetServerBaseUrl =
            "http://127.0.0.1:8000";

        private GeneratedObjectManager generatedObjects;
        private readonly Dictionary<string, byte[]> pendingAudio =
            new(StringComparer.Ordinal);
        private UbiqRoomSession session;
        private MeshupMatchState hostState;
        private MeshupMatchSnapshot snapshot = new();
        private MimeWordService wordService;
        private MeshupGameMessageChannel messageChannel;
        private MeshupGameView view;
        private MeshupVictoryFireworks victoryFireworks;
        private CorrectGuessAudio correctGuessAudio;
        private VoskGuessTranscriber transcriber;
        private MeshupAssetGeneratorClient generatorClient;
        private GeneratedObjectSizeSelector sizeSelector;
        private GeneratorActivityAudio generatorActivityAudio;
        private MeshupGameSnapshotEffects snapshotEffects;
        private string[] privateWordOptions = Array.Empty<string>();
        private string privateSelectedWord = string.Empty;
        private string activeGenerationRequest = string.Empty;
        private GeneratedObjectSize activeGenerationSize =
            GeneratedObjectSize.Medium;
        private string transientMessage = string.Empty;
        private string guessFeedback = string.Empty;
        private float guessFeedbackUntil;
        private bool guessListening;
        private bool hasAppliedSnapshot;
        private MeshupGamePhase lastAppliedPhase;
        private float previousWallSide;
        private int crossingSentVersion = -1;
        private bool originalWallEnabled;
        private bool wallStateCaptured;
        private bool generatorAudioPreview;

        public MeshupMatchSnapshot CurrentSnapshot => snapshot;
        public bool CanRecordGeneratorLocally => IsLocalMime
            && CurrentPhase == MeshupGamePhase.Preparation
            && snapshot.generationTokens > 0 && !snapshot.generationPending;
        public bool CanManipulateGeneratedObjectsLocally => IsLocalMime
            && CurrentPhase is MeshupGamePhase.Preparation
                or MeshupGamePhase.TimedGuessing;
        public bool CanGuessLocally => !IsLocalMime
            && CurrentPhase == MeshupGamePhase.TimedGuessing;
        public GeneratedObjectSize SelectedGeneratedObjectSize =>
            sizeSelector != null ? sizeSelector.SelectedSize
                : GeneratedObjectSize.Medium;

        private bool IsLocalMime => session != null
            && snapshot.mimePeerId == session.LocalPeerId;
        private MeshupGamePhase CurrentPhase =>
            (MeshupGamePhase)snapshot.phase;

        private void Start()
        {
            session = UbiqRoomSession.Instance;
            if (session == null || gameStart == null || localPlayer == null
                || invisibleWall == null || guesserMonitor == null
                || monitorUiFrontMount == null || monitorUiBackMount == null
                || mimeTerminal == null || terminalUiMount == null
                || generatorAnchor == null
                || generatorButton == null || smallSizeButton == null
                || mediumSizeButton == null || extraLargeSizeButton == null)
            {
                enabled = false;
                Debug.LogError("[MeshUp] Game coordinator references are incomplete.");
                return;
            }

            wordService = MimeWordService.LoadDefault();
            LoadRuntimeConfiguration();
            generatedObjects = new GeneratedObjectManager(generatorAnchor,
                (instance, state, cancellation) =>
                    instance.Initialize(this, state, cancellation),
                ReportLocalMessage);
            messageChannel = new MeshupGameMessageChannel(this, session,
                IsAuthoritativeInbound,
                (kind, message) => ProcessAuthoritativeMessage(kind, message),
                (kind, message) =>
                {
                    if (hostState != null) ProcessHostCommand(kind, message);
                });
            originalWallEnabled = invisibleWall.enabled;
            wallStateCaptured = true;
            previousWallSide = WallSide;
            session.ParticipantsChanged += HandleParticipantsChanged;
            gameStart.Completed += HandleWalkCompleted;

            view = gameObject.AddComponent<MeshupGameView>();
            view.Build(monitorUiFrontMount, monitorUiBackMount,
                terminalUiMount, localPlayer.transform, ChooseWord, StartRound);
            victoryFireworks = gameObject.AddComponent<MeshupVictoryFireworks>();
            victoryFireworks.Configure(guesserMonitor);
            correctGuessAudio = guesserMonitor.GetComponent<CorrectGuessAudio>()
                ?? guesserMonitor.gameObject.AddComponent<CorrectGuessAudio>();
            correctGuessAudio.Configure();
            transcriber = gameObject.AddComponent<VoskGuessTranscriber>();
            transcriber.Configure(() => CanGuessLocally, wordService.Verbs);
            transcriber.TranscriptionReceived += SubmitGuess;
            transcriber.ErrorOccurred += ReportLocalMessage;
            transcriber.ListeningChanged += HandleListeningChanged;
            generatorClient = generatorButton.GetComponent<
                MeshupAssetGeneratorClient>()
                ?? generatorButton.AddComponent<MeshupAssetGeneratorClient>();
            generatorClient.Configure(this);
            generatorActivityAudio = generatorAnchor.GetComponent<
                GeneratorActivityAudio>()
                ?? generatorAnchor.gameObject.AddComponent<
                    GeneratorActivityAudio>();
            sizeSelector = gameObject.AddComponent<GeneratedObjectSizeSelector>();
            sizeSelector.Configure(smallSizeButton, mediumSizeButton,
                extraLargeSizeButton, () => CanRecordGeneratorLocally, RequestSizeSelection);
            snapshotEffects = new MeshupGameSnapshotEffects(generatedObjects,
                sizeSelector, generatorActivityAudio, correctGuessAudio,
                victoryFireworks, SetParticleState);
            SetParticleState(false);
            Render();

            if (gameStart.IsComplete)
            {
                HandleWalkCompleted();
            }
            if (!session.IsRoomCreator)
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
            if (Input.GetKeyDown(GeneratorAudioTestKey))
            {
                ToggleGeneratorAudioPreview();
            }
            if (Input.GetKeyDown(GeneratorFailureAudioTestKey))
            {
                PreviewGeneratorFailure();
            }
            if (session == null)
            {
                return;
            }
            if (Input.GetKeyDown(FireworksTestKey))
            {
                victoryFireworks?.PlayForTesting();
            }
            UpdateWallAndCrossing();
            if (guessFeedbackUntil > 0f
                && Time.unscaledTime >= guessFeedbackUntil)
            {
                guessFeedback = string.Empty;
                guessFeedbackUntil = 0f;
                Render();
            }
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
            messageChannel?.ProcessMessage(networkMessage);
        }

        private static bool IsAuthoritativeInbound(MessageKind kind,
            GameMessage message)
        {
            return kind is MessageKind.Snapshot or MessageKind.PrivateWords
                    or MessageKind.GenerationAuthorized
                    or MessageKind.GuessFeedback
                || (kind == MessageKind.ObjectTransform
                    && !string.IsNullOrEmpty(message.creatorPeerId));
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
                        snapshotEffects.Apply(snapshot, message.snapshot,
                            hasAppliedSnapshot, lastAppliedPhase);
                        snapshot = message.snapshot;
                        lastAppliedPhase = (MeshupGamePhase)snapshot.phase;
                        hasAppliedSnapshot = true;
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
                case MessageKind.GuessFeedback:
                    if (message.targetPeerId == session.LocalPeerId)
                    {
                        guessFeedback = message.text ?? string.Empty;
                        guessFeedbackUntil = Time.unscaledTime + 2.5f;
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
                    generatedObjects.ApplyTransform(message.generatedObject);
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
                        ClearRoundGeneration();
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
                    else if (hostState.Phase == MeshupGamePhase.TimedGuessing
                        && message.senderPeerId != hostState.MimePeerId
                        && hostState.Players.Any(item => item.connected
                            && item.peerId == message.senderPeerId))
                    {
                        SendGuessFeedback(message.senderPeerId, message.text);
                    }
                    break;
                case MessageKind.SelectSize:
                    if (hostState.TrySelectSize(message.senderPeerId,
                        (GeneratedObjectSize)message.intValue))
                    {
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.GenerationRequest:
                    if (hostState.TryBeginGeneration(message.senderPeerId))
                    {
                        activeGenerationRequest = message.requestId;
                        activeGenerationSize = GeneratedObjectSizes.Normalize(
                            (GeneratedObjectSize)message.intValue);
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
                        var succeeded = !string.IsNullOrWhiteSpace(message.text);
                        hostState.EndGeneration(succeeded);
                        if (succeeded)
                        {
                            generatedObjects.Add(message.text,
                                activeGenerationSize);
                        }
                        BroadcastSnapshot();
                    }
                    break;
                case MessageKind.ObjectTransform:
                    if (message.senderPeerId == hostState.MimePeerId
                        && message.generatedObject != null)
                    {
                        var state = generatedObjects.UpdateHostTransform(
                            message.generatedObject);
                        if (state != null)
                        {
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

        private void RequestSizeSelection(GeneratedObjectSize size)
        {
            if (!CanRecordGeneratorLocally) return;
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.SelectSize,
                intValue = (int)size
            });
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

        public void RequestGeneration(byte[] wav, GeneratedObjectSize size)
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
                requestId = requestId,
                intValue = (int)GeneratedObjectSizes.Normalize(size)
            });
        }

        public void CompleteGeneration(string requestId, string url, string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[MeshUp] Asset generation failed: {error}");
                ReportLocalMessage(error);
            }
            else
            {
                ReportLocalMessage(string.Empty);
            }
            SendCommand(new GameMessage
            {
                kind = (int)MessageKind.GenerationComplete,
                requestId = requestId,
                text = url ?? string.Empty
            });
        }

        public void SubmitObjectTransform(string objectId, Vector3 position,
            Quaternion rotation, Vector3 relativeScale, bool final)
        {
            var state = new MeshupGeneratedObjectState
            {
                objectId = objectId,
                position = position,
                rotation = rotation,
                scale = relativeScale
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

        private void HandleListeningChanged(bool listening)
        {
            guessListening = listening;
            if (listening)
            {
                transientMessage = string.Empty;
            }
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
            var nextSnapshot = hostState.CreateSnapshot(generatedObjects.States);
            ProcessAuthoritativeMessage(MessageKind.Snapshot,
                new GameMessage { snapshot = nextSnapshot });
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

        private void SendGuessFeedback(string target, string transcription)
        {
            var heard = MeshupMatchState.NormalizeGuess(transcription);
            var message = new GameMessage
            {
                kind = (int)MessageKind.GuessFeedback,
                creatorPeerId = session.LocalPeerId,
                targetPeerId = target,
                text = string.IsNullOrWhiteSpace(heard)
                    ? "Incorrect guess — try again"
                    : $"{heard} is an incorrect guess — try again"
            };
            if (target == session.LocalPeerId)
            {
                ProcessAuthoritativeMessage(MessageKind.GuessFeedback, message);
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
            messageChannel?.Send(message);
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
                    ClearRoundGeneration();
                }
            }
            if (changed)
            {
                BroadcastSnapshot();
            }
        }

        private void ClearRoundGeneration()
        {
            generatedObjects.Clear();
            pendingAudio.Clear();
            activeGenerationRequest = string.Empty;
            activeGenerationSize = GeneratedObjectSize.Medium;
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

        private void ToggleGeneratorAudioPreview()
        {
            if (generatorActivityAudio == null)
            {
                return;
            }

            generatorAudioPreview = !generatorAudioPreview;
            if (generatorAudioPreview)
            {
                SetParticleState(true);
                generatorActivityAudio.SetGenerating(true);
            }
            else
            {
                var generationPending = snapshot?.generationPending == true;
                SetParticleState(generationPending);
                generatorActivityAudio.SetGenerating(generationPending);
                if (!generationPending)
                {
                    generatorActivityAudio.PlayCompletion();
                }
            }
        }

        private void PreviewGeneratorFailure()
        {
            if (generatorActivityAudio == null)
            {
                return;
            }

            generatorAudioPreview = false;
            var generationPending = snapshot?.generationPending == true;
            SetParticleState(generationPending);
            generatorActivityAudio.SetGenerating(generationPending);
            if (!generationPending)
            {
                generatorActivityAudio.PlayFailure();
            }
        }

        private void Render()
        {
            sizeSelector?.SetInteractable(CanRecordGeneratorLocally);
            view?.Render(snapshot, session?.LocalPeerId ?? string.Empty,
                privateWordOptions, privateSelectedWord, transientMessage,
                guessFeedback, guessListening);
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
            generatedObjects?.Dispose();
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
                transcriber.ListeningChanged -= HandleListeningChanged;
            }
            if (wallStateCaptured && invisibleWall != null)
            {
                invisibleWall.enabled = originalWallEnabled;
            }
            messageChannel?.Dispose();
        }
    }
}
