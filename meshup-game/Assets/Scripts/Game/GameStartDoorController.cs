using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class GameStartDoorController : MonoBehaviour
    {
        private static readonly int CharacterNearby =
            Animator.StringToHash("character_nearby");
        private const float OpeningDelaySeconds = 0.09f;

        [SerializeField] private Animator doorAnimator;
        [SerializeField] private GameStartRoute route;
        [SerializeField] private float routeDistance;
        [SerializeField] private float pathOffset;
        [SerializeField] private float openLeadDistance = 4f;

        private bool isOpen;
        [SerializeField] private AudioSource openingAudioSource;

        public float RouteDistance => routeDistance;
        public float PathOffset => pathOffset;
        public bool IsConfigured => doorAnimator != null && route != null;

        private void Awake()
        {
            doorAnimator ??= GetComponent<Animator>();
            RecalculateRoutePosition();
            SetOpen(false, true);
        }

        public void SetFormationProgress(float leaderDistance)
        {
            if (ShouldOpen(leaderDistance, routeDistance, openLeadDistance))
            {
                SetOpen(true);
            }
        }

        public void Close()
        {
            SetOpen(false);
        }

        public static bool ShouldOpen(float leaderDistance,
            float doorRouteDistance, float leadDistance)
        {
            var opensAt = doorRouteDistance - Mathf.Max(0f, leadDistance);
            return leaderDistance >= opensAt;
        }

        private void RecalculateRoutePosition()
        {
            if (route == null)
            {
                routeDistance = 0f;
                pathOffset = float.PositiveInfinity;
                return;
            }

            routeDistance = route.GetClosestDistance(transform.position,
                out var closestPoint);
            var offset = transform.position - closestPoint;
            offset.y = 0f;
            pathOffset = offset.magnitude;
        }

        private void SetOpen(bool open, bool force = false)
        {
            if (!force && isOpen == open)
            {
                return;
            }

            var wasOpen = isOpen;
            isOpen = open;
            if (doorAnimator != null && doorAnimator.enabled)
            {
                doorAnimator.SetBool(CharacterNearby, open);
            }

            if (open && !wasOpen)
            {
                openingAudioSource?.PlayDelayed(OpeningDelaySeconds);
            }
            else if (!open && wasOpen)
            {
                openingAudioSource?.Stop();
            }
        }

        private void OnDisable()
        {
            SetOpen(false, true);
        }

        private void OnDrawGizmosSelected()
        {
            if (route == null)
            {
                return;
            }

            route.GetClosestDistance(transform.position, out var closestPoint);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, closestPoint);
            Gizmos.DrawWireSphere(closestPoint, 0.2f);
        }
    }
}
