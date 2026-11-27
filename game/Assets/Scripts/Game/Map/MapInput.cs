using SpaceTrade.Game.Theming;
using SpaceTrade.Sim.Timing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// Map controls (spec 9.5): hold right or middle mouse and drag, or WASD / arrow keys, to pan; mouse wheel to zoom;
    /// left click to select a system or ship (empty space clears it); F to follow the selected ship; Space pauses and
    /// resumes; 1, 2, 3 choose 1x, 3x, 5x. Esc closes the open list or settings panel first, then the planner or receipt,
    /// then the close-up, then clears the selection. While a number is being typed into the planner, the keys go to it. Panning by hand ends follow mode; zooming while following zooms around the ship. Wheeling in
    /// at the closest zoom with a ship selected opens its close-up; wheeling out or Esc closes it.
    /// How each control feels (click reach, zoom step, pan speed) comes from the theme's "controls" section.
    /// </summary>
    public sealed class MapInput : MonoBehaviour
    {
        /// <summary>How close to a star or ship, in screen pixels, a click must be to select it.</summary>
        public static float PickPixels => Theme.Controls.ClickRadiusPixels;

        /// <summary>A wheel reading this large or larger is in Windows wheel units (120 a notch), not notches.</summary>
        private const float WheelUnitsThreshold = 10f;
        private const float WheelUnitsPerNotch = 120f;

        public MapScreen Map;

        /// <summary>Devices to read; null means the current mouse and keyboard. Tests set their own.</summary>
        public Mouse MouseDevice { get; set; }
        public Keyboard KeyboardDevice { get; set; }

        private Vector2? _dragFrom;

        private void Update()
        {
            if (Map == null || Map.View.Galaxy == null) return;
            var mouse = MouseDevice ?? Mouse.current;
            var keyboard = KeyboardDevice ?? Keyboard.current;
            var cam = Map.Camera;
            var controls = Theme.Controls;

            bool esc = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            // Typing a load into the planner: digits, letters, and Space belong to the number box, not to the map.
            if (keyboard != null && Map.Planner != null && Map.Panels.IsShowing(Map.Planner) && Map.Planner.IsTyping) keyboard = null;
            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) Map.Bar.TogglePause();
                if (keyboard.digit1Key.wasPressedThisFrame) Map.Bar.ChooseSpeed(ClockSpeed.X1);
                if (keyboard.digit2Key.wasPressedThisFrame) Map.Bar.ChooseSpeed(ClockSpeed.X3);
                if (keyboard.digit3Key.wasPressedThisFrame) Map.Bar.ChooseSpeed(ClockSpeed.X5);
            }
            if (esc && Map.Bar.CloseDropdowns()) esc = false;
            if (esc && Map.Panels.CloseTop()) esc = false;

            if (Map.IsCloseUpOpen)
            {
                _dragFrom = null;
                Map.Hud.HoveredShipId = -1;
                bool overBar = mouse != null && Map.Bar.IsOver(mouse.position.ReadValue());
                bool wheelOut = mouse != null && !overBar && mouse.scroll.ReadValue().y < 0f;
                if (wheelOut || esc) Map.CloseCloseUp();
                return;
            }

            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                bool overPanel = Map.Hud.IsOverPanel(pos);

                Map.Hud.HoveredSystemId = overPanel ? -1 : Map.View.PickSystem(pos, controls.ClickRadiusPixels);
                Map.Hud.HoveredShipId = overPanel ? -1 : Map.Ships.PickShip(pos, controls.ClickRadiusPixels, out _);

                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !overPanel)
                {
                    bool atClosest = cam.Camera.orthographicSize <= cam.MinSize + 1e-4f;
                    if (scroll > 0f && atClosest && Map.SelectedShipId >= 0) Map.OpenCloseUp();
                    else
                    {
                        float notches = Mathf.Abs(scroll) >= WheelUnitsThreshold ? scroll / WheelUnitsPerNotch : scroll;
                        float most = controls.MaxWheelNotchesPerFrame;
                        Vector2 around = Map.IsFollowing ? (Vector2)cam.Camera.WorldToScreenPoint(cam.Centre) : pos;
                        cam.ZoomAt(Mathf.Pow(controls.ZoomPerWheelNotch, Mathf.Clamp(notches, -most, most)), around);
                    }
                }

                bool dragging = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                if (dragging)
                {
                    if (_dragFrom.HasValue && pos != _dragFrom.Value)
                    {
                        Map.StopFollowing();
                        cam.DragPixels(pos - _dragFrom.Value);
                    }
                    _dragFrom = pos;
                }
                else _dragFrom = null;

                if (mouse.leftButton.wasPressedThisFrame && !overPanel) Map.ClickAt(pos);
            }

            if (keyboard != null)
            {
                Vector2 dir = Vector2.zero;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) dir.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) dir.x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) dir.y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) dir.y += 1f;
                if (dir != Vector2.zero)
                {
                    Map.StopFollowing();
                    cam.PanWorld(dir.normalized * (2f * cam.Camera.orthographicSize * controls.KeyPanScreensPerSecond * Time.unscaledDeltaTime));
                }
                if (keyboard.fKey.wasPressedThisFrame) Map.ToggleFollow();
                if (esc) Map.Select(-1);
            }
        }
    }
}
