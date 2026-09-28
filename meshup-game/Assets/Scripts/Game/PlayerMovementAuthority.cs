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
