using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class GeneratedObjectSizeSelector : MonoBehaviour
    {
        private sealed class Binding
        {
            public GeneratedObjectSize Size;
            public GameObject Target;
            public TMP_Text Label;
            public XRSimpleInteractable Interactable;
            public UnityAction<SelectEnterEventArgs> Listener;
        }

        private readonly List<Binding> bindings = new();
        private Func<bool> canSelect;
        private bool interactable;

        public GeneratedObjectSize SelectedSize { get; private set; } =
            GeneratedObjectSize.Medium;

        public void Configure(GameObject small, GameObject medium,
            GameObject extraLarge, Func<bool> selectionAllowed)
        {
            ClearBindings();
            canSelect = selectionAllowed;
            AddBinding(small, GeneratedObjectSize.Small, "S");
            AddBinding(medium, GeneratedObjectSize.Medium, "M");
            AddBinding(extraLarge, GeneratedObjectSize.ExtraLarge, "XL");
            ResetToMedium();
        }

        public void ResetToMedium()
        {
            SelectedSize = GeneratedObjectSize.Medium;
            RefreshLabels();
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            foreach (var binding in bindings)
            {
                if (binding.Interactable != null)
                {
                    binding.Interactable.enabled = value;
                }
            }
        }

        public bool TrySelect(GeneratedObjectSize size)
        {
            if (!interactable || canSelect?.Invoke() != true)
            {
                return false;
            }
            SelectedSize = GeneratedObjectSizes.Normalize(size);
            RefreshLabels();
            return true;
        }

        private void AddBinding(GameObject target, GeneratedObjectSize size,
            string labelText)
        {
            if (target == null)
            {
                return;
            }
            var label = target.GetComponent<TMP_Text>();
            if (label == null)
            {
                label = target.GetComponentInChildren<TMP_Text>(true);
            }
            if (label != null)
            {
                label.text = labelText;
            }
            var collider = target.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = target.AddComponent<BoxCollider>();
            }
            if (target.transform is RectTransform rect)
            {
                collider.center = Vector3.zero;
                collider.size = new Vector3(
                    Mathf.Max(0.01f, rect.rect.width),
                    Mathf.Max(0.01f, rect.rect.height), 0.08f);
            }
            var xr = target.GetComponent<XRSimpleInteractable>();
            if (xr == null)
            {
                xr = target.AddComponent<XRSimpleInteractable>();
            }
            if (!xr.colliders.Contains(collider))
            {
                xr.colliders.Add(collider);
            }
            UnityAction<SelectEnterEventArgs> listener = _ => TrySelect(size);
            xr.selectEntered.AddListener(listener);
            bindings.Add(new Binding
            {
                Size = size,
                Target = target,
                Label = label,
                Interactable = xr,
                Listener = listener
            });
        }

        private void RefreshLabels()
        {
            foreach (var binding in bindings)
            {
                if (binding.Label == null)
                {
                    continue;
                }
                var selected = binding.Size == SelectedSize;
                var plain = binding.Size == GeneratedObjectSize.ExtraLarge
                    ? "XL" : binding.Size == GeneratedObjectSize.Medium ? "M" : "S";
                binding.Label.text = plain;
                binding.Label.fontStyle = selected
                    ? FontStyles.Bold : FontStyles.Normal;
                binding.Label.alpha = selected ? 1f : 0.55f;
            }
        }

        private void ClearBindings()
        {
            foreach (var binding in bindings)
            {
                if (binding.Interactable != null)
                {
                    binding.Interactable.selectEntered.RemoveListener(
                        binding.Listener);
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
