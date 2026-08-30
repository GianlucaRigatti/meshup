using System;
using System.Collections;
using System.Collections.Generic;
using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meshup.Lobby
{
    /// <summary>
    /// Presents a stationary, comfort-safe portal around the local viewpoint.
    /// The same animation is used for desktop and XR; head/look tracking remains
    /// active while locomotion and room UI input are temporarily suspended.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyPortalTransition : MonoBehaviour
    {
        [Header("Portal Visuals")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform cyanRing;
        [SerializeField] private Transform goldRing;
        [SerializeField] private ParticleSystem[] particles;
        [SerializeField] private Light portalLight;
        [SerializeField] private Material cyanMaterial;
        [SerializeField] private Material goldMaterial;
        [SerializeField] private Material veilMaterial;
        [SerializeField] private Canvas overlayCanvas;
        [SerializeField] private CanvasGroup overlayGroup;
        [SerializeField] private Image overlayImage;

        [Header("Input")]
        [SerializeField] private GameObject traditionalController;
        [SerializeField] private Behaviour[] locomotionBehaviours;
        [SerializeField] private CanvasGroup roomUiCanvasGroup;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] private float materializeDuration = 0.4f;
        [SerializeField, Min(0.05f)] private float engulfDuration = 0.45f;
        [SerializeField, Min(0.05f)] private float cancelDuration = 0.3f;
        [SerializeField, Min(0.05f)] private float revealDuration = 0.35f;
        [SerializeField, Min(0.25f)] private float portalDistance = 1.45f;

        private readonly Dictionary<Behaviour, bool> locomotionStates = new();
        private UbiqRoomSession session;
        private Coroutine visualSequence;
        private Action pendingCompletion;
        private bool inputLocked;
        private bool traditionalControllerWasActive;
        private bool roomUiWasInteractable;
        private bool roomUiBlockedRaycasts;
        private bool enteringGame;
        private float cyanAngle;
        private float goldAngle;

        private static readonly Color ComfortFadeColor =
            new(0.004f, 0.008f, 0.016f, 1f);

        public bool IsVisible => visualRoot != null && visualRoot.gameObject.activeSelf;
        public bool InputLocked => inputLocked;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            BuildPortalVisuals();
            if (overlayCanvas != null)
            {
                overlayCanvas.transform.localScale = Vector3.one;
            }
            if (overlayImage != null)
            {
                // A near-black cover is comfortable in a headset and avoids the
                // several-second cyan flash when scene preloading stalls.
                overlayImage.color = ComfortFadeColor;
            }
            SetOverlayAlpha(0f);
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryBindSession();
        }

        private void Update()
        {
            if (session == null)
            {
                TryBindSession();
            }

            if (!IsVisible)
            {
                return;
            }

            cyanAngle += 34f * Time.unscaledDeltaTime;
            goldAngle -= 48f * Time.unscaledDeltaTime;
            if (cyanRing != null)
            {
                cyanRing.localRotation = Quaternion.Euler(0f, 0f, cyanAngle);
            }
            if (goldRing != null)
            {
                goldRing.localRotation = Quaternion.Euler(0f, 0f, goldAngle);
            }

        }

        private void TryBindSession()
        {
            var availableSession = UbiqRoomSession.Instance;
            if (availableSession == null || availableSession == session)
            {
                return;
            }

            UnbindSession();
            session = availableSession;
            session.StateChanged += HandleStateChanged;
            session.GameSceneTransitionRequested += HandleTransitionRequested;
            HandleStateChanged(session.State);
        }

        private void UnbindSession()
        {
            if (session == null)
            {
                return;
            }

            session.StateChanged -= HandleStateChanged;
            session.GameSceneTransitionRequested -= HandleTransitionRequested;
            session = null;
        }

        private void HandleStateChanged(RoomSessionState state)
        {
            if (state == RoomSessionState.Joining)
            {
                BeginMaterialize();
                return;
            }

            if (state is RoomSessionState.Publishing
                or RoomSessionState.LoadingGame
                or RoomSessionState.InGame)
            {
                return;
            }

            if (IsVisible || inputLocked)
            {
                BeginCancel();
            }
        }

        private void HandleTransitionRequested(Action completeTransition)
        {
            if (completeTransition == null)
            {
                return;
            }

            if (pendingCompletion != null || enteringGame)
            {
                completeTransition();
                return;
            }

            pendingCompletion = completeTransition;
            enteringGame = true;
            StopVisualSequence();
            visualSequence = StartCoroutine(Engulf());
        }

        private void BeginMaterialize()
        {
            if (enteringGame || IsVisible)
            {
                return;
            }

            SetInputLocked(true);
            PositionPortal();
            visualRoot.gameObject.SetActive(true);
            visualRoot.localScale = Vector3.one * 0.04f;
            SetOverlayAlpha(0f);
            SetParticlesPlaying(true);
            StopVisualSequence();
            visualSequence = StartCoroutine(Materialize());
        }

        private IEnumerator Materialize()
        {
            var elapsed = 0f;
            while (elapsed < materializeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / materializeDuration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                visualRoot.localScale = Vector3.one * Mathf.Lerp(0.04f, 1f, eased);
                yield return null;
            }

            visualRoot.localScale = Vector3.one;
            visualSequence = null;
        }

        private IEnumerator Engulf()
        {
            try
            {
                if (!IsVisible)
                {
                    SetInputLocked(true);
                    PositionPortal();
                    visualRoot.gameObject.SetActive(true);
                    visualRoot.localScale = Vector3.one * 0.2f;
                    SetParticlesPlaying(true);
                }

                var initialScale = visualRoot.localScale.x;
                var initialOverlay = overlayGroup != null ? overlayGroup.alpha : 0f;
                var elapsed = 0f;
                while (elapsed < engulfDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / engulfDuration);
                    var eased = t * t * (3f - 2f * t);
                    // Keep geometry outside the headset instead of expanding it
                    // through the near plane, which caused severe fill-rate spikes.
                    visualRoot.localScale = Vector3.one
                        * Mathf.Lerp(initialScale, 1.45f, eased);
                    SetOverlayAlpha(Mathf.Lerp(initialOverlay, 1f,
                        Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01((t - 0.08f) / 0.82f))));
                    yield return null;
                }

                SetOverlayAlpha(1f);
                visualRoot.gameObject.SetActive(false);
            }
            finally
            {
                // Scene activation must never remain gated if a visual effect
                // fails or this coroutine is stopped unexpectedly.
                var completion = pendingCompletion;
                pendingCompletion = null;
                completion?.Invoke();
                visualSequence = null;
            }
        }

        private void BeginCancel()
        {
            if (enteringGame)
            {
                return;
            }

            StopVisualSequence();
            visualSequence = StartCoroutine(CancelPortal());
        }

        private IEnumerator CancelPortal()
        {
            var startScale = IsVisible ? visualRoot.localScale.x : 0f;
            var elapsed = 0f;
            while (elapsed < cancelDuration && IsVisible)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / cancelDuration);
                visualRoot.localScale = Vector3.one
                    * Mathf.Lerp(startScale, 0.04f, t);
                yield return null;
            }

            SetParticlesPlaying(false);
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
                visualRoot.localScale = Vector3.one;
            }
            SetOverlayAlpha(0f);
            SetInputLocked(false);
            visualSequence = null;
        }

        private void PositionPortal()
        {
            var camera = Camera.main;
            if (camera == null || visualRoot == null)
            {
                return;
            }

            var forward = Vector3.ProjectOnPlane(camera.transform.forward,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f)
            {
                forward = camera.transform.forward;
            }
            visualRoot.position = camera.transform.position + forward * portalDistance;
            visualRoot.rotation = Quaternion.LookRotation(
                camera.transform.position - visualRoot.position, Vector3.up);
            if (overlayCanvas != null)
            {
                overlayCanvas.worldCamera = camera;
            }
        }

        private void BuildPortalVisuals()
        {
            if (visualRoot == null)
            {
                visualRoot = NewChild("Portal Visuals", transform);
            }
            if (cyanRing != null && goldRing != null)
            {
                return;
            }

            // One veil and two LineRenderers replace the previous 52 cube renderers,
            // two particle systems and realtime point light. This is substantially
            // cheaper for both desktop and stereo rendering.
            var veil = GameObject.CreatePrimitive(PrimitiveType.Quad);
            veil.name = "Portal Veil";
            veil.transform.SetParent(visualRoot, false);
            veil.transform.localScale = Vector3.one * 1.15f;
            veil.GetComponent<Renderer>().sharedMaterial = veilMaterial;
            Destroy(veil.GetComponent<Collider>());

            cyanRing = CreateRing("Cyan Rune Ring", visualRoot,
                0.72f, 36, cyanMaterial, 0.036f);
            goldRing = CreateRing("Gold Rune Ring", visualRoot,
                0.52f, 28, goldMaterial, 0.025f);
            goldRing.localRotation = Quaternion.Euler(0f, 0f, 8f);

            particles = Array.Empty<ParticleSystem>();
            portalLight = null;
        }

        private static Transform CreateRing(string name, Transform parent,
            float radius, int pointCount, Material material, float thickness)
        {
            var ring = NewChild(name, parent);
            var line = ring.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = pointCount;
            line.startWidth = thickness;
            line.endWidth = thickness;
            line.numCornerVertices = 1;
            line.numCapVertices = 0;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            for (var i = 0; i < pointCount; i++)
            {
                var angle = i * Mathf.PI * 2f / pointCount;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }
            return ring;
        }

        private static Transform NewChild(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void SetInputLocked(bool locked)
        {
            if (locked == inputLocked)
            {
                return;
            }

            inputLocked = locked;
            if (locked)
            {
                traditionalControllerWasActive = traditionalController != null
                    && traditionalController.activeSelf;
                traditionalController?.SetActive(false);

                locomotionStates.Clear();
                foreach (var behaviour in locomotionBehaviours ?? Array.Empty<Behaviour>())
                {
                    if (behaviour == null || locomotionStates.ContainsKey(behaviour))
                    {
                        continue;
                    }
                    locomotionStates.Add(behaviour, behaviour.enabled);
                    behaviour.enabled = false;
                }

                if (roomUiCanvasGroup != null)
                {
                    roomUiWasInteractable = roomUiCanvasGroup.interactable;
                    roomUiBlockedRaycasts = roomUiCanvasGroup.blocksRaycasts;
                    roomUiCanvasGroup.interactable = false;
                    roomUiCanvasGroup.blocksRaycasts = false;
                }
                return;
            }

            if (traditionalController != null)
            {
                traditionalController.SetActive(traditionalControllerWasActive);
            }
            foreach (var pair in locomotionStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                }
            }
            locomotionStates.Clear();
            if (roomUiCanvasGroup != null)
            {
                roomUiCanvasGroup.interactable = roomUiWasInteractable;
                roomUiCanvasGroup.blocksRaycasts = roomUiBlockedRaycasts;
            }
        }

        private void SetParticlesPlaying(bool playing)
        {
            foreach (var particleSystem in particles ?? Array.Empty<ParticleSystem>())
            {
                if (particleSystem == null)
                {
                    continue;
                }
                if (playing)
                {
                    particleSystem.Play(true);
                }
                else
                {
                    particleSystem.Stop(true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void SetOverlayAlpha(float alpha)
        {
            if (overlayGroup != null)
            {
                overlayGroup.alpha = Mathf.Clamp01(alpha);
                overlayGroup.blocksRaycasts = alpha > 0.01f;
            }
            if (overlayImage != null)
            {
                overlayImage.enabled = alpha > 0.001f;
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!enteringGame)
            {
                return;
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.transform.localScale = Vector3.one;
                overlayCanvas.worldCamera = Camera.main;
            }
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
            }
            StopVisualSequence();
            visualSequence = StartCoroutine(RevealGame());
        }

        private IEnumerator RevealGame()
        {
            var elapsed = 0f;
            while (elapsed < revealDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetOverlayAlpha(1f - Mathf.Clamp01(elapsed / revealDuration));
                yield return null;
            }

            SetOverlayAlpha(0f);
            Destroy(gameObject);
        }

        private void StopVisualSequence()
        {
            if (visualSequence == null)
            {
                return;
            }
            StopCoroutine(visualSequence);
            visualSequence = null;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnbindSession();
        }

        private void OnDestroy()
        {
            SetInputLocked(false);
            var completion = pendingCompletion;
            pendingCompletion = null;
            completion?.Invoke();
        }
    }
}
