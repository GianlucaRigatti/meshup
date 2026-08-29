using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class GameStartDoorController : MonoBehaviour
    {
        private static readonly int CharacterNearby =
            Animator.StringToHash("character_nearby");

        [SerializeField] private Animator doorAnimator;
        [SerializeField] private GameStartRoute route;
        [SerializeField] private float routeDistance;
        [SerializeField] private float pathOffset;
        [SerializeField] private float openLeadDistance = 3f;
        [SerializeField] private float closeClearance = 1f;

        private bool isOpen;

        public float RouteDistance => routeDistance;
        public float PathOffset => pathOffset;
        public bool IsConfigured => doorAnimator != null && route != null;

        public void Configure(Animator animator, GameStartRoute formationRoute)
        {
            doorAnimator = animator;
            route = formationRoute;
            RecalculateRoutePosition();
        }

        private void Awake()
        {
            doorAnimator ??= GetComponent<Animator>();
            RecalculateRoutePosition();
            SetOpen(false, true);
        }

        public void SetFormationProgress(float leaderDistance, int playerCount)
        {
            SetOpen(ShouldBeOpen(leaderDistance, routeDistance, playerCount,
                route != null ? route.RowSpacing : 1.25f,
                openLeadDistance, closeClearance));
        }

        public void Close()
        {
            SetOpen(false);
        }

        public static bool ShouldBeOpen(float leaderDistance,
            float doorRouteDistance, int playerCount, float rowSpacing,
            float leadDistance, float clearance)
        {
            var finalRow = Mathf.Max(0, (playerCount - 1) / 2);
            var opensAt = doorRouteDistance - Mathf.Max(0f, leadDistance);
            var closesAt = doorRouteDistance
                + finalRow * Mathf.Max(0f, rowSpacing)
                + Mathf.Max(0f, clearance);
            return leaderDistance >= opensAt && leaderDistance < closesAt;
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

            isOpen = open;
            if (doorAnimator != null && doorAnimator.enabled)
            {
                doorAnimator.SetBool(CharacterNearby, open);
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
