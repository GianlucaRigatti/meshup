using System;
using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class GameStartRoute : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints = Array.Empty<Transform>();
        [SerializeField] private float columnSpacing = 0.9f;
        [SerializeField] private float rowSpacing = 1.25f;

        public int WaypointCount => waypoints?.Length ?? 0;
        public float ColumnSpacing => columnSpacing;
        public float RowSpacing => rowSpacing;
        public float Length => CalculateLength(GetPositions());

        public void Configure(Transform[] routeWaypoints, float columns, float rows)
        {
            waypoints = routeWaypoints ?? Array.Empty<Transform>();
            columnSpacing = Mathf.Max(0.1f, columns);
            rowSpacing = Mathf.Max(0.1f, rows);
        }

        public Vector3 GetSlotPosition(float leaderDistance, int slotIndex,
            int playerCount)
        {
            return GetSlotPosition(GetPositions(), leaderDistance, slotIndex,
                playerCount, columnSpacing, rowSpacing);
        }

        public float GetClosestDistance(Vector3 worldPoint,
            out Vector3 closestPoint)
        {
            return GetClosestDistance(GetPositions(), worldPoint,
                out closestPoint);
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

            var bestSqrDistance = float.PositiveInfinity;
            var bestRouteDistance = 0f;
            var traversed = 0f;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var segment = points[i + 1] - points[i];
                var segmentLength = segment.magnitude;
                if (segmentLength < 0.0001f)
                {
                    continue;
                }

                var flatSegment = new Vector2(segment.x, segment.z);
                var flatLengthSqr = flatSegment.sqrMagnitude;
                var amount = flatLengthSqr < 0.0001f
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(
                        new Vector2(worldPoint.x - points[i].x,
                            worldPoint.z - points[i].z), flatSegment)
                        / flatLengthSqr);
                var candidate = points[i] + segment * amount;
                var offset = worldPoint - candidate;
                offset.y = 0f;
                if (offset.sqrMagnitude < bestSqrDistance)
                {
                    bestSqrDistance = offset.sqrMagnitude;
                    closestPoint = candidate;
                    bestRouteDistance = traversed + segmentLength * amount;
                }
                traversed += segmentLength;
            }
            return bestRouteDistance;
        }

        public static Vector3 GetSlotPosition(Vector3[] points,
            float leaderDistance, int slotIndex, int playerCount,
            float columns, float rows)
        {
            var row = Mathf.Max(0, slotIndex / 2);
            var centeredOddSlot = playerCount % 2 == 1
                && slotIndex == playerCount - 1;
            var lateral = centeredOddSlot
                ? 0f
                : (slotIndex % 2 == 0 ? -0.5f : 0.5f) * columns;
            var distance = leaderDistance - row * rows;
            var center = Sample(points, distance, out var tangent);
            var flatTangent = Vector3.ProjectOnPlane(tangent, Vector3.up);
            if (flatTangent.sqrMagnitude < 0.0001f)
            {
                flatTangent = Vector3.forward;
            }

            var right = Vector3.Cross(Vector3.up, flatTangent.normalized);
            return center + right * lateral;
        }

        public static Vector3 Sample(Vector3[] points, float distance,
            out Vector3 tangent)
        {
            if (points == null || points.Length == 0)
            {
                tangent = Vector3.forward;
                return Vector3.zero;
            }
            if (points.Length == 1)
            {
                tangent = Vector3.forward;
                return points[0];
            }

            if (distance < 0f)
            {
                tangent = SafeDirection(points[1] - points[0]);
                return points[0] + tangent * distance;
            }

            var remaining = distance;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var segment = points[i + 1] - points[i];
                var length = segment.magnitude;
                if (length < 0.0001f)
                {
                    continue;
                }

                if (remaining <= length)
                {
                    tangent = segment / length;
                    return points[i] + tangent * remaining;
                }
                remaining -= length;
            }

            tangent = SafeDirection(points[^1] - points[^2]);
            return points[^1];
        }

        public static float CalculateLength(Vector3[] points)
        {
            if (points == null)
            {
                return 0f;
            }

            var result = 0f;
            for (var i = 0; i < points.Length - 1; i++)
            {
                result += Vector3.Distance(points[i], points[i + 1]);
            }
            return result;
        }

        private Vector3[] GetPositions()
        {
            if (waypoints == null)
            {
                return Array.Empty<Vector3>();
            }

            var result = new Vector3[waypoints.Length];
            for (var i = 0; i < waypoints.Length; i++)
            {
                result[i] = waypoints[i] != null
                    ? waypoints[i].position
                    : transform.position;
            }
            return result;
        }

        private static Vector3 SafeDirection(Vector3 value)
        {
            return value.sqrMagnitude < 0.0001f
                ? Vector3.forward
                : value.normalized;
        }

        private void OnDrawGizmosSelected()
        {
            var points = GetPositions();
            if (points.Length < 2)
            {
                return;
            }

            Gizmos.color = new Color(0.1f, 0.9f, 1f, 1f);
            for (var i = 0; i < points.Length; i++)
            {
                Gizmos.DrawSphere(points[i], 0.16f);
                if (i + 1 < points.Length)
                {
                    Gizmos.DrawLine(points[i], points[i + 1]);
                }
            }

            Gizmos.color = new Color(1f, 0.8f, 0.15f, 1f);
            for (var slot = 0; slot < 6; slot++)
            {
                Gizmos.DrawWireSphere(GetSlotPosition(0f, slot, 6), 0.22f);
                Gizmos.DrawWireSphere(GetSlotPosition(Length, slot, 6), 0.22f);
            }
        }
    }
}
