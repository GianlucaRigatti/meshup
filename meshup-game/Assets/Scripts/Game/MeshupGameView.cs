using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupGameView : MonoBehaviour
    {
        private Text leaderboard;
        private Text status;
        private Text listeningIndicator;
        private Text terminalTitle;
        private Text terminalStatus;
        private Button firstChoice;
        private Button secondChoice;
        private Button startButton;
        private Action<int> chooseWord;
        private Action startRound;
        private int lastChoiceInteractionFrame = -1;
        private int lastStartInteractionFrame = -1;
        private bool cursorReleasedForTerminal;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;
        private readonly Dictionary<GraphicRaycaster, bool>
            desktopOverlayRaycasterStates = new();
        private Transform monitorCanvas;
        private Transform monitorFrontMount;
        private Transform monitorBackMount;
        private Transform localViewer;

        public void Build(Transform monitorFront, Transform monitorBack,
            Transform terminal, Transform viewer, Action<int> onChooseWord,
            Action onStartRound)
        {
            chooseWord = onChooseWord;
            startRound = onStartRound;
            monitorFrontMount = monitorFront;
            monitorBackMount = monitorBack;
            localViewer = viewer;
            BuildMonitor(monitorFront, viewer);
            BuildTerminal(terminal, viewer);
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
                firstChoice.GetComponentInChildren<Text>().text = privateWordOptions[0];
                secondChoice.GetComponentInChildren<Text>().text = privateWordOptions[1];
            }
            SetInteractable(firstChoice, choicesVisible);
            SetInteractable(secondChoice, choicesVisible);

            var preparation = isMime && phase == MeshupGamePhase.Preparation;
            SetDesktopTerminalCursor(choicesVisible || preparation);
            startButton.gameObject.SetActive(preparation);
            SetInteractable(startButton,
                preparation && !snapshot.generationPending);
            startButton.GetComponentInChildren<Text>().text =
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

        private void BuildMonitor(Transform target, Transform localViewer)
        {
            var canvas = CreateCanvas("MeshUp Monitor UI", target,
                new Vector2(1200f, 600f), localViewer);
            monitorCanvas = canvas.transform;
            var background = CreatePanel(canvas.transform, "Background",
                new Color(0.015f, 0.04f, 0.07f, 0.94f));
            var leaderboardPanel = CreatePanel(background.transform,
                "Leaderboard", new Color(0.02f, 0.12f, 0.17f, 0.95f));
            SetRect(leaderboardPanel.rectTransform, new Vector2(0f, 0f),
                new Vector2(0.34f, 1f), Vector2.zero, Vector2.zero);
            leaderboard = CreateText(leaderboardPanel.transform, "Scores", 34,
                TextAnchor.UpperLeft, Color.white);
            SetRect(leaderboard.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(24f, 24f), new Vector2(-24f, -24f));

            status = CreateText(background.transform, "Game Status", 52,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.95f, 1f));
            SetRect(status.rectTransform, new Vector2(0.34f, 0f), Vector2.one,
                new Vector2(30f, 30f), new Vector2(-30f, -30f));

            listeningIndicator = CreateText(background.transform,
                "Listening Indicator", 30, TextAnchor.UpperRight,
                new Color(1f, 0.12f, 0.12f));
            listeningIndicator.text = "●  LISTENING";
            SetRect(listeningIndicator.rectTransform,
                new Vector2(0.72f, 0.86f), new Vector2(0.98f, 0.98f),
                Vector2.zero, Vector2.zero);
            listeningIndicator.gameObject.SetActive(false);
        }

        private void BuildTerminal(Transform target, Transform localViewer)
        {
            var canvas = CreateCanvas("MeshUp Mime Terminal UI", target,
                new Vector2(800f, 600f), localViewer);
            var background = CreatePanel(canvas.transform, "Background",
                new Color(0.02f, 0.04f, 0.08f, 0.96f));
            terminalTitle = CreateText(background.transform, "Title", 52,
                TextAnchor.MiddleCenter, new Color(0.4f, 1f, 0.95f));
            SetRect(terminalTitle.rectTransform, new Vector2(0.05f, 0.68f),
                new Vector2(0.95f, 0.96f), Vector2.zero, Vector2.zero);

            firstChoice = CreateButton(background.transform, "First Choice",
                new Vector2(0.08f, 0.38f), new Vector2(0.47f, 0.65f));
            secondChoice = CreateButton(background.transform, "Second Choice",
                new Vector2(0.53f, 0.38f), new Vector2(0.92f, 0.65f));
            firstChoice.onClick.AddListener(() => SelectWord(0));
            secondChoice.onClick.AddListener(() => SelectWord(1));

            terminalStatus = CreateText(background.transform, "Status", 38,
                TextAnchor.MiddleCenter, Color.white);
            SetRect(terminalStatus.rectTransform, new Vector2(0.08f, 0.18f),
                new Vector2(0.92f, 0.37f), Vector2.zero, Vector2.zero);
            startButton = CreateButton(background.transform, "Start",
                new Vector2(0.28f, 0.03f), new Vector2(0.72f, 0.18f));
            startButton.GetComponentInChildren<Text>().text = "START";
            startButton.onClick.AddListener(StartRound);
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

        private static Canvas CreateCanvas(string name, Transform mount,
            Vector2 size, Transform localViewer)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(TrackedDeviceGraphicRaycaster));
            gameObject.transform.SetParent(mount, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            var canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = localViewer != null
                ? localViewer.GetComponentInChildren<Camera>(true)
                : Camera.main;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            return canvas;
        }

        private void SetDesktopTerminalCursor(bool terminalInteractionActive)
        {
            if (Application.isMobilePlatform)
            {
                return;
            }

            if (terminalInteractionActive && !cursorReleasedForTerminal)
            {
                previousCursorLockMode = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                cursorReleasedForTerminal = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (terminalInteractionActive)
            {
                SuppressDesktopOverlayRaycasters();
            }
            else if (!terminalInteractionActive && cursorReleasedForTerminal)
            {
                RestoreDesktopCursor();
            }
        }

        private void RestoreDesktopCursor()
        {
            if (!cursorReleasedForTerminal)
            {
                return;
            }
            cursorReleasedForTerminal = false;
            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
            RestoreDesktopOverlayRaycasters();
        }

        private void SuppressDesktopOverlayRaycasters()
        {
            foreach (var raycaster in FindObjectsByType<GraphicRaycaster>(
                FindObjectsInactive.Include))
            {
                var canvas = raycaster.GetComponent<Canvas>();
                if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    || raycaster.GetComponent<GameSessionMenu>() != null)
                {
                    continue;
                }
                if (!desktopOverlayRaycasterStates.ContainsKey(raycaster))
                {
                    desktopOverlayRaycasterStates.Add(raycaster,
                        raycaster.enabled);
                }
                raycaster.enabled = false;
            }
        }

        private void RestoreDesktopOverlayRaycasters()
        {
            foreach (var item in desktopOverlayRaycasterStates)
            {
                if (item.Key != null)
                {
                    item.Key.enabled = item.Value;
                }
            }
            desktopOverlayRaycasterStates.Clear();
        }

        private void OnDestroy()
        {
            RestoreDesktopCursor();
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

        private static Image CreatePanel(Transform parent, string name,
            Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            SetRect(image.rectTransform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            return image;
        }

        private static Text CreateText(Transform parent, string name,
            int fontSize, TextAnchor alignment, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var panel = CreatePanel(parent, name,
                new Color(0.06f, 0.38f, 0.48f, 1f));
            panel.raycastTarget = true;
            SetRect(panel.rectTransform, anchorMin, anchorMax,
                Vector2.zero, Vector2.zero);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            var collider = panel.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(panel.rectTransform.rect.width,
                panel.rectTransform.rect.height, 8f);
            var xrInteractable = panel.gameObject.AddComponent<
                XRSimpleInteractable>();
            xrInteractable.selectEntered.AddListener(_ =>
            {
                if (button.isActiveAndEnabled && button.interactable)
                {
                    button.onClick.Invoke();
                }
            });
            var label = CreateText(panel.transform, "Label", 38,
                TextAnchor.MiddleCenter, Color.white);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(8f, 8f), new Vector2(-8f, -8f));
            return button;
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

        private static void SetRect(RectTransform rect, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
