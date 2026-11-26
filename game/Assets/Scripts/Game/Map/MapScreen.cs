using System.Collections.Generic;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>The galaxy map screen (spec 9.1): stars, backdrop, camera, controls, and text, for any galaxy size.</summary>
    public sealed class MapScreen : MonoBehaviour
    {
        public GalaxyMapView View { get; private set; }
        public MapCamera Camera { get; private set; }
        public MapHud Hud { get; private set; }
        public MapInput Input { get; private set; }
        public Simulation Simulation { get; private set; }

        public int SelectedSystemId => View.SelectedSystemId;

        public static MapScreen Create(Camera camera)
        {
            var go = new GameObject("Map");
            var screen = go.AddComponent<MapScreen>();
            screen.View = new GameObject("Systems").AddComponent<GalaxyMapView>();
            screen.View.transform.SetParent(go.transform, false);
            screen.Camera = camera.GetComponent<MapCamera>();
            if (screen.Camera == null) screen.Camera = camera.gameObject.AddComponent<MapCamera>();
            var background = new GameObject("Background").AddComponent<MapBackground>();
            background.transform.SetParent(go.transform, false);
            background.Build(camera);
            screen.Hud = MapHud.Create(go.transform);
            screen.Input = go.AddComponent<MapInput>();
            screen.Input.Map = screen;
            return screen;
        }

        public void Show(Simulation sim, IReadOnlyList<string> messages, string debugLine)
        {
            Simulation = sim;
            View.Build(sim.Market.Galaxy, Camera.Camera);
            Camera.Configure(View.WorldBounds);
            Hud.Show(sim, Camera.Camera, messages, debugLine);
        }

        /// <summary>Selects a system by id, or clears the selection with -1.</summary>
        public void Select(int systemId)
        {
            View.Select(systemId);
            Hud.ShowSystem(systemId);
        }
    }
}
