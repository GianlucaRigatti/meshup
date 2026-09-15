using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

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
        private XRBodyTransformer bodyTransformer;
        private readonly XROriginMovement forcedMovement = new();
        private MovementLockReason locks;

        public MovementLockReason ActiveLocks => locks;
        public bool ManualMovementBlocked => locks != MovementLockReason.None;
        public Vector3 BodyPosition => GetBodyPosition(transform,
            bodyTransformer != null && bodyTransformer.xrOrigin != null
                ? bodyTransformer.xrOrigin.Camera?.transform
                : null);

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            bodyTransformer = GetComponentInChildren<XRBodyTransformer>(true);
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
            var current = BodyPosition;
            var next = Vector3.MoveTowards(current, target,
                Mathf.Max(0f, maxDistanceDelta));
            ApplyMotion(next - current);
            return Vector3.Distance(next, target);
        }

        public float MoveTowardsAvoidingObstacles(Vector3 target,
            float maxDistanceDelta, ref int preferredSide)
        {
            var current = BodyPosition;
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
            ApplyMotion(horizontal);
            return Vector3.Distance(current + horizontal, target);
        }

        public static Vector3 GetBodyPosition(Transform origin,
            Transform viewpoint)
        {
            if (origin == null)
            {
                return Vector3.zero;
            }
            if (viewpoint == null || !viewpoint.IsChildOf(origin))
            {
                return origin.position;
            }

            // Room-scale tracking moves the camera within the XR Origin. The
            // player's grounded body position is therefore the camera's
            // position projected onto the origin's floor plane, matching the
            // default body evaluator used by XR Interaction Toolkit.
            var localBody = origin.InverseTransformPoint(viewpoint.position);
            localBody.y = 0f;
            return origin.TransformPoint(localBody);
        }

        private void ApplyMotion(Vector3 motion)
        {
            if (bodyTransformer != null && bodyTransformer.isActiveAndEnabled
                && bodyTransformer.xrOrigin != null)
            {
                // Queue through XRI so its body evaluator keeps the collision
                // capsule under the tracked headset on Quest. Directly moving
                // the CharacterController bypasses that synchronization.
                forcedMovement.motion = motion;
                bodyTransformer.QueueTransformation(forcedMovement);
            }
            else if (characterController != null
                && characterController.enabled)
            {
                characterController.Move(motion);
            }
            else
            {
                transform.position += motion;
            }
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
            // The serialized list also contains the non-XR desktop provider.
            // Discover XRI providers at runtime so newly added translation,
            // teleport, or climb modes cannot fight scripted motion.
            foreach (var provider in GetComponentsInChildren<
                LocomotionProvider>(true))
            {
                // Looking and stick turning remain available during the
                // guided walk. They rotate around XRI's tracked body point
                // without contributing manual translation.
                if (provider is not ContinuousTurnProvider
                    && provider is not SnapTurnProvider)
                {
                    DisableProvider(provider);
                }
            }
            foreach (var provider in translationProviders)
            {
                DisableProvider(provider);
            }
        }

        private void DisableProvider(Behaviour provider)
        {
            if (provider == null || providerStates.ContainsKey(provider))
            {
                return;
            }
            providerStates.Add(provider, provider.enabled);
            provider.enabled = false;
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
