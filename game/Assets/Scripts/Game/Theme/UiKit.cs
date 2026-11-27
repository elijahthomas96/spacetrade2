using System;
using SpaceTrade.Game.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Theming
{
    /// <summary>
    /// The one place on-screen controls get their look, all of it from the theme: text, panels, buttons, icons, scroll bars,
    /// and sliders. Screens build their controls here instead of setting colours and sizes themselves. A button made here
    /// also plays its sound: a plain click unless it is given a more specific sound, or none.
    /// </summary>
    public static class UiKit
    {
        private const string SelectedClass = "st-selected";

        /// <summary>Sets the font, body text size, and text colour that everything under a screen's root inherits.</summary>
        public static void Root(VisualElement root)
        {
            root.style.fontSize = Theme.Text.Body;
            root.style.color = Theme.Colours.Text;
            if (Theme.Font != null) root.style.unityFontDefinition = FontDefinition.FromFont(Theme.Font);
        }

        public static Label Label(string text, Color colour, float size)
        {
            var l = new Label(text);
            l.style.color = colour;
            l.style.fontSize = size;
            return l;
        }

        public static Label Body(string text, Color colour) => Label(text, colour, Theme.Text.Body);

        public static Label Title(string text, float size)
        {
            var l = Label(text, Theme.Colours.Text, size);
            if (Theme.Text.TitlesBold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        public static void Padding(VisualElement e, float px) => Padding(e, px, px);

        public static void Padding(VisualElement e, float side, float topAndBottom)
        {
            e.style.paddingLeft = side; e.style.paddingRight = side; e.style.paddingTop = topAndBottom; e.style.paddingBottom = topAndBottom;
        }

        public static void Margin(VisualElement e, float px)
        {
            e.style.marginLeft = px; e.style.marginRight = px; e.style.marginTop = px; e.style.marginBottom = px;
        }

        public static void Border(VisualElement e, float width, Color colour)
        {
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width; e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftColor = colour; e.style.borderRightColor = colour; e.style.borderTopColor = colour; e.style.borderBottomColor = colour;
        }

        public static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        }

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static Image Icon(Sprite sprite, float size, Color tint)
        {
            var image = new Image { sprite = sprite, tintColor = tint, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = size;
            image.style.height = size;
            image.style.flexShrink = 0;
            return image;
        }

        /// <summary>A button with text. <paramref name="sound"/> is what it plays when pressed; null plays nothing (its action makes its own sound).</summary>
        public static Button TextButton(string text, Action onClick, SoundId? sound = SoundId.Click)
        {
            var b = new Button { text = text };
            b.clicked += () =>
            {
                if (sound.HasValue) GameAudio.Play(sound.Value, "button: " + (string.IsNullOrEmpty(b.name) ? b.text : b.name));
                onClick?.Invoke();
            };
            b.style.fontSize = Theme.Text.Body;
            b.style.color = Theme.Colours.ButtonText;
            Border(b, Theme.Panels.ButtonBorderWidth, Theme.Colours.ButtonBorder);
            Hover(b, () => b.ClassListContains(SelectedClass) ? Theme.Colours.ButtonSelected : Theme.Colours.ButtonBackground,
                () => b.ClassListContains(SelectedClass) ? Theme.Colours.ButtonSelected : Theme.Colours.ButtonHover,
                () => Theme.Colours.ButtonPressed);
            return b;
        }

        /// <summary>A button that is only a picture, with no box around it until the mouse is over it.</summary>
        public static Button IconButton(Sprite icon, float size, Action onClick, SoundId? sound = SoundId.Click)
        {
            var b = new Button();
            b.clicked += () =>
            {
                if (sound.HasValue) GameAudio.Play(sound.Value, "button: " + b.name);
                onClick?.Invoke();
            };
            b.text = "";
            b.style.width = size;
            b.style.height = size;
            b.style.flexShrink = 0;
            b.style.backgroundImage = new StyleBackground(icon);
            b.style.unityBackgroundImageTintColor = Theme.Colours.Icon;
            Border(b, 0, Color.clear);
            Margin(b, 0);
            Padding(b, 0);
            Hover(b, () => Color.clear, () => Theme.Colours.IconButtonHover, () => Theme.Colours.IconButtonPressed);
            return b;
        }

        /// <summary>Marks a text button as the chosen one of a group (for example the current speed).</summary>
        public static void SetSelected(Button b, bool selected)
        {
            if (b.ClassListContains(SelectedClass) == selected) return;
            b.EnableInClassList(SelectedClass, selected);
            b.style.backgroundColor = selected ? Theme.Colours.ButtonSelected : Theme.Colours.ButtonBackground;
        }

        public static bool IsSelected(Button b) => b.ClassListContains(SelectedClass);

        private static void Hover(VisualElement e, Func<Color> normal, Func<Color> hover, Func<Color> pressed)
        {
            e.style.backgroundColor = normal();
            bool over = false;
            e.RegisterCallback<PointerEnterEvent>(_ => { over = true; e.style.backgroundColor = hover(); });
            e.RegisterCallback<PointerLeaveEvent>(_ => { over = false; e.style.backgroundColor = normal(); });
            e.RegisterCallback<PointerDownEvent>(_ => e.style.backgroundColor = pressed(), TrickleDown.TrickleDown);
            e.RegisterCallback<PointerUpEvent>(_ => e.style.backgroundColor = over ? hover() : normal(), TrickleDown.TrickleDown);
        }

        /// <summary>A panel's background and inner spacing.</summary>
        public static void Panel(VisualElement e, Color background, float padding)
        {
            e.style.backgroundColor = background;
            Padding(e, padding);
        }

        /// <summary>Gives a scrolling list the theme's scroll bar: a plain track and thumb, no arrow buttons.</summary>
        public static void ScrollBars(VisualElement scrolling)
        {
            scrolling.Query<Scroller>().ForEach(scroller =>
            {
                bool vertical = scroller.direction == SliderDirection.Vertical;
                if (vertical) scroller.style.width = Theme.Panels.ScrollBarWidth;
                else scroller.style.height = Theme.Panels.ScrollBarWidth;
                Border(scroller, 0, Color.clear);
                scroller.style.backgroundColor = Color.clear;
                scroller.Query<RepeatButton>().ForEach(b => b.style.display = DisplayStyle.None);
                var slider = scroller.slider;
                if (vertical) { slider.style.marginTop = 0; slider.style.marginBottom = 0; slider.style.width = Theme.Panels.ScrollBarWidth; }
                else { slider.style.marginLeft = 0; slider.style.marginRight = 0; slider.style.height = Theme.Panels.ScrollBarWidth; }
                StyleSliderParts(slider, Theme.Colours.ScrollTrack, Theme.Colours.ScrollThumb, vertical, Theme.Panels.ScrollBarWidth, -1f, -1f);
            });
        }

        /// <summary>
        /// A box for typing a whole number. The new number is reported when Enter is pressed or the box is left, not on
        /// every keystroke.
        /// </summary>
        public static IntegerField NumberField(int value, float width, Action<int> onChange)
        {
            var f = new IntegerField { value = value, isDelayed = true };
            f.style.width = width;
            f.style.flexShrink = 0;
            Margin(f, 0);
            f.style.fontSize = Theme.Text.Body;
            f.style.color = Theme.Colours.Text;
            var input = f.Q(className: IntegerField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = Theme.Colours.FieldBackground;
                input.style.color = Theme.Colours.Text;
                Border(input, Theme.Panels.ButtonBorderWidth, Theme.Colours.FieldBorder);
                input.style.unityTextAlign = TextAnchor.MiddleRight;
            }
            f.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            return f;
        }

        /// <summary>Greys a button out and stops it taking clicks, or brings it back.</summary>
        public static void SetEnabled(Button b, bool enabled)
        {
            if (b.enabledSelf != enabled) b.SetEnabled(enabled);
        }

        /// <summary>A plain scrolling area with the theme's scroll bar and no sideways scrolling.</summary>
        public static ScrollView Scroll(string name)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = name, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.RegisterCallback<GeometryChangedEvent>(_ => ScrollBars(scroll));
            ScrollBars(scroll);
            return scroll;
        }

        /// <summary>A slider from 0 to <paramref name="max"/> in whole steps.</summary>
        public static SliderInt Slider(int max, int value, Action<int> onChange)
        {
            var s = new SliderInt(0, max) { value = value };
            s.style.flexGrow = 1;
            s.style.height = Theme.Settings.SliderHandleHeight;
            Margin(s, 0);
            StyleSliderParts(s, Theme.Colours.SliderTrack, Theme.Colours.SliderFill, false, Theme.Settings.SliderHeight, Theme.Settings.SliderHandleWidth, Theme.Settings.SliderHandleHeight);
            s.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            return s;
        }

        private static void StyleSliderParts(VisualElement slider, Color track, Color thumb, bool vertical, float thickness, float handleWidth, float handleHeight)
        {
            var tracker = slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = track;
                Border(tracker, 0, Color.clear);
                if (vertical) { tracker.style.width = thickness; tracker.style.left = 0; tracker.style.marginLeft = 0; }
                else { tracker.style.height = thickness; tracker.style.top = (handleHeight > thickness ? (handleHeight - thickness) / 2f : 0); tracker.style.marginTop = 0; }
            }
            var dragger = slider.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = thumb;
                Border(dragger, 0, Color.clear);
                if (vertical) { dragger.style.width = thickness; dragger.style.left = 0; dragger.style.marginLeft = 0; }
                else if (handleWidth > 0f)
                {
                    dragger.style.width = handleWidth;
                    dragger.style.height = handleHeight;
                    dragger.style.top = 0;
                    dragger.style.marginTop = 0;
                }
                else { dragger.style.height = thickness; dragger.style.top = 0; dragger.style.marginTop = 0; }
            }
            var draggerBorder = slider.Q(className: "unity-base-slider__dragger-border");
            if (draggerBorder != null) draggerBorder.style.display = DisplayStyle.None;
        }
    }
}
