#if DEBUG
using System.Collections;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim.Timing;
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// Editor and Development Builds only: the frame-time scenario for the map, run inside the game so nothing outside it
    /// disturbs the frames measured. At 1x the selected ship is followed on the map, at 3x its close-up is open, and at 5x
    /// the map is panned and zoomed by hand. Only the three allowed speeds are used and no time is skipped.
    /// </summary>
    public static class MapFrameCheck
    {
        public static IEnumerator Run(MapScreen map, RealTimeDriver driver, FrameTimeProbe probe, float secondsPerSpeed, string prefix)
        {
            driver.SetSpeed(ClockSpeed.X1);
            driver.Play();
            map.SetFollowing(true);
            probe.Begin(prefix + " 1x following");
            yield return Hold(secondsPerSpeed, null);
            probe.End();

            driver.SetSpeed(ClockSpeed.X3);
            map.OpenCloseUp();
            probe.Begin(prefix + " 3x close-up");
            yield return Hold(secondsPerSpeed, null);
            probe.End();
            map.CloseCloseUp();

            driver.SetSpeed(ClockSpeed.X5);
            map.StopFollowing();
            probe.Begin(prefix + " 5x pan and zoom");
            int frame = 0;
            yield return Hold(secondsPerSpeed, () =>
            {
                var cam = map.Camera;
                cam.PanWorld(new Vector2(Mathf.Sin(frame * 0.05f), Mathf.Cos(frame * 0.05f)) * cam.Camera.orthographicSize * 0.02f);
                var centre = new Vector2(cam.Camera.pixelWidth / 2f, cam.Camera.pixelHeight / 2f);
                cam.ZoomAt(frame / 60 % 2 == 0 ? 0.98f : 1.02f, centre);
                frame++;
            });
            probe.End();
            driver.Pause();
        }

        private static IEnumerator Hold(float seconds, System.Action eachFrame)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                eachFrame?.Invoke();
                yield return null;
            }
        }
    }
}
#endif
