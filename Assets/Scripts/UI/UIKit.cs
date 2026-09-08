// -----------------------------------------------------------------------------
// UIKit — uGUI widgets assembled in code.
//
// The whole interface is built at runtime for the same reason the world is: a
// Canvas laid out in the Editor is a .unity file full of GUID references that
// cannot be authored blind. Building it here means the layout is reviewable as
// code, and there are no unassigned inspector slots to go wrong.
//
// Legacy UnityEngine.UI rather than TextMeshPro: TMP needs its imported font
// assets and a first-run import step. This project has no imported assets.
// -----------------------------------------------------------------------------

using LochNess.Boot;
using UnityEngine;
using UnityEngine.UI;

namespace LochNess.UI
{
    public static class UIKit
    {
        // A single palette, used everywhere. Peat, brass and phosphor: the colours of
        // a working boat's wheelhouse at dusk.
        public static readonly Color Abyss = new Color(0.043f, 0.078f, 0.086f, 1f);
        public static readonly Color Hull = new Color(0.086f, 0.137f, 0.161f, 0.94f);
        public static readonly Color Rule = new Color(0.165f, 0.247f, 0.286f, 1f);
        public static readonly Color Brass = new Color(0.784f, 0.569f, 0.184f, 1f);
        public static readonly Color Phosphor = new Color(0.498f, 0.831f, 0.757f, 1f);
        public static readonly Color Alarm = new Color(0.851f, 0.333f, 0.231f, 1f);
        public static readonly Color Mist = new Color(0.624f, 0.702f, 0.722f, 1f);
        public static readonly Color Paper = new Color(0.910f, 0.894f, 0.851f, 1f);

        public static RectTransform Panel(Transform parent, string name, Color colour,
                                          Vector2 anchorMin, Vector2 anchorMax,
                                          Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = colour.a > 0.01f;
            return rect;
        }

        /// <summary>A full-screen stretched panel — the base of each menu page.</summary>
        public static RectTransform FullScreen(Transform parent, string name, Color colour) =>
            Panel(parent, name, colour, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        public static Text Label(Transform parent, string name, string content, int size, Color colour,
                                 TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax,
                                 Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = go.GetComponent<Text>();
            text.font = Fonts.Builtin;
            text.text = content;
            text.fontSize = size;
            text.color = colour;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static UnityEngine.UI.Button Button(Transform parent, string name, string caption,
                                    Vector2 anchorMin, Vector2 anchorMax,
                                    Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rect = Panel(parent, name, Rule, anchorMin, anchorMax, offsetMin, offsetMax);

            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            var image = rect.GetComponent<Image>();
            button.targetGraphic = image;

            // Explicit colour states: the default white tint is invisible against this
            // palette, so a button would give no feedback at all.
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colours.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colours.selectedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colours.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colours;

            Label(rect, "Caption", caption, 22, Paper, TextAnchor.MiddleCenter,
                  Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return button;
        }

        public static UnityEngine.UI.InputField Field(Transform parent, string name, string placeholder, string value,
                                       Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rect = Panel(parent, name, Abyss, anchorMin, anchorMax, offsetMin, offsetMax);

            Text text = Label(rect, "Text", value, 20, Paper, TextAnchor.MiddleLeft,
                              Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
            text.supportRichText = false;

            Text hint = Label(rect, "Placeholder", placeholder, 20, new Color(0.45f, 0.5f, 0.52f),
                              TextAnchor.MiddleLeft, Vector2.zero, Vector2.one,
                              new Vector2(12f, 0f), new Vector2(-12f, 0f));

            var field = rect.gameObject.AddComponent<UnityEngine.UI.InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.text = value;
            field.targetGraphic = rect.GetComponent<Image>();
            return field;
        }

        /// <summary>
        /// A slider, hand-assembled. uGUI's slider needs a fill rect and a handle rect
        /// wired to the component; the Editor's "Create > Slider" menu item does
        /// exactly this, and this is that menu item written out.
        /// </summary>
        public static UnityEngine.UI.Slider Slider(Transform parent, string name, float min, float max, float value,
                                    Vector2 anchorMin, Vector2 anchorMax,
                                    Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform root = Panel(parent, name, new Color(0f, 0f, 0f, 0f),
                                       anchorMin, anchorMax, offsetMin, offsetMax);

            RectTransform track = Panel(root, "Track", Abyss,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(0f, 4f));

            RectTransform fillArea = Panel(track, "Fill Area", new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform fill = Panel(fillArea, "Fill", Brass,
                Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            RectTransform handleArea = Panel(root, "Handle Area", new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, new Vector2(9f, 0f), new Vector2(-9f, 0f));
            RectTransform handle = Panel(handleArea, "Handle", Paper,
                Vector2.zero, new Vector2(0f, 1f), new Vector2(-9f, -9f), new Vector2(9f, 9f));

            // Fully qualified: inside this method the bare name `Slider` binds to the
            // method itself, not to the uGUI type.
            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            return slider;
        }

        public static UnityEngine.UI.Toggle Toggle(Transform parent, string name, string caption, bool value,
                                    Vector2 anchorMin, Vector2 anchorMax,
                                    Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform root = Panel(parent, name, new Color(0f, 0f, 0f, 0f),
                                       anchorMin, anchorMax, offsetMin, offsetMax);

            RectTransform box = Panel(root, "Box", Abyss,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -13f), new Vector2(26f, 13f));
            RectTransform tick = Panel(box, "Tick", Brass,
                Vector2.zero, Vector2.one, new Vector2(5f, 5f), new Vector2(-5f, -5f));

            Label(root, "Caption", caption, 19, Mist, TextAnchor.MiddleLeft,
                  new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(38f, 0f), Vector2.zero);

            var toggle = root.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = tick.GetComponent<Image>();
            toggle.isOn = value;
            return toggle;
        }

        /// <summary>A one-pixel horizontal rule, for separating blocks of readout.</summary>
        public static void Divider(Transform parent, float yFromTop, float inset = 0f)
        {
            Panel(parent, "Rule", Rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                  new Vector2(inset, -yFromTop - 1f), new Vector2(-inset, -yFromTop));
        }
    }
}
