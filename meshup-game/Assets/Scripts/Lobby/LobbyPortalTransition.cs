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
        [SerializeField, Min(0.05f)] private float materializeDuration = 0.65f;
        [SerializeField, Min(0.05f)] private float engulfDuration = 1.1f;
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

        public bool IsVisible => visualRoot != null && visualRoot.gameObject.activeSelf;
        public bool InputLocked => inputLocked;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            BuildPortalVisuals();
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

            if (!enteringGame && portalLight != null)
            {
                portalLight.intensity = 1.2f
                    + Mathf.Sin(Time.unscaledTime * 3.4f) * 0.18f;
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
            if (portalLight != null)
            {
                portalLight.enabled = true;
                portalLight.intensity = 0f;
            }
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
                if (portalLight != null)
                {
                    portalLight.intensity = Mathf.Lerp(0f, 1.2f, eased);
                }
                yield return null;
            }

            visualRoot.localScale = Vector3.one;
            visualSequence = null;
        }

        private IEnumerator Engulf()
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
                visualRoot.localScale = Vector3.one
                    * Mathf.Lerp(initialScale, 8.5f, eased);
                SetOverlayAlpha(Mathf.Lerp(initialOverlay, 1f,
                    Mathf.Clamp01((t - 0.34f) / 0.66f)));
                if (portalLight != null)
                {
                    portalLight.intensity = Mathf.Lerp(1.2f, 4.5f, eased);
                }
                yield return null;
            }

            SetOverlayAlpha(1f);
            visualRoot.gameObject.SetActive(false);
            var completion = pendingCompletion;
            pendingCompletion = null;
            completion?.Invoke();
            visualSequence = null;
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
            var startLight = portalLight != null ? portalLight.intensity : 0f;
            var elapsed = 0f;
            while (elapsed < cancelDuration && IsVisible)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / cancelDuration);
                visualRoot.localScale = Vector3.one
                    * Mathf.Lerp(startScale, 0.04f, t);
                if (portalLight != null)
                {
                    portalLight.intensity = Mathf.Lerp(startLight, 0f, t);
                }
                yield return null;
            }

            SetParticlesPlaying(false);
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
                visualRoot.localScale = Vector3.one;
            }
            if (portalLight != null)
            {
                portalLight.enabled = false;
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
            if (cyanRing != null && goldRing != null && particles != null
                && particles.Length >= 2 && portalLight != null)
            {
                return;
            }

            var veil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            veil.name = "Portal Veil";
            veil.transform.SetParent(visualRoot, false);
            veil.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            veil.transform.localScale = new Vector3(0.62f, 0.008f, 0.62f);
            veil.GetComponent<Renderer>().sharedMaterial = veilMaterial;
            Destroy(veil.GetComponent<Collider>());

            cyanRing = CreateSegmentedRing("Cyan Rune Ring", visualRoot,
                0.72f, 30, cyanMaterial, 0.16f, 0.036f);
            goldRing = CreateSegmentedRing("Gold Rune Ring", visualRoot,
                0.52f, 22, goldMaterial, 0.13f, 0.028f);
            goldRing.localRotation = Quaternion.Euler(0f, 0f, 8f);

            particles = new[]
            {
                CreateParticles("Cyan Story Sparks", visualRoot, cyanMaterial,
                    new Color(0.3f, 0.95f, 1f), 54, 0.76f, 0.028f),
                CreateParticles("Gold Story Sparks", visualRoot, goldMaterial,
                    new Color(1f, 0.72f, 0.24f), 32, 0.56f, 0.022f)
            };

            var lightObject = NewChild("Portal Light", visualRoot).gameObject;
            lightObject.transform.localPosition = new Vector3(0f, 0f, -0.18f);
            portalLight = lightObject.AddComponent<Light>();
            portalLight.type = LightType.Point;
            portalLight.color = new Color(0.24f, 0.9f, 1f);
            portalLight.range = 4.5f;
            portalLight.intensity = 0f;
            portalLight.shadows = LightShadows.None;
        }

        private static Transform CreateSegmentedRing(string name, Transform parent,
            float radius, int segmentCount, Material material, float length,
            float thickness)
        {
            var ring = NewChild(name, parent);
            for (var i = 0; i < segmentCount; i++)
            {
                var angle = i * Mathf.PI * 2f / segmentCount;
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"Rune {i + 1:00}";
                segment.transform.SetParent(ring, false);
                segment.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                segment.transform.localRotation = Quaternion.Euler(
                    0f, 0f, angle * Mathf.Rad2Deg + 90f);
                var alternatingLength = i % 3 == 0 ? length * 1.35f : length;
                segment.transform.localScale = new Vector3(
                    alternatingLength, thickness, thickness * 0.65f);
                segment.GetComponent<Renderer>().sharedMaterial = material;
                Destroy(segment.GetComponent<Collider>());
            }
            return ring;
        }

        private static ParticleSystem CreateParticles(string name, Transform parent,
            Material material, Color color, int emissionRate, float radius,
            float size)
        {
            var particleObject = NewChild(name, parent).gameObject;
            var system = particleObject.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.7f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 160;

            var emission = system.emission;
            emission.rateOverTime = emissionRate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.34f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
            velocity.radial = new ParticleSystem.MinMaxCurve(-0.42f, -0.18f);

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 0.55f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            var renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 8;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
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
