using UnityEngine;
using UnityEngine.UIElements;

namespace Karman.UI
{
    /// <summary>
    /// Shared visual language for KÁRMÁN's runtime UI: Inter typeface, translucent dark glass, generous spacing,
    /// small-caps captions over large tabular values, one accent colour. Used by the flight HUD, VAB and menus.
    /// </summary>
    public static class HudStyle
    {
        public static readonly Color Panel = new Color(0.03f, 0.04f, 0.055f, 0.74f);
        public static readonly Color PanelSolid = new Color(0.035f, 0.045f, 0.06f, 0.9f);
        public static readonly Color Rule = new Color(1, 1, 1, 0.08f);
        public static readonly Color Text = new Color(0.94f, 0.96f, 0.98f, 1f);
        public static readonly Color Dim = new Color(0.60f, 0.66f, 0.74f, 1f);
        public static readonly Color Faint = new Color(0.52f, 0.58f, 0.66f, 1f);
        public static readonly Color Accent = new Color(0.38f, 0.80f, 1f, 1f);
        public static readonly Color Good = new Color(0.45f, 0.9f, 0.62f, 1f);
        public static readonly Color Warn = new Color(1f, 0.74f, 0.32f, 1f);
        public static readonly Color Bad = new Color(1f, 0.40f, 0.35f, 1f);

        static Font regular, medium, semibold;
        public static Font Regular => regular ??= Resources.Load<Font>("Fonts/Inter-Regular");
        public static Font Medium => medium ??= Resources.Load<Font>("Fonts/Inter-Medium");
        public static Font SemiBold => semibold ??= Resources.Load<Font>("Fonts/Inter-SemiBold");

        /// <summary>Applies the Inter typeface to a UI root so every child inherits it.</summary>
        public static void ApplyFont(VisualElement root)
        {
            if (Regular != null) root.style.unityFontDefinition = FontDefinition.FromFont(Regular);
        }

        public static T Weight<T>(T e, Font f) where T : VisualElement
        {
            if (f != null) e.style.unityFontDefinition = FontDefinition.FromFont(f);
            e.style.unityFontStyleAndWeight = FontStyle.Normal;
            return e;
        }

        static Texture2D fadeUp, fadeDown;
        /// <summary>Soft dark gradient strip that keeps HUD text legible over bright scenery (sky, concrete, cloud).</summary>
        public static VisualElement Scrim(bool bottom, float height, float alpha)
        {
            Texture2D Make(bool up)
            {
                var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++) { float a = y / 63f; a = up ? 1 - a : a; t.SetPixel(0, y, new Color(0, 0, 0, a * a)); }
                t.Apply(); return t;
            }
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute; e.style.left = 0; e.style.right = 0; e.style.height = height;
            if (bottom) e.style.bottom = 0; else e.style.top = 0;
            e.style.backgroundImage = bottom ? (fadeUp ??= Make(true)) : (fadeDown ??= Make(false));
            e.style.unityBackgroundImageTintColor = new Color(1, 1, 1, alpha);
            return e;
        }

        /// <summary>Thin, arrow-less scrollbar that matches the glass panels.</summary>
        public static void SlimScroll(ScrollView sv)
        {
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var s = sv.verticalScroller;
            s.style.width = 6; s.style.borderLeftWidth = 0;
            s.lowButton.style.display = DisplayStyle.None; s.highButton.style.display = DisplayStyle.None;
            s.slider.style.marginTop = s.slider.style.marginBottom = 0;
            var tracker = s.slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null) { tracker.style.backgroundColor = new Color(1, 1, 1, 0.04f); HudStyle.Border(tracker, 0, Color.clear); Radius(tracker, 3); }
            var drag = s.slider.Q(className: "unity-base-slider__dragger");
            if (drag != null) { drag.style.backgroundColor = new Color(1, 1, 1, 0.22f); Border(drag, 0, Color.clear); Radius(drag, 3); drag.style.width = 6; drag.style.left = 0; }
            s.style.backgroundColor = Color.clear;
        }

        public static void Shadow<T>(T e) where T : VisualElement
        {
            e.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 3, color = new Color(0, 0, 0, 0.75f) };
        }

        public static void Radius(VisualElement v, float r) { v.style.borderTopLeftRadius = v.style.borderTopRightRadius = v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = r; }
        public static void Border(VisualElement v, float w, Color c)
        {
            v.style.borderTopWidth = v.style.borderBottomWidth = v.style.borderLeftWidth = v.style.borderRightWidth = w;
            v.style.borderTopColor = v.style.borderBottomColor = v.style.borderLeftColor = v.style.borderRightColor = c;
        }
        public static void Pad(VisualElement v, float x, float y) { v.style.paddingLeft = v.style.paddingRight = x; v.style.paddingTop = v.style.paddingBottom = y; }

        public static VisualElement PanelBox(float width)
        {
            var v = new VisualElement();
            v.style.backgroundColor = Panel;
            if (width > 0) v.style.width = width;
            Pad(v, 16, 12);
            Radius(v, 14);
            Border(v, 1, new Color(1, 1, 1, 0.07f));
            return v;
        }

        /// <summary>Small-caps section caption.</summary>
        public static Label Title(string text)
        {
            var l = Weight(new Label(text), SemiBold);
            l.style.color = Dim;
            l.style.fontSize = 10;
            l.style.letterSpacing = 1.6f;
            l.style.marginBottom = 6;
            return l;
        }

        public static Label Caption(string text, Color? c = null)
        {
            var l = Weight(new Label(text), Medium);
            l.style.color = c ?? Dim; l.style.fontSize = 10; l.style.letterSpacing = 1.4f;
            Shadow(l);
            return l;
        }

        public static Label Value(string text, float size, Color? c = null)
        {
            var l = Weight(new Label(text), size >= 18 ? SemiBold : Medium);
            l.style.color = c ?? Text; l.style.fontSize = size;
            Shadow(l);
            return l;
        }

        /// <summary>Label/value row. Returns the value label.</summary>
        public static Label Row(VisualElement parent, string name)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 2; row.style.marginBottom = 2;
            var k = new Label(name); k.style.color = Dim; k.style.fontSize = 12;
            var v = Weight(new Label("—"), Medium); v.style.color = Text; v.style.fontSize = 13; v.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(k); row.Add(v);
            parent.Add(row);
            return v;
        }

        public static VisualElement Bar(VisualElement parent, Color fill, out VisualElement fillElement, float height = 4)
        {
            var track = new VisualElement();
            track.style.height = height; track.style.marginTop = 4; track.style.marginBottom = 4;
            track.style.backgroundColor = new Color(1, 1, 1, 0.09f);
            Radius(track, height / 2);
            fillElement = new VisualElement();
            fillElement.style.height = Length.Percent(100);
            fillElement.style.width = Length.Percent(50);
            fillElement.style.backgroundColor = fill;
            Radius(fillElement, height / 2);
            track.Add(fillElement);
            parent.Add(track);
            return track;
        }

        /// <summary>Rounded pill used for modes (SAS, assist, warp) and keyboard hints.</summary>
        public static Label Chip(string text, Color c)
        {
            var l = Weight(new Label(text), SemiBold);
            l.style.fontSize = 10; l.style.letterSpacing = 1.2f; l.style.color = c;
            l.style.backgroundColor = new Color(c.r, c.g, c.b, 0.12f);
            Border(l, 1, new Color(c.r, c.g, c.b, 0.35f));
            Radius(l, 9); Pad(l, 8, 3);
            l.style.marginLeft = 3; l.style.marginRight = 3;
            return l;
        }

        public static void SetChip(Label l, string text, Color c)
        {
            l.text = text; l.style.color = c;
            l.style.backgroundColor = new Color(c.r, c.g, c.b, 0.12f);
            Border(l, 1, new Color(c.r, c.g, c.b, 0.35f));
        }

        public static string Distance(double m)
        {
            double a = System.Math.Abs(m);
            if (double.IsInfinity(m)) return "∞";
            if (a < 10_000) return $"{m:N0} m";
            if (a < 10_000_000) return $"{m / 1000:N1} km";
            if (a < 1e11) return $"{m / 1000:N0} km";
            return $"{m / Karman.Core.Constants.AU:N3} AU";
        }

        public static string Speed(double ms) => System.Math.Abs(ms) < 10_000 ? $"{ms:N1} m/s" : $"{ms / 1000:N2} km/s";

        public static string Duration(double s)
        {
            if (double.IsInfinity(s) || double.IsNaN(s)) return "—";
            var sign = s < 0 ? "-" : "";
            s = System.Math.Abs(s);
            long t = (long)s;
            long d = t / 86400, h = t / 3600 % 24, m = t / 60 % 60, sec = t % 60;
            return d > 0 ? $"{sign}{d}d {h:00}:{m:00}:{sec:00}" : h > 0 ? $"{sign}{h}:{m:00}:{sec:00}" : $"{sign}{m:00}:{sec:00}";
        }
    }
}
