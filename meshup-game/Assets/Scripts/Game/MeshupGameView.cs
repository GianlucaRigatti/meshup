using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupGameView : MonoBehaviour
    {
        private Text leaderboard;
        private Text status;
        private Text terminalTitle;
        private Text terminalStatus;
        private Button firstChoice;
        private Button secondChoice;
        private Button startButton;
        private Action<int> chooseWord;
        private Action startRound;

        public void Build(Transform monitor, Transform terminal,
            Action<int> onChooseWord, Action onStartRound)
        {
            chooseWord = onChooseWord;
            startRound = onStartRound;
            BuildMonitor(monitor);
            BuildTerminal(terminal);
        }

        public void Render(MeshupMatchSnapshot snapshot, string localPeerId,
            string[] privateWordOptions, string privateSelectedWord,
            string transientMessage = "")
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
            var mimeName = snapshot.scores.FirstOrDefault(item =>
                item.peerId == snapshot.mimePeerId)?.displayName ?? "Mime";
            status.text = phase switch
            {
                MeshupGamePhase.WaitingForArrival => "Waiting for players…",
                MeshupGamePhase.CallingMime =>
                    $"{mimeName}\nPlease reach the stage",
                MeshupGamePhase.ChoosingWord => $"{mimeName} is choosing a word",
                MeshupGamePhase.Preparation => FormatPuzzle(snapshot, "Get ready"),
                MeshupGamePhase.TimedGuessing => FormatPuzzle(snapshot,
                    $"{snapshot.remainingSeconds / 60:0}:{snapshot.remainingSeconds % 60:00}"),
                MeshupGamePhase.Result =>
                    $"{snapshot.resultMessage}\n\n{snapshot.resultWord}",
                MeshupGamePhase.Finished => "FINAL LEADERBOARD",
                _ => string.Empty
            };

            var isMime = localPeerId == snapshot.mimePeerId;
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

            var preparation = isMime && phase == MeshupGamePhase.Preparation;
            startButton.gameObject.SetActive(preparation);
            startButton.interactable = preparation && !snapshot.generationPending;
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
            terminalStatus.text = preparation
                ? $"{new string('●', snapshot.generationTokens)}"
                    + $"{new string('○', 3 - snapshot.generationTokens)}\n"
                    + (snapshot.generationPending ? "Generating…" : transientMessage)
                : transientMessage;
        }

        private static string FormatPuzzle(MeshupMatchSnapshot snapshot,
            string footer)
        {
            var spaced = string.Join(" ", (snapshot.maskedWord ?? string.Empty)
                .Select(character => character.ToString()));
            return $"{spaced}\n\n{footer}";
        }

        private void BuildMonitor(Transform target)
        {
            var canvas = CreateCanvas("MeshUp Monitor UI", target,
                new Vector2(1200f, 600f), 0.003f);
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
        }

        private void BuildTerminal(Transform target)
        {
            var canvas = CreateCanvas("MeshUp Mime Terminal UI", target,
                new Vector2(800f, 600f), 0.002f);
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
            firstChoice.onClick.AddListener(() => chooseWord?.Invoke(0));
            secondChoice.onClick.AddListener(() => chooseWord?.Invoke(1));

            terminalStatus = CreateText(background.transform, "Status", 38,
                TextAnchor.MiddleCenter, Color.white);
            SetRect(terminalStatus.rectTransform, new Vector2(0.08f, 0.18f),
                new Vector2(0.92f, 0.37f), Vector2.zero, Vector2.zero);
            startButton = CreateButton(background.transform, "Start",
                new Vector2(0.28f, 0.03f), new Vector2(0.72f, 0.18f));
            startButton.GetComponentInChildren<Text>().text = "START";
            startButton.onClick.AddListener(() => startRound?.Invoke());
        }

        private static Canvas CreateCanvas(string name, Transform target,
            Vector2 size, float worldScale)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler),
                typeof(TrackedDeviceGraphicRaycaster));
            gameObject.transform.SetParent(target, false);
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
            var parentScale = target.lossyScale;
            gameObject.transform.localScale = new Vector3(
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
            var rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            var canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            return canvas;
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
            var label = CreateText(panel.transform, "Label", 38,
                TextAnchor.MiddleCenter, Color.white);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(8f, 8f), new Vector2(-8f, -8f));
            return button;
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
