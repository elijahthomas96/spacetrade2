#if DEBUG
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>Editor and Development Builds only: marks the end of our scripts' work in a frame for <see cref="FrameTimeProbe"/>.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class FrameTimeProbeEnd : MonoBehaviour
    {
        public FrameTimeProbe Probe;

        private void LateUpdate()
        {
            if (Probe != null) Probe.EndOfWork();
        }
    }
}
#endif
