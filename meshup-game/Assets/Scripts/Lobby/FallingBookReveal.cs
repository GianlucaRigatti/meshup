using System.Collections;
using UnityEngine;

namespace Meshup.Lobby
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class FallingBookReveal : MonoBehaviour
    {
        [Header("Sequence")]
        [SerializeField] private float revealDelay = 2.8f;
        [SerializeField] private float flightDuration = 1.45f;
        [SerializeField] private float impactDuration = 0.28f;
        [SerializeField] private float openingDuration = 0.9f;
        [SerializeField] private float hologramDuration = 0.65f;
        [SerializeField] private Vector3 shelfPosition;
        [SerializeField] private Vector3 landingPosition;
        [SerializeField] private Vector3 arcControlPoint;
        [SerializeField] private Vector3 shelfEuler = new(90f, 90f, 0f);
        [SerializeField] private Vector3 landingEuler = new(0f, 12f, 0f);
        [SerializeField] private float shelfScale = 0.52f;

        [Header("Book")]
        [SerializeField] private GameObject flightBook;
        [SerializeField] private GameObject openBook;
        [SerializeField] private Transform leftCoverPivot;
        [SerializeField] private Transform rightCoverPivot;
        [SerializeField] private Transform leftPagesPivot;
        [SerializeField] private Transform rightPagesPivot;
        [SerializeField] private Transform impactRing;

        [Header("Hologram")]
        [SerializeField] private GameObject projectorEffects;
        [SerializeField] private GameObject hologramCanvas;
        [SerializeField] private CanvasGroup hologramCanvasGroup;
        [SerializeField] private Light projectorLight;

        [Header("Interaction")]
        [SerializeField] private RoomTotemInteraction interaction;
        [SerializeField] private Collider interactionTrigger;
        [SerializeField] private RoomTotemPanel panel;
        [SerializeField] private LobbyFirstPersonController lobbyPlayer;

        public bool IsRevealed { get; private set; }

        private void Awake()
        {
            IsRevealed = false;
            transform.SetPositionAndRotation(shelfPosition, Quaternion.Euler(shelfEuler));
            transform.localScale = Vector3.one * shelfScale;
            flightBook.SetActive(true);
            openBook.SetActive(false);
            projectorEffects.SetActive(false);
            impactRing.gameObject.SetActive(false);
            interaction.SetInteractionAvailable(false);
            interactionTrigger.enabled = false;
            projectorLight.enabled = false;
            hologramCanvas.SetActive(false);
            hologramCanvasGroup.alpha = 0f;
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(revealDelay);

            var startRotation = Quaternion.Euler(shelfEuler);
            var endRotation = Quaternion.Euler(landingEuler);
            var elapsed = 0f;
            while (elapsed < flightDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / flightDuration);
                var eased = t * t * (3f - 2f * t);
                transform.position = QuadraticBezier(shelfPosition, arcControlPoint, landingPosition, eased);
                transform.localScale = Vector3.one * Mathf.Lerp(shelfScale, 1f, eased);
                var tumble = Quaternion.Euler(410f * t, -235f * t, 520f * t);
                transform.rotation = Quaternion.Slerp(startRotation, endRotation, eased) * tumble;
                yield return null;
            }

            transform.SetPositionAndRotation(landingPosition, Quaternion.Euler(landingEuler));
            transform.localScale = Vector3.one;
            yield return AnimateImpact();

            flightBook.SetActive(false);
            openBook.SetActive(true);
            SetBookPose(0f);
            yield return AnimateOpening();

            projectorEffects.SetActive(true);
            projectorLight.enabled = true;
            hologramCanvas.SetActive(true);
            yield return AnimateHologram();

            IsRevealed = true;
            panel.Open(lobbyPlayer);
        }

        private IEnumerator AnimateImpact()
        {
            impactRing.gameObject.SetActive(true);
            var elapsed = 0f;
            while (elapsed < impactDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / impactDuration);
                var bounce = Mathf.Sin(t * Mathf.PI) * (1f - t) * 0.16f;
                transform.position = landingPosition + Vector3.up * bounce;
                impactRing.localScale = Vector3.one * Mathf.Lerp(0.15f, 1.35f, t);
                yield return null;
            }
            transform.SetPositionAndRotation(landingPosition, Quaternion.Euler(landingEuler));
            transform.localScale = Vector3.one;
            impactRing.gameObject.SetActive(false);
        }

        private IEnumerator AnimateOpening()
        {
            var elapsed = 0f;
            while (elapsed < openingDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / openingDuration);
                SetBookPose(t);
                yield return null;
            }
            SetBookPose(1f);
        }

        private void SetBookPose(float t)
        {
            var smooth = t * t * (3f - 2f * t);
            leftCoverPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-82f, -7f, smooth));
            rightCoverPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(82f, 7f, smooth));
            leftPagesPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-77f, -4f, smooth));
            rightPagesPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(77f, 4f, smooth));
        }

        private IEnumerator AnimateHologram()
        {
            var elapsed = 0f;
            while (elapsed < hologramDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / hologramDuration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                hologramCanvasGroup.alpha = eased;
                projectorLight.intensity = Mathf.Lerp(0f, 0.34f, eased);
                yield return null;
            }
            hologramCanvasGroup.alpha = 1f;
        }

        private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            var inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }
    }
}
