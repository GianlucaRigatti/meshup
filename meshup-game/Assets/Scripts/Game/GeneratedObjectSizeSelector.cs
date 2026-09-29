using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class GeneratedObjectSizeSelector : MonoBehaviour
    {
        [SerializeField] private GameObject smallButton;
        [SerializeField] private GameObject mediumButton;
        [SerializeField] private GameObject extraLargeButton;

        private sealed class Binding
        {
            public GeneratedObjectSize Size;
            public ConsoleButtonFeedback Feedback;
            public AudioSource AudioSource;
            public XRSimpleInteractable Interactable;
            public UnityAction<SelectEnterEventArgs> Listener;
        }

        private readonly List<Binding> bindings = new();
        private Func<bool> canSelect;
        private Action<GeneratedObjectSize> requestSelection;
        private bool interactable;

        public GeneratedObjectSize SelectedSize { get; private set; } =
            GeneratedObjectSize.Medium;

        public void Configure(Func<bool> selectionAllowed,
            Action<GeneratedObjectSize> selectionRequested = null)
        {
            ClearBindings();
            canSelect = selectionAllowed;
            requestSelection = selectionRequested;
            AddBinding(smallButton, GeneratedObjectSize.Small);
            AddBinding(mediumButton, GeneratedObjectSize.Medium);
            AddBinding(extraLargeButton, GeneratedObjectSize.ExtraLarge);
            ResetToMedium();
        }

        public static Renderer[] FindPhysicalButtons(params GameObject[] buttons)
        {
            if (buttons == null || buttons.Length != 3 || buttons.Any(button =>
                    button == null || button.GetComponent<ConsoleButtonFeedback>() == null))
            {
                return Array.Empty<Renderer>();
            }
            return buttons.Reverse().Select(button => button.GetComponent<Renderer>())
                .Where(renderer => renderer != null).ToArray();
        }

        public void ResetToMedium()
        {
            ApplySelectedSize(GeneratedObjectSize.Medium);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            foreach (var binding in bindings)
            {
                binding.Interactable.enabled = value;
            }
        }

        public bool TrySelect(GeneratedObjectSize size)
        {
            if (!interactable || canSelect?.Invoke() != true)
            {
                return false;
            }
            size = GeneratedObjectSizes.Normalize(size);
            if (requestSelection != null) requestSelection(size);
            else ApplySelectedSize(size, true);
            return true;
        }

        public void ApplySelectedSize(GeneratedObjectSize size, bool animate = false)
        {
            SelectedSize = GeneratedObjectSizes.Normalize(size);
            foreach (var binding in bindings)
            {
                binding.Feedback.SetSelected(binding.Size == SelectedSize);
            }
            if (!animate) return;

            var selected = bindings.Find(item => item.Size == SelectedSize);
            selected?.Feedback.Pulse();
            if (selected?.AudioSource != null && selected.AudioSource.clip != null)
            {
                selected.AudioSource.PlayOneShot(selected.AudioSource.clip);
            }
        }

        private void AddBinding(GameObject button, GeneratedObjectSize size)
        {
            if (button == null || !button.TryGetComponent(out ConsoleButtonFeedback feedback)
                || !button.TryGetComponent(out XRSimpleInteractable xr)
                || !button.TryGetComponent(out AudioSource audio))
            {
                Debug.LogError($"[MeshUp] {size} selector button needs authored "
                    + "feedback, XR interaction, and audio components.", this);
                return;
            }

            UnityAction<SelectEnterEventArgs> listener = _ => TrySelect(size);
            xr.selectEntered.AddListener(listener);
            bindings.Add(new Binding
            {
                Size = size,
                Feedback = feedback,
                AudioSource = audio,
                Interactable = xr,
                Listener = listener
            });
        }

        private void ClearBindings()
        {
            foreach (var binding in bindings)
            {
                if (binding.Interactable != null)
                {
                    binding.Interactable.selectEntered.RemoveListener(binding.Listener);
                }
            }
            bindings.Clear();
        }

        private void OnDestroy()
        {
            ClearBindings();
        }
    }
}
