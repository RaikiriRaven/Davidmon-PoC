using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Davidmon.UI
{
    /// <summary>
    /// Small uGUI construction helpers used by every screen. Screens build their own
    /// canvas at runtime, which keeps scenes tiny and lets the whole UI be data/text
    /// driven instead of hand-placed in the editor.
    /// </summary>
    public static class UIFactory
    {
        private static Sprite _whiteSprite;
        private static Font _font;

        public static Sprite WhiteSprite()
        {
            if (_whiteSprite == null)
                _whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            return _whiteSprite;
        }

        public static Font Font()
        {
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        public static Canvas CreateCanvas(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>Full-bleed panel anchored across the whole canvas.</summary>
        public static RectTransform CreateFullPanel(Transform parent, Color color)
        {
            return CreatePanel(parent, Vector2.zero, Vector2.one, 0f, 0f, color);
        }

        public static RectTransform CreatePanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            float offsetMin, float offsetMax, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(offsetMin, offsetMin);
            rt.offsetMax = new Vector2(offsetMax, offsetMax);
            go.GetComponent<Image>().color = color;
            return rt;
        }

        /// <summary>Panel with a centered layout and a fixed pixel size.</summary>
        public static RectTransform CreateCenteredPanel(Transform parent, Vector2 size, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        public static Text CreateText(Transform parent, string content, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, string name = "Text")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = Font();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button CreateButton(Transform parent, Vector2 size, string label, int fontSize,
            Action onClick, Color buttonColor, Color labelColor, string name = "Button")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            go.GetComponent<Image>().sprite = WhiteSprite();
            go.GetComponent<Image>().color = buttonColor;

            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            var col = button.colors;
            col.highlightedColor = buttonColor * 1.2f;
            col.pressedColor = buttonColor * 0.85f;
            button.colors = col;
            button.onClick.AddListener(() => onClick?.Invoke());

            CreateText(rt, label, fontSize, labelColor, TextAnchor.MiddleCenter, FontStyle.Bold, "Label");
            return button;
        }
    }
}