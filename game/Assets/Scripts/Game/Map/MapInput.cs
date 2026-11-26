using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// Map controls (spec 9.5): hold right or middle mouse and drag, or WASD / arrow keys, to pan; mouse wheel to zoom;
    /// left click to select a system (empty space clears it); Esc clears the selection.
    /// </summary>
    public sealed class MapInput : MonoBehaviour
    {
        /// <summary>How close to a star, in screen pixels, a click must be to select it.</summary>
        public const float PickPixels = 24f;
        /// <summary>Keyboard pan speed, in screen heights per real second.</summary>
        public const float KeyPanScreensPerSecond = 0.8f;
        /// <summary>Zoom factor per mouse-wheel notch.</summary>
        public const float ZoomPerNotch = 0.85f;

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

            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                bool overPanel = Map.Hud.IsOverPanel(pos);

                Map.Hud.HoveredSystemId = overPanel ? -1 : Map.View.PickSystem(pos, PickPixels);

                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !overPanel)
                {
                    float notches = Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;
                    cam.ZoomAt(Mathf.Pow(ZoomPerNotch, Mathf.Clamp(notches, -5f, 5f)), pos);
                }

                bool dragging = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                if (dragging)
                {
                    if (_dragFrom.HasValue) cam.DragPixels(pos - _dragFrom.Value);
                    _dragFrom = pos;
                }
                else _dragFrom = null;

                if (mouse.leftButton.wasPressedThisFrame && !overPanel) Map.Select(Map.View.PickSystem(pos, PickPixels));
            }

            if (keyboard != null)
            {
                Vector2 dir = Vector2.zero;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) dir.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) dir.x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) dir.y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) dir.y += 1f;
                if (dir != Vector2.zero)
                    cam.PanWorld(dir.normalized * (2f * cam.Camera.orthographicSize * KeyPanScreensPerSecond * Time.unscaledDeltaTime));
                if (keyboard.escapeKey.wasPressedThisFrame) Map.Select(-1);
            }
        }
    }
}
