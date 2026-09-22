using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;

namespace Davidmon.UI
{
    /// <summary>
    /// Top-center toast stack for transient messages (evolutions, quest hints, area
    /// changes). Messages fade in, linger, then fade out and are removed automatically.
    /// </summary>
    public sealed class NotificationPanel : MonoBehaviour
    {
        private static readonly Color DefaultColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        private static readonly Color HighlightColor = new Color(1.00f, 0.82f, 0.30f, 1f);

        [SerializeField] private int maxVisible = 4;
        [SerializeField] private float lingerSeconds = 2.2f;
        [SerializeField] private float fadeSeconds = 0.45f;

        private Transform _stackRoot;

        private void Awake()
        {
            UIFactory.EnsureEventSystem();
            Canvas canvas = UIFactory.CreateCanvas("NotificationCanvas", 90);

            var stackGo = new GameObject("ToastStack", typeof(RectTransform));
            RectTransform stackRt = stackGo.GetComponent<RectTransform>();
            stackRt.SetParent(canvas.transform, false);
            stackRt.anchorMin = new Vector2(0.5f, 0.5f);
            stackRt.anchorMax = new Vector2(0.5f, 0.5f);
            stackRt.pivot = new Vector2(0.5f, 1f);
            stackRt.anchoredPosition = new Vector2(0f, 230f);
            stackRt.sizeDelta = new Vector2(900f, 0f);

            var layout = stackGo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;

            _stackRoot = stackRt;
        }

        private void OnEnable()
        {
            GameEvents.ShowNotification += OnShowNotification;
            GameEvents.ShowLegendaryNotification += OnShowLegendary;
        }

        private void OnDisable()
        {
            GameEvents.ShowNotification -= OnShowNotification;
            GameEvents.ShowLegendaryNotification -= OnShowLegendary;
        }

        private void OnShowNotification(string message)
        {
            Spawn(message, DefaultColor);
        }

        private void OnShowLegendary(string message)
        {
            Spawn(message, HighlightColor);
        }

        private void Spawn(string message, Color color)
        {
            if (string.IsNullOrEmpty(message)) return;

            Text text = UIFactory.CreateText(_stackRoot, message, 24, color,
                TextAnchor.MiddleCenter, FontStyle.Bold, "Toast");
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = new Color(color.r, color.g, color.b, 0f);
            text.raycastTarget = false;

            int count = _stackRoot.childCount;
            if (count > maxVisible)
            {
                Transform oldest = _stackRoot.GetChild(0);
                if (oldest != text.transform)
                {
                    Destroy(oldest.gameObject);
                    count--;
                }
            }

            StartCoroutine(Animate(text));
        }

        private IEnumerator Animate(Text text)
        {
            yield return null;
            yield return null;

            float elapsed = 0f;
            while (elapsed < fadeSeconds && text != null)
            {
                elapsed += Time.unscaledDeltaTime;
                text.color = FadeTowards(text.color, 1f, elapsed / fadeSeconds);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(lingerSeconds);

            elapsed = 0f;
            while (elapsed < fadeSeconds && text != null)
            {
                elapsed += Time.unscaledDeltaTime;
                text.color = FadeTowards(text.color, 0f, elapsed / fadeSeconds);
                yield return null;
            }

            if (text != null) Destroy(text.gameObject);
        }

        private static Color FadeTowards(Color from, float targetAlpha, float t)
        {
            return new Color(from.r, from.g, from.b, Mathf.Lerp(from.a, targetAlpha, t));
        }
    }
}