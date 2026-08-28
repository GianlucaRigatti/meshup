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
