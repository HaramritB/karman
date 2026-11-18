using UnityEngine;
using UnityEngine.UIElements;

namespace Karman.UI
{
    /// <summary>Shared visual language for KÁRMÁN's runtime UI: dark glass panels, thin rules, tabular numerals.</summary>
    public static class HudStyle
    {
        public static readonly Color Panel = new Color(0.035f, 0.045f, 0.06f, 0.72f);
        public static readonly Color Rule = new Color(1, 1, 1, 0.10f);
        public static readonly Color Text = new Color(0.92f, 0.95f, 0.98f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.68f, 0.76f, 1f);
        public static readonly Color Accent = new Color(0.36f, 0.78f, 1f, 1f);
        public static readonly Color Warn = new Color(1f, 0.72f, 0.3f, 1f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.33f, 1f);

        public static VisualElement PanelBox(float width)
        {
            var v = new VisualElement();
            v.style.backgroundColor = Panel;
            v.style.width = width;
            v.style.paddingLeft = 14; v.style.paddingRight = 14; v.style.paddingTop = 10; v.style.paddingBottom = 12;
            v.style.borderTopLeftRadius = 10; v.style.borderTopRightRadius = 10; v.style.borderBottomLeftRadius = 10; v.style.borderBottomRightRadius = 10;
            v.style.borderTopWidth = 1; v.style.borderBottomWidth = 1; v.style.borderLeftWidth = 1; v.style.borderRightWidth = 1;
            var b = new Color(1, 1, 1, 0.08f);
            v.style.borderTopColor = b; v.style.borderBottomColor = b; v.style.borderLeftColor = b; v.style.borderRightColor = b;
            return v;
        }

        public static Label Title(string text)
        {
            var l = new Label(text);
            l.style.color = Accent;
            l.style.fontSize = 11;
            l.style.letterSpacing = 2.2f;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 6;
            return l;
        }

        /// <summary>Label/value row. Returns the value label.</summary>
        public static Label Row(VisualElement parent, string name)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 2; row.style.marginBottom = 2;
            var k = new Label(name); k.style.color = Dim; k.style.fontSize = 13;
            var v = new Label("—"); v.style.color = Text; v.style.fontSize = 14; v.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(k); row.Add(v);
            parent.Add(row);
            return v;
        }

        public static VisualElement Bar(VisualElement parent, Color fill, out VisualElement fillElement)
        {
            var track = new VisualElement();
            track.style.height = 5; track.style.marginTop = 4; track.style.marginBottom = 4;
            track.style.backgroundColor = new Color(1, 1, 1, 0.08f);
            track.style.borderTopLeftRadius = 3; track.style.borderTopRightRadius = 3; track.style.borderBottomLeftRadius = 3; track.style.borderBottomRightRadius = 3;
            fillElement = new VisualElement();
            fillElement.style.height = Length.Percent(100);
            fillElement.style.width = Length.Percent(50);
            fillElement.style.backgroundColor = fill;
            fillElement.style.borderTopLeftRadius = 3; fillElement.style.borderBottomLeftRadius = 3; fillElement.style.borderTopRightRadius = 3; fillElement.style.borderBottomRightRadius = 3;
            track.Add(fillElement);
            parent.Add(track);
            return track;
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
            return d > 0 ? $"{sign}{d}d {h:00}:{m:00}:{sec:00}" : $"{sign}{h:00}:{m:00}:{sec:00}";
        }
    }
}
