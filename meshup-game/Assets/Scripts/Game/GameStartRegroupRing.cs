using System;
using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class GameStartRegroupRing : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints = Array.Empty<Transform>();

        public int WaypointCount => waypoints?.Length ?? 0;
        public float Length => CalculateLength(GetPositions());
        public Vector3 ExitPosition => waypoints != null
            && waypoints.Length > 0 && waypoints[0] != null
                ? waypoints[0].position
                : transform.position;

        public void Configure(Transform[] ringWaypoints)
        {
            waypoints = ringWaypoints ?? Array.Empty<Transform>();
        }

        public float GetClosestDistance(Vector3 worldPoint,
            out Vector3 closestPoint)
        {
            return GetClosestDistance(GetPositions(), worldPoint,
                out closestPoint);
        }

        public Vector3 Sample(float distance)
        {
            return Sample(GetPositions(), distance);
        }

        public int GetShortestDirectionToExit(float distance)
        {
            return GetShortestDirectionToExit(distance, Length);
        }

        public float GetDistanceToExit(float distance, int direction)
        {
            return GetDistanceToExit(distance, Length, direction);
        }

        public static float GetClosestDistance(Vector3[] points,
            Vector3 worldPoint, out Vector3 closestPoint)
        {
            closestPoint = points != null && points.Length > 0
                ? points[0]
                : Vector3.zero;
            if (points == null || points.Length < 2)
            {
                return 0f;
            }

            var closestSqrDistance = float.PositiveInfinity;
            var closestRouteDistance = 0f;
            var traversed = 0f;
            for (var i = 0; i < points.Length; i++)
            {
                var next = (i + 1) % points.Length;
                var segment = points[next] - points[i];
                var segmentLength = segment.magnitude;
                if (segmentLength < 0.0001f)
                {
                    continue;
                }

                var amount = Mathf.Clamp01(Vector3.Dot(
                    worldPoint - points[i], segment)
                    / (segmentLength * segmentLength));
                var candidate = points[i] + segment * amount;
                var offset = worldPoint - candidate;
                offset.y = 0f;
                if (offset.sqrMagnitude < closestSqrDistance)
                {
                    closestSqrDistance = offset.sqrMagnitude;
                    closestPoint = candidate;
                    closestRouteDistance = traversed
                        + segmentLength * amount;
                }
                traversed += segmentLength;
            }
            return closestRouteDistance;
        }

        public static Vector3 Sample(Vector3[] points, float distance)
        {
            if (points == null || points.Length == 0)
            {
                return Vector3.zero;
            }
            if (points.Length == 1)
            {
                return points[0];
            }

            var length = CalculateLength(points);
            if (length < 0.0001f)
            {
                return points[0];
            }

            var remaining = Mathf.Repeat(distance, length);
            for (var i = 0; i < points.Length; i++)
            {
                var next = (i + 1) % points.Length;
                var segment = points[next] - points[i];
                var segmentLength = segment.magnitude;
                if (segmentLength < 0.0001f)
                {
                    continue;
                }
                if (remaining <= segmentLength)
                {
                    return points[i] + segment / segmentLength * remaining;
                }
                remaining -= segmentLength;
            }
            return points[0];
        }

        public static float CalculateLength(Vector3[] points)
        {
            if (points == null || points.Length < 2)
            {
                return 0f;
            }

            var length = 0f;
            for (var i = 0; i < points.Length; i++)
            {
                length += Vector3.Distance(points[i],
                    points[(i + 1) % points.Length]);
            }
            return length;
        }

        public static int GetShortestDirectionToExit(float distance,
            float length)
        {
            if (length <= 0f)
            {
                return -1;
            }
            var normalized = Mathf.Repeat(distance, length);
            return normalized <= length - normalized ? -1 : 1;
        }

        public static float GetDistanceToExit(float distance, float length,
            int direction)
        {
            if (length <= 0f)
            {
                return 0f;
            }
            var normalized = Mathf.Repeat(distance, length);
            return direction < 0 ? normalized : length - normalized;
        }

        private Vector3[] GetPositions()
        {
            if (waypoints == null)
            {
                return Array.Empty<Vector3>();
            }

            var points = new Vector3[waypoints.Length];
            for (var i = 0; i < waypoints.Length; i++)
            {
                points[i] = waypoints[i] != null
                    ? waypoints[i].position
                    : transform.position;
            }
            return points;
        }

        private void OnDrawGizmosSelected()
        {
            var points = GetPositions();
            if (points.Length < 2)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.25f, 0.85f, 1f);
            for (var i = 0; i < points.Length; i++)
            {
                Gizmos.DrawWireSphere(points[i], i == 0 ? 0.28f : 0.18f);
                Gizmos.DrawLine(points[i], points[(i + 1) % points.Length]);
            }
        }
    }
}
