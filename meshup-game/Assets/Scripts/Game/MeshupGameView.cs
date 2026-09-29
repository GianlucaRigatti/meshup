using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupGameView : MonoBehaviour
    {
        [SerializeField] private GameInteractionState interactionState;
        [SerializeField] private Text leaderboard;
        [SerializeField] private Text status;
        [SerializeField] private Text listeningIndicator;
        [SerializeField] private Transform monitorCanvas;
        [SerializeField] private Canvas terminalCanvas;
        [SerializeField] private Text terminalTitle;
        [SerializeField] private Text terminalStatus;
        [SerializeField] private Button firstChoice;
        [SerializeField] private Button secondChoice;
        [SerializeField] private Button startButton;
        [SerializeField] private Text firstChoiceLabel;
        [SerializeField] private Text secondChoiceLabel;
        [SerializeField] private Text startButtonLabel;

        private Action<int> chooseWord;
        private Action startRound;
        private int lastChoiceInteractionFrame = -1;
        private int lastStartInteractionFrame = -1;
        private Transform monitorFrontMount;
        private Transform monitorBackMount;
        private Transform localViewer;

        public void Configure(Transform monitorFront, Transform monitorBack,
            Transform viewer, Action<int> onChooseWord, Action onStartRound)
        {
            chooseWord = onChooseWord;
            startRound = onStartRound;
            monitorFrontMount = monitorFront;
            monitorBackMount = monitorBack;
            localViewer = viewer;
            var camera = viewer != null
                ? viewer.GetComponentInChildren<Camera>(true) : Camera.main;
            monitorCanvas.GetComponent<Canvas>().worldCamera = camera;
            terminalCanvas.worldCamera = camera;
            BindButton(firstChoice, SelectFirstWord);
            BindButton(secondChoice, SelectSecondWord);
            BindButton(startButton, StartRound);
        }

        public void Render(MeshupMatchSnapshot snapshot, string localPeerId,
            string[] privateWordOptions, string privateSelectedWord,
            string transientMessage = "", string guessFeedback = "",
            bool isListening = false)
        {
            if (snapshot == null || leaderboard == null || status == null)
            {
                return;
            }

            leaderboard.text = "LEADERBOARD\n\n" + string.Join("\n",
                snapshot.scores.Select((score, index) =>
                    $"{index + 1}. {score.displayName}  {score.points}"
                    + (score.connected ? "" : "  (left)")));

            var phase = (MeshupGamePhase)snapshot.phase;
            var isMime = localPeerId == snapshot.mimePeerId;
            var mimeName = snapshot.scores.FirstOrDefault(item =>
                item.peerId == snapshot.mimePeerId)?.displayName ?? "Mime";
            status.text = phase switch
            {
                MeshupGamePhase.WaitingForArrival => "Waiting for players…",
                MeshupGamePhase.CallingMime =>
                    $"{mimeName}\nPlease reach the stage",
                MeshupGamePhase.ChoosingWord => $"{mimeName} is choosing a word",
                MeshupGamePhase.Preparation => isMime
                    ? FormatPuzzle(snapshot, "Get ready")
                    : "generating objects...",
                MeshupGamePhase.TimedGuessing => FormatPuzzle(snapshot,
                    $"{snapshot.remainingSeconds / 60:0}:{snapshot.remainingSeconds % 60:00}"),
                MeshupGamePhase.Result =>
                    $"{snapshot.resultMessage}\n\n{snapshot.resultWord}",
                MeshupGamePhase.Finished => FormatWinner(snapshot),
                _ => string.Empty
            };
            if (!isMime && phase == MeshupGamePhase.TimedGuessing)
            {
                var feedback = !string.IsNullOrWhiteSpace(guessFeedback)
                    ? guessFeedback
                    : transientMessage;
                if (!string.IsNullOrWhiteSpace(feedback))
                {
                    status.text += $"\n\n{feedback}";
                }
            }
            listeningIndicator.gameObject.SetActive(!isMime
                && phase == MeshupGamePhase.TimedGuessing && isListening);

            var choicesVisible = isMime
                && phase == MeshupGamePhase.ChoosingWord
                && privateWordOptions?.Length == 2;
            firstChoice.gameObject.SetActive(choicesVisible);
            secondChoice.gameObject.SetActive(choicesVisible);
            if (choicesVisible)
            {
                firstChoiceLabel.text = privateWordOptions[0];
                secondChoiceLabel.text = privateWordOptions[1];
            }
            SetInteractable(firstChoice, choicesVisible);
            SetInteractable(secondChoice, choicesVisible);

            var preparation = isMime && phase == MeshupGamePhase.Preparation;
            interactionState.SetTerminalActive(choicesVisible || preparation);
            startButton.gameObject.SetActive(preparation);
            SetInteractable(startButton,
                preparation && !snapshot.generationPending);
            startButtonLabel.text =
                snapshot.generationPending ? "GENERATING…" : "START";
            terminalTitle.text = isMime
                ? phase switch
                {
                    MeshupGamePhase.ChoosingWord => "CHOOSE YOUR WORD",
                    MeshupGamePhase.Preparation or MeshupGamePhase.TimedGuessing
                        => privateSelectedWord,
                    MeshupGamePhase.Result => "RETURN TO THE GROUP",
                    _ => "WAIT FOR YOUR TURN"
                }
                : "MIME ONLY";
            terminalStatus.text = isMime
                && phase == MeshupGamePhase.ChoosingWord && !choicesVisible
                    ? "Receiving word choices…"
                    : preparation
                        ? $"{new string('●', snapshot.generationTokens)}"
                            + $"{new string('○', 3 - snapshot.generationTokens)}\n"
                            + (snapshot.generationPending
                                ? "Generating…"
                                : transientMessage)
                        : transientMessage;
        }

        private static string FormatPuzzle(MeshupMatchSnapshot snapshot,
            string footer)
        {
            var spaced = string.Join(" ", (snapshot.maskedWord ?? string.Empty)
                .Select(character => character.ToString()));
            return $"{spaced}\n\n{footer}";
        }

        private static string FormatWinner(MeshupMatchSnapshot snapshot)
        {
            var winner = snapshot.scores.FirstOrDefault();
            return winner == null
                ? "FINAL LEADERBOARD"
                : $"FINAL LEADERBOARD\n\n{winner.displayName} won";
        }

        private void SelectFirstWord() => SelectWord(0);
        private void SelectSecondWord() => SelectWord(1);

        private void BindButton(Button button, UnityAction onClick)
        {
            button.onClick.AddListener(onClick);
            button.GetComponent<XRSimpleInteractable>().selectEntered.AddListener(ForwardXrClick);
        }

        private void UnbindButton(Button button, UnityAction onClick)
        {
            if (button == null) return;
            button.onClick.RemoveListener(onClick);
            button.GetComponent<XRSimpleInteractable>().selectEntered.RemoveListener(ForwardXrClick);
        }

        private void ForwardXrClick(SelectEnterEventArgs args)
        {
            var button = args.interactableObject.transform.GetComponent<Button>();
            if (button.isActiveAndEnabled && button.interactable)
            {
                button.onClick.Invoke();
            }
        }

        private void SelectWord(int index)
        {
            if (lastChoiceInteractionFrame == Time.frameCount)
            {
                return;
            }
            lastChoiceInteractionFrame = Time.frameCount;
            chooseWord?.Invoke(index);
        }

        private void StartRound()
        {
            if (lastStartInteractionFrame == Time.frameCount)
            {
                return;
            }
            lastStartInteractionFrame = Time.frameCount;
            startRound?.Invoke();
        }

        private void OnDisable() => interactionState?.SetTerminalActive(false);

        private void OnDestroy()
        {
            UnbindButton(firstChoice, SelectFirstWord);
            UnbindButton(secondChoice, SelectSecondWord);
            UnbindButton(startButton, StartRound);
        }

        private void LateUpdate()
        {
            if (monitorCanvas == null || monitorFrontMount == null
                || monitorBackMount == null || localViewer == null)
            {
                return;
            }
            var frontDistance = (localViewer.position - monitorFrontMount.position)
                .sqrMagnitude;
            var backDistance = (localViewer.position - monitorBackMount.position)
                .sqrMagnitude;
            var target = frontDistance <= backDistance
                ? monitorFrontMount : monitorBackMount;
            if (monitorCanvas.parent != target)
            {
                monitorCanvas.SetParent(target, false);
                monitorCanvas.localPosition = Vector3.zero;
                monitorCanvas.localRotation = Quaternion.identity;
                monitorCanvas.localScale = Vector3.one;
            }
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            button.interactable = interactable;
            var xrInteractable = button.GetComponent<XRSimpleInteractable>();
            if (xrInteractable != null)
            {
                xrInteractable.enabled = interactable;
            }
        }

    }
}
