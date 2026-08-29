using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meshup.Game
{
    [Flags]
    public enum MovementLockReason
    {
        None = 0,
        PauseMenu = 1,
        GameStartSequence = 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovementAuthority : MonoBehaviour
    {
        [SerializeField] private Behaviour[] translationProviders =
            Array.Empty<Behaviour>();

        private readonly Dictionary<Behaviour, bool> providerStates = new();
        private CharacterController characterController;
        private MovementLockReason locks;

        public MovementLockReason ActiveLocks => locks;
        public bool ManualMovementBlocked => locks != MovementLockReason.None;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
        }

        public void Configure(Behaviour[] providers)
        {
            translationProviders = providers ?? Array.Empty<Behaviour>();
        }

        public void SetLock(MovementLockReason reason, bool locked)
        {
            var previous = locks;
            locks = locked ? locks | reason : locks & ~reason;
            if (previous == MovementLockReason.None
                && locks != MovementLockReason.None)
            {
                DisableTranslation();
            }
            else if (previous != MovementLockReason.None
                && locks == MovementLockReason.None)
            {
                RestoreTranslation();
            }
        }

        public float MoveTowards(Vector3 target, float maxDistanceDelta)
        {
            var current = transform.position;
            var next = Vector3.MoveTowards(current, target,
                Mathf.Max(0f, maxDistanceDelta));
            if (characterController != null && characterController.enabled)
            {
                characterController.Move(next - current);
            }
            else
            {
                transform.position = next;
            }
            return Vector3.Distance(transform.position, target);
        }

        public float MoveTowardsAvoidingObstacles(Vector3 target,
            float maxDistanceDelta, ref int preferredSide)
        {
            var current = transform.position;
            var flatTarget = target - current;
            flatTarget.y = 0f;
            if (flatTarget.sqrMagnitude < 0.0001f)
            {
                preferredSide = 0;
                return MoveTowards(target, maxDistanceDelta);
            }

            var forward = flatTarget.normalized;
            var probeDistance = Mathf.Max(0.7f, maxDistanceDelta * 4f);
            var direction = forward;
            if (!DirectionIsClear(forward, probeDistance))
            {
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                if (preferredSide == 0)
                {
                    preferredSide = ChooseAvoidanceSide(forward, right,
                        probeDistance);
                }

                if (!TryGetAvoidanceDirection(forward, right,
                    preferredSide, probeDistance, out direction))
                {
                    preferredSide *= -1;
                    if (!TryGetAvoidanceDirection(forward, right,
                        preferredSide, probeDistance, out direction))
                    {
                        // Wait for a moving obstruction instead of repeatedly
                        // pushing the CharacterController into it.
                        direction = Vector3.zero;
                    }
                }
            }
            else
            {
                preferredSide = 0;
            }

            var horizontal = direction * Mathf.Max(0f, maxDistanceDelta);
            horizontal.y = Mathf.MoveTowards(0f, target.y - current.y,
                Mathf.Max(0f, maxDistanceDelta));
            if (characterController != null && characterController.enabled)
            {
                characterController.Move(horizontal);
            }
            else
            {
                transform.position += horizontal;
            }
            return Vector3.Distance(transform.position, target);
        }

        private int ChooseAvoidanceSide(Vector3 forward, Vector3 right,
            float probeDistance)
        {
            var rightClear = TryGetAvoidanceDirection(forward, right, 1,
                probeDistance, out _);
            var leftClear = TryGetAvoidanceDirection(forward, right, -1,
                probeDistance, out _);
            if (rightClear != leftClear)
            {
                return rightClear ? 1 : -1;
            }

            // Stable tie-breaker prevents left/right oscillation while
            // following the edge of a large obstacle.
            return 1;
        }

        private bool TryGetAvoidanceDirection(Vector3 forward, Vector3 right,
            int side, float probeDistance, out Vector3 direction)
        {
            var diagonal = (forward + right * side * 1.35f).normalized;
            if (DirectionIsClear(diagonal, probeDistance * 0.65f)
                && HasGroundAhead(diagonal, probeDistance * 0.5f))
            {
                direction = diagonal;
                return true;
            }

            var sideways = right * side;
            if (DirectionIsClear(sideways, probeDistance * 0.65f)
                && HasGroundAhead(sideways, probeDistance * 0.5f))
            {
                direction = sideways;
                return true;
            }

            direction = Vector3.zero;
            return false;
        }

        private bool DirectionIsClear(Vector3 direction, float distance)
        {
            if (characterController == null || !characterController.enabled)
            {
                return true;
            }

            GetControllerCapsule(out var bottom, out var top, out var radius);
            var hits = Physics.CapsuleCastAll(bottom, top, radius, direction,
                distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (!IsOwnCollider(hit.collider))
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasGroundAhead(Vector3 direction, float distance)
        {
            var origin = transform.position + direction * distance
                + Vector3.up * 0.75f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 2.5f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (!IsOwnCollider(hit.collider))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOwnCollider(Collider candidate)
        {
            return candidate != null
                && candidate.transform.IsChildOf(transform);
        }

        private void GetControllerCapsule(out Vector3 bottom,
            out Vector3 top, out float radius)
        {
            var scale = transform.lossyScale;
            radius = characterController.radius
                * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            radius = Mathf.Max(0.05f, radius * 0.9f);
            var height = Mathf.Max(characterController.height
                * Mathf.Abs(scale.y), radius * 2f);
            var center = transform.TransformPoint(characterController.center);
            var halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            bottom = center - Vector3.up * halfSegment;
            top = center + Vector3.up * halfSegment;
        }

        private void DisableTranslation()
        {
            providerStates.Clear();
            foreach (var provider in translationProviders)
            {
                if (provider == null || providerStates.ContainsKey(provider))
                {
                    continue;
                }
                providerStates.Add(provider, provider.enabled);
                provider.enabled = false;
            }
        }

        private void RestoreTranslation()
        {
            foreach (var item in providerStates)
            {
                if (item.Key != null)
                {
                    item.Key.enabled = item.Value;
                }
            }
            providerStates.Clear();
        }

        private void OnDisable()
        {
            locks = MovementLockReason.None;
            RestoreTranslation();
        }
    }
}
