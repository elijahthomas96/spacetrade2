using System.Collections.Generic;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Hud;
using SpaceTrade.Game.Settings;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// The galaxy map screen (spec 9.1): stars, ships, routes, backdrop, camera, controls, text, the ship close-up, and the
    /// always-visible bar (spec 9.4), for any galaxy size. Each frame, after the clock has moved and input has been read,
    /// it places the ships from the simulation and then centres a followed ship, before anything is drawn.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class MapScreen : MonoBehaviour
    {
        public GalaxyMapView View { get; private set; }
        public ShipMapView Ships { get; private set; }
        public RouteLinesView Routes { get; private set; }
        public MapCamera Camera { get; private set; }
        public MapHud Hud { get; private set; }
        public MapInput Input { get; private set; }
        public ShipCloseUp CloseUp { get; private set; }
        public TopBar Bar { get; private set; }
        public Simulation Simulation { get; private set; }

        /// <summary>What drives the game's clock. The bar's pause, play, and speed buttons go through it. Set by whoever starts the game.</summary>
        public SimulationRunner Runner { get; set; }

        /// <summary>Where the settings panel keeps the player's settings. Null means they are not written anywhere.</summary>
        public SettingsStore SettingsStore { get; set; }

        public int SelectedSystemId => View.SelectedSystemId;
        public int SelectedShipId { get; private set; } = -1;
        public bool IsFollowing { get; private set; }
        public bool IsCloseUpOpen => CloseUp != null && CloseUp.IsOpen;

        public static MapScreen Create(Camera camera)
        {
            var go = new GameObject("Map");
            var screen = go.AddComponent<MapScreen>();
            screen.Routes = new GameObject("Routes").AddComponent<RouteLinesView>();
            screen.Routes.transform.SetParent(go.transform, false);
            screen.View = new GameObject("Systems").AddComponent<GalaxyMapView>();
            screen.View.transform.SetParent(go.transform, false);
            screen.Ships = new GameObject("Ships").AddComponent<ShipMapView>();
            screen.Ships.transform.SetParent(go.transform, false);
            screen.Camera = camera.GetComponent<MapCamera>();
            if (screen.Camera == null) screen.Camera = camera.gameObject.AddComponent<MapCamera>();
            var background = new GameObject("Background").AddComponent<MapBackground>();
            background.transform.SetParent(go.transform, false);
            background.Build(camera);
            screen.Hud = MapHud.Create(go.transform);
            screen.Hud.Map = screen;
            screen.CloseUp = ShipCloseUp.Create(go.transform);
            screen.CloseUp.CloseRequested += screen.CloseCloseUp;
            screen.Bar = TopBar.Create(go.transform);
            screen.Bar.Map = screen;
            screen.Input = go.AddComponent<MapInput>();
            screen.Input.Map = screen;
            return screen;
        }

        /// <summary>Shows a game, fitted to the whole galaxy, with the starter ship (the lowest ship id) selected (spec 9.1).</summary>
        public void Show(Simulation sim, IReadOnlyList<string> messages, string debugLine)
        {
            Simulation = sim;
            CloseUp.Close();
            SelectedShipId = -1;
            IsFollowing = false;
            View.Build(sim.Market.Galaxy, Camera.Camera);
            Camera.Configure(View.WorldBounds);
            Routes.Build(sim, Camera.Camera);
            Ships.Build(sim, Camera.Camera);
            Hud.Show(sim, Camera.Camera, messages, debugLine);
            Bar.Show(sim);
            if (sim.Ships.Count > 0) SelectShip(sim.Ships[0].Id);
            if (GameAudio.Current != null)
            {
                GameAudio.Current.SetAmbience(null);
                GameAudio.Current.SetMusic(SoundId.MusicMap);
            }
        }

        /// <summary>Selects a system by id, or clears the selection with -1. Either ends follow mode.</summary>
        public void Select(int systemId)
        {
            SelectedShipId = -1;
            Ships.SelectedShipId = -1;
            IsFollowing = false;
            View.Select(systemId);
            Hud.ShowSystem(systemId);
        }

        /// <summary>Selects a ship. Selecting a different ship ends follow mode.</summary>
        public void SelectShip(int shipId)
        {
            if (Simulation?.FindShip(shipId) == null) { Select(-1); return; }
            if (shipId != SelectedShipId) IsFollowing = false;
            View.Select(-1);
            SelectedShipId = shipId;
            Ships.SelectedShipId = shipId;
            Hud.ShowShip(shipId);
        }

        /// <summary>A left click: the nearest ship or star within reach, a ship winning a tie; empty space clears the selection.</summary>
        public void ClickAt(Vector2 screenPoint)
        {
            float reach = Theme.Controls.ClickRadiusPixels;
            int ship = Ships.PickShip(screenPoint, reach, out float shipSq);
            int system = View.PickSystem(screenPoint, reach, out float systemSq);
            if (ship >= 0 && (system < 0 || shipSq <= systemSq))
            {
                SelectShip(ship);
                GameAudio.Play(SoundId.Select, "ship selected on the map");
            }
            else
            {
                Select(system);
                if (system >= 0) GameAudio.Play(SoundId.Select, "system selected on the map");
            }
        }

        /// <summary>
        /// Jumps to a ship from a notification or a list: leaves the close-up, selects the ship, and centres the map on
        /// where it is now, wherever it has gone since. It does not start following it.
        /// </summary>
        public bool JumpToShip(int shipId)
        {
            if (Simulation?.FindShip(shipId) == null) return false;
            if (CloseUp.IsOpen) CloseCloseUp();
            SelectShip(shipId);
            IsFollowing = false;
            Ships.Refresh();
            Camera.SetCentre(ShipMapView.WorldPosition(Simulation.PositionOf(shipId)));
            GameAudio.Play(SoundId.Select, "jumped to a ship");
            return true;
        }

        /// <summary>Jumps to a system: leaves the close-up, selects the system, and centres the map on it.</summary>
        public bool JumpToSystem(int systemId)
        {
            if (Simulation == null || systemId < 0 || systemId >= Simulation.Market.Galaxy.Systems.Count) return false;
            if (CloseUp.IsOpen) CloseCloseUp();
            Select(systemId);
            Camera.SetCentre(GalaxyMapView.WorldPosition(Simulation.Market.Galaxy.Systems[systemId]));
            GameAudio.Play(SoundId.Select, "jumped to a system");
            return true;
        }

        public void SetFollowing(bool on) => IsFollowing = on && SelectedShipId >= 0;

        /// <summary>The player's own follow switch (the F key or the Follow button): it plays the toggle sound.</summary>
        public void ToggleFollow()
        {
            bool before = IsFollowing;
            SetFollowing(!IsFollowing);
            if (IsFollowing != before) GameAudio.Play(IsFollowing ? SoundId.ToggleOn : SoundId.ToggleOff, "follow " + (IsFollowing ? "on" : "off"));
        }

        /// <summary>The player panned by hand: follow mode ends.</summary>
        public void StopFollowing() => IsFollowing = false;

        /// <summary>Opens the close-up of the selected ship, zoomed in fully and following it, so leaving returns to the same ship.</summary>
        public void OpenCloseUp()
        {
            if (SelectedShipId < 0 || Simulation == null) return;
            bool wasOpen = CloseUp.IsOpen;
            Camera.Camera.orthographicSize = Camera.MinSize;
            IsFollowing = true;
            CloseUp.Open(Simulation, SelectedShipId);
            if (!wasOpen) GameAudio.Play(SoundId.PanelOpen, "close-up opened");
        }

        public void CloseCloseUp()
        {
            if (!CloseUp.IsOpen) return;
            CloseUp.Close();
            Camera.Camera.orthographicSize = Camera.MinSize;
            IsFollowing = SelectedShipId >= 0;
            GameAudio.Play(SoundId.PanelClose, "close-up closed");
        }

        private void LateUpdate()
        {
            if (Simulation == null) return;
            Ships.Refresh();
            Routes.Refresh(SelectedShipId);
            if (IsFollowing && Ships.TryGetDrawn(SelectedShipId, out var at)) Camera.SetCentre(at);
        }
    }
}
