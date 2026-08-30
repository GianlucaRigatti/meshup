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
        private bool cursorReleasedForTerminal;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;
        private readonly System.Collections.Generic.List<ScreenMount>
            screenMounts = new();

        public void Build(Transform monitor, Transform terminal,
            Transform localViewer, Action<int> onChooseWord,
            Action onStartRound)
        {
            chooseWord = onChooseWord;
            startRound = onStartRound;
            BuildMonitor(monitor, localViewer);
            BuildTerminal(terminal, localViewer);
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
            firstChoice.interactable = choicesVisible;
            secondChoice.interactable = choicesVisible;

            var preparation = isMime && phase == MeshupGamePhase.Preparation;
            SetDesktopTerminalCursor(choicesVisible || preparation);
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

        private void BuildMonitor(Transform target, Transform localViewer)
        {
            var canvas = CreateCanvas("MeshUp Monitor UI", target,
                new Vector2(1200f, 600f), 0.003f, localViewer,
                "pPlane1_monter_MTL_0", "pPlane1");
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

        private void BuildTerminal(Transform target, Transform localViewer)
        {
            var canvas = CreateCanvas("MeshUp Mime Terminal UI", target,
                new Vector2(800f, 600f), 0.002f, localViewer,
                "screen_low_Material_0", "screen_low");
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

        private Canvas CreateCanvas(string name, Transform target,
            Vector2 size, float fallbackWorldScale, Transform localViewer,
            params string[] preferredSurfaceNames)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(TrackedDeviceGraphicRaycaster));
            gameObject.transform.SetParent(target, false);
            var surface = FindDisplaySurface(target, preferredSurfaceNames);
            var placement = surface != null
                ? SurfacePlacement.From(surface, localViewer, size)
                : SurfacePlacement.From(target, localViewer,
                    fallbackWorldScale);
            placement.Resolve(out var position, out var rotation);
            gameObject.transform.SetPositionAndRotation(position, rotation);
            var parentScale = target.lossyScale;
            gameObject.transform.localScale = new Vector3(
                placement.WorldScale
                    / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                placement.WorldScale
                    / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                placement.WorldScale
                    / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
            var rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            var canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = localViewer != null
                ? localViewer.GetComponentInChildren<Camera>(true)
                : Camera.main;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            screenMounts.Add(new ScreenMount(gameObject.transform, placement));
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
        }

        private void OnDestroy()
        {
            RestoreDesktopCursor();
        }

        private void LateUpdate()
        {
            foreach (var mount in screenMounts)
            {
                mount.Apply();
            }
        }

        private static Renderer FindDisplaySurface(Transform target,
            string[] preferredNames)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (var preferredName in preferredNames ?? Array.Empty<string>())
            {
                var preferred = renderers.FirstOrDefault(renderer =>
                    string.Equals(renderer.name, preferredName,
                        StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                {
                    return preferred;
                }
            }

            Renderer best = null;
            var bestScore = 0f;
            foreach (var renderer in renderers)
            {
                if (!TryGetLocalBounds(renderer, out var bounds))
                {
                    continue;
                }
                var dimensions = GetWorldDimensions(renderer.transform,
                    bounds.size);
                Array.Sort(dimensions);
                if (dimensions[0] > dimensions[1] * 0.35f)
                {
                    continue;
                }
                var score = dimensions[1] * dimensions[2];
                if (score > bestScore)
                {
                    best = renderer;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool TryGetLocalBounds(Renderer renderer,
            out Bounds bounds)
        {
            if (renderer.TryGetComponent<MeshFilter>(out var filter)
                && filter.sharedMesh != null)
            {
                bounds = filter.sharedMesh.bounds;
                return true;
            }
            if (renderer is SkinnedMeshRenderer skinned
                && skinned.sharedMesh != null)
            {
                bounds = skinned.sharedMesh.bounds;
                return true;
            }
            bounds = default;
            return false;
        }

        private static float[] GetWorldDimensions(Transform transform,
            Vector3 localSize)
        {
            return new[]
            {
                transform.TransformVector(Vector3.right * localSize.x).magnitude,
                transform.TransformVector(Vector3.up * localSize.y).magnitude,
                transform.TransformVector(Vector3.forward * localSize.z).magnitude
            };
        }

        private readonly struct SurfacePlacement
        {
            public readonly Transform Surface;
            public readonly Transform Viewer;
            public readonly Vector3 LocalCenter;
            public readonly Vector3 LocalNormal;
            public readonly Vector3 LocalUp;
            public readonly float Offset;
            public readonly float WorldScale;

            private SurfacePlacement(Transform surface, Transform viewer,
                Vector3 localCenter, Vector3 localNormal, Vector3 localUp,
                float offset, float worldScale)
            {
                Surface = surface;
                Viewer = viewer;
                LocalCenter = localCenter;
                LocalNormal = localNormal;
                LocalUp = localUp;
                Offset = offset;
                WorldScale = worldScale;
            }

            public static SurfacePlacement From(Renderer renderer,
                Transform viewer, Vector2 canvasSize)
            {
                TryGetLocalBounds(renderer, out var bounds);
                var localSize = bounds.size;
                var dimensions = GetWorldDimensions(renderer.transform,
                    localSize);
                var normalIndex = SmallestIndex(dimensions);
                var remaining = Enumerable.Range(0, 3)
                    .Where(index => index != normalIndex).ToArray();
                var firstAxis = Axis(remaining[0]);
                var secondAxis = Axis(remaining[1]);
                var firstUp = Mathf.Abs(Vector3.Dot(
                    renderer.transform.TransformDirection(firstAxis).normalized,
                    Vector3.up));
                var secondUp = Mathf.Abs(Vector3.Dot(
                    renderer.transform.TransformDirection(secondAxis).normalized,
                    Vector3.up));
                var upIndex = firstUp >= secondUp ? remaining[0] : remaining[1];
                var widthIndex = upIndex == remaining[0]
                    ? remaining[1]
                    : remaining[0];
                var localUp = Axis(upIndex);
                if (Vector3.Dot(renderer.transform.TransformDirection(localUp),
                    Vector3.up) < 0f)
                {
                    localUp = -localUp;
                }
                var fittedScale = Mathf.Min(
                    dimensions[widthIndex] * 0.9f / canvasSize.x,
                    dimensions[upIndex] * 0.9f / canvasSize.y);
                var faceClearance = Mathf.Max(0.003f,
                    Mathf.Min(dimensions[widthIndex], dimensions[upIndex])
                    * 0.0125f);
                var offset = dimensions[normalIndex] * 0.5f + faceClearance;
                return new SurfacePlacement(renderer.transform, viewer,
                    bounds.center, Axis(normalIndex), localUp, offset,
                    Mathf.Max(0.0001f, fittedScale));
            }

            public static SurfacePlacement From(Transform target,
                Transform viewer, float worldScale)
            {
                return new SurfacePlacement(target, viewer, Vector3.zero,
                    Vector3.forward, Vector3.up, 0.01f, worldScale);
            }

            public void Resolve(out Vector3 position, out Quaternion rotation)
            {
                var center = Surface.TransformPoint(LocalCenter);
                var normal = Surface.TransformDirection(LocalNormal).normalized;
                var up = Surface.TransformDirection(LocalUp).normalized;
                var viewerDirection = Viewer != null
                    ? Viewer.position - center
                    : -normal;
                var towardViewer = Vector3.Dot(viewerDirection, normal) >= 0f
                    ? normal
                    : -normal;
                position = center + towardViewer * Offset;
                rotation = Quaternion.LookRotation(-towardViewer, up);
            }

            private static int SmallestIndex(float[] values)
            {
                return values[0] <= values[1] && values[0] <= values[2]
                    ? 0
                    : values[1] <= values[2] ? 1 : 2;
            }

            private static Vector3 Axis(int index)
            {
                return index switch
                {
                    0 => Vector3.right,
                    1 => Vector3.up,
                    _ => Vector3.forward
                };
            }
        }

        private sealed class ScreenMount
        {
            private readonly Transform canvas;
            private readonly SurfacePlacement placement;

            public ScreenMount(Transform canvasTransform,
                SurfacePlacement value)
            {
                canvas = canvasTransform;
                placement = value;
                Apply();
            }

            public void Apply()
            {
                if (canvas == null || placement.Surface == null)
                {
                    return;
                }
                placement.Resolve(out var position, out var rotation);
                canvas.SetPositionAndRotation(position, rotation);
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
