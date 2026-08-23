using System.Threading;
using UnityEngine;

namespace Ubiq.SceneSwitcher
{
    public interface IPlayerRigReset
    {
        void ResetAt(Transform spawn);
    }

    public interface IRoomSceneTransitionView
    {
        Awaitable FadeOutAsync(CancellationToken token);
        Awaitable FadeInAsync(CancellationToken token);
        void SetStatus(string message);
    }
}
