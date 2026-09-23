using System.Collections;
using System.Reflection;
using Meshup.Lobby;
using NUnit.Framework;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class HologramBillboardTests
    {
        [Test]
        public void PanelAnimationUsesTheCurrentMenuAndPreservesItsScale()
        {
            var owner = new GameObject("Hologram", typeof(Canvas));
            var menu = new GameObject("Ubiq Sample Menu", typeof(RectTransform));
            try
            {
                menu.transform.SetParent(owner.transform, false);
                var fullScale = Vector3.one * 2f;
                menu.transform.localScale = fullScale;
                var panel = owner.AddComponent<RoomTotemPanel>();
                typeof(RoomTotemPanel).GetField("panelRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(panel, menu);
                var billboard = owner.AddComponent<HologramBillboard>();
                typeof(HologramBillboard).GetField("panel",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(billboard, panel);

                var animation = (IEnumerator)typeof(HologramBillboard)
                    .GetMethod("AnimatePanelIn",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(billboard, null);
                Assert.That(animation, Is.Not.Null);
                Assert.That(animation.MoveNext(), Is.True);
                Assert.That(menu.GetComponent<CanvasGroup>(), Is.Not.Null);
                Assert.That(menu.transform.localScale.x,
                    Is.GreaterThan(fullScale.x * 0.87f));
                Assert.That(menu.transform.localScale.x,
                    Is.LessThanOrEqualTo(fullScale.x));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
