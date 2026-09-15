using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string ButtonPressClipResourcePath = "SizeButtonPress";
        private const float ButtonPressVolume = 0.45f;

        private sealed class Binding
        {
            public GeneratedObjectSize Size;
            public TMP_Text Label;
            public Color LabelColor;
            public Renderer ButtonRenderer;
            public Material[] OriginalMaterials;
            public Material[] ButtonMaterials;
            public Light Glow;
            public AudioSource AudioSource;
            public XRSimpleInteractable Interactable;
            public UnityAction<SelectEnterEventArgs> Listener;
        }

        private readonly List<Binding> bindings = new();
        private Func<bool> canSelect;
        private bool interactable;
        private AudioClip buttonPressClip;

        public GeneratedObjectSize SelectedSize { get; private set; } =
            GeneratedObjectSize.Medium;

        public void Configure(GameObject small, GameObject medium,
            GameObject extraLarge, Func<bool> selectionAllowed)
        {
            ClearBindings();
            canSelect = selectionAllowed;
            buttonPressClip = Resources.Load<AudioClip>(
                ButtonPressClipResourcePath);
            if (buttonPressClip == null)
            {
                Debug.LogWarning($"[MeshUp] Size button sound not found at "
                    + $"Resources/{ButtonPressClipResourcePath}.");
            }
            DisableLabelHitTarget(small);
            DisableLabelHitTarget(medium);
            DisableLabelHitTarget(extraLarge);
            var buttons = FindPhysicalButtons(small, medium, extraLarge);
            if (buttons.Length != 3)
            {
                Debug.LogError("[MeshUp] The S, M, and XL physical buttons "
                    + "could not be resolved from the authored selector model.");
                return;
            }
            AddBinding(small, buttons[2], GeneratedObjectSize.Small, "S");
            AddBinding(medium, buttons[1], GeneratedObjectSize.Medium, "M");
            AddBinding(extraLarge, buttons[0], GeneratedObjectSize.ExtraLarge,
                "XL");
            ResetToMedium();
        }

        public static Renderer[] FindPhysicalButtons(params GameObject[] labels)
        {
            if (labels == null || labels.Length != 3
                || labels.Any(label => label == null))
            {
                return Array.Empty<Renderer>();
            }
            var root = CommonAncestor(labels.Select(label => label.transform));
            if (root == null)
            {
                return Array.Empty<Renderer>();
            }
            return root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.sharedMaterials.Any(material =>
                    material != null && material.name.StartsWith("Button",
                        StringComparison.OrdinalIgnoreCase)))
                .OrderBy(renderer => root.InverseTransformPoint(
                    renderer.bounds.center).x)
                .ToArray();
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

        private void AddBinding(GameObject labelObject, Renderer buttonRenderer,
            GeneratedObjectSize size, string labelText)
        {
            var label = labelObject.GetComponent<TMP_Text>();
            if (label == null)
            {
                label = labelObject.GetComponentInChildren<TMP_Text>(true);
            }
            if (label != null)
            {
                label.text = labelText;
            }
            var target = buttonRenderer.gameObject;
            var collider = target.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = target.AddComponent<BoxCollider>();
            }
            var mesh = target.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null)
            {
                collider.center = mesh.bounds.center;
                collider.size = mesh.bounds.size;
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
            var originalMaterials = buttonRenderer.sharedMaterials;
            var materials = originalMaterials.Select(material =>
                material != null ? new Material(material) : null).ToArray();
            buttonRenderer.sharedMaterials = materials;
            var glowObject = new GameObject("Selected Size Glow");
            glowObject.transform.SetParent(target.transform, false);
            glowObject.transform.localPosition = mesh != null
                ? mesh.bounds.center : Vector3.zero;
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.range = Mathf.Max(0.25f, buttonRenderer.bounds.extents.magnitude
                * 1.5f);
            glow.intensity = size == GeneratedObjectSize.Small ? 0.65f : 2f;
            var audioSource = target.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
            audioSource.volume = ButtonPressVolume;
            audioSource.maxDistance = 8f;
            audioSource.clip = buttonPressClip;
            UnityAction<SelectEnterEventArgs> listener = _ =>
                SelectFromButton(size, audioSource);
            xr.selectEntered.AddListener(listener);
            bindings.Add(new Binding
            {
                Size = size,
                Label = label,
                LabelColor = label != null ? label.color : Color.cyan,
                ButtonRenderer = buttonRenderer,
                OriginalMaterials = originalMaterials,
                ButtonMaterials = materials,
                Glow = glow,
                AudioSource = audioSource,
                Interactable = xr,
                Listener = listener
            });
        }

        private void SelectFromButton(GeneratedObjectSize size,
            AudioSource audioSource)
        {
            if (TrySelect(size) && buttonPressClip != null
                && audioSource != null)
            {
                audioSource.PlayOneShot(buttonPressClip);
            }
        }

        private void RefreshLabels()
        {
            foreach (var binding in bindings)
            {
                var selected = binding.Size == SelectedSize;
                if (binding.Label != null)
                {
                    var plain = binding.Size == GeneratedObjectSize.ExtraLarge
                        ? "XL" : binding.Size == GeneratedObjectSize.Medium ? "M" : "S";
                    binding.Label.text = plain;
                    binding.Label.color = selected ? binding.LabelColor
                        : Color.Lerp(binding.LabelColor, Color.black, 0.65f);
                    binding.Label.fontStyle = selected
                        ? FontStyles.Bold : FontStyles.Normal;
                    binding.Label.alpha = selected
                        ? binding.Size == GeneratedObjectSize.Small ? 0.8f : 1f
                        : 0.45f;
                }
                if (binding.Glow != null)
                {
                    binding.Glow.color = binding.LabelColor;
                    binding.Glow.enabled = selected;
                }
                foreach (var material in binding.ButtonMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    var emissionStrength = binding.Size ==
                        GeneratedObjectSize.Small ? 1.1f : 2.5f;
                    var emission = selected
                        ? binding.LabelColor * emissionStrength : Color.black;
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.SetColor("_EmissionColor", emission);
                    }
                    if (material.HasProperty("emissiveFactor"))
                    {
                        material.SetColor("emissiveFactor", emission);
                    }
                    if (selected)
                    {
                        material.EnableKeyword("_EMISSION");
                    }
                    else
                    {
                        material.DisableKeyword("_EMISSION");
                    }
                }
            }
        }

        private static void DisableLabelHitTarget(GameObject label)
        {
            if (label == null)
            {
                return;
            }
            var interactable = label.GetComponent<XRSimpleInteractable>();
            if (interactable != null)
            {
                interactable.enabled = false;
            }
            var collider = label.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private static Transform CommonAncestor(IEnumerable<Transform> values)
        {
            var transforms = values.ToArray();
            for (var candidate = transforms[0]; candidate != null;
                 candidate = candidate.parent)
            {
                if (transforms.All(value => value == candidate
                    || value.IsChildOf(candidate)))
                {
                    return candidate;
                }
            }
            return null;
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
                if (binding.ButtonRenderer != null)
                {
                    binding.ButtonRenderer.sharedMaterials =
                        binding.OriginalMaterials;
                }
                if (binding.Glow != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(binding.Glow.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(binding.Glow.gameObject);
                    }
                }
                if (binding.AudioSource != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(binding.AudioSource);
                    }
                    else
                    {
                        DestroyImmediate(binding.AudioSource);
                    }
                }
                foreach (var material in binding.ButtonMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    if (Application.isPlaying)
                    {
                        Destroy(material);
                    }
                    else
                    {
                        DestroyImmediate(material);
                    }
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
