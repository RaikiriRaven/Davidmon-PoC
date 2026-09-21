using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Creatures;
using Davidmon.UI;

namespace Davidmon.Combat
{
    /// <summary>
    /// Shared visual helpers for combat: element tints, projectile/tracer materials and
    /// floating damage numbers. All self-building so scenes stay tiny.
    /// </summary>
    public static class CombatFx
    {
        private static Canvas _fxCanvas;
        private static GameObject _runner;
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        public static Color ElementColor(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color(1f, 0.40f, 0.10f);
                case ElementType.Water: return new Color(0.20f, 0.60f, 1f);
                case ElementType.Nature: return new Color(0.30f, 0.90f, 0.40f);
                case ElementType.Electric: return new Color(1f, 0.85f, 0.20f);
                case ElementType.Stone: return new Color(0.65f, 0.52f, 0.34f);
                default: return new Color(0.80f, 0.80f, 0.85f);
            }
        }

        public static Camera CameraEnsure()
        {
            return Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        }

        public static Material TintedMaterial(Color tint)
        {
            if (Materials.TryGetValue(tint, out Material cached) && cached != null) return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { color = tint };
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", tint * 0.8f);
            }
            Materials[tint] = mat;
            return mat;
        }

        public static void EnsureFxRunner()
        {
            if (_fxCanvas != null) return;
            _fxCanvas = UIFactory.CreateCanvas("CombatFxCanvas", 70);
            Object.DontDestroyOnLoad(_fxCanvas.gameObject);

            if (_runner != null) return;
            _runner = new GameObject("CombatFxRunner");
            _runner.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_runner);
        }

        /// <summary>Spawns a short-lived tracer line between two world points.</summary>
        public static void SpawnTracer(Vector3 from, Vector3 to, Color color)
        {
            EnsureFxRunner();

            Vector3 dir = to - from;
            float length = dir.magnitude;
            if (length < 0.01f) return;
            dir /= length;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Tracer";
            go.transform.position = from + dir * (length * 0.5f);
            go.transform.localScale = new Vector3(0.05f, 0.05f, length);
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            go.GetComponent<MeshRenderer>().sharedMaterial = TintedMaterial(color);
            Object.Destroy(go, 0.12f);
        }

        /// <summary>Spawns a quickly expanding impact flash at a world position.</summary>
        public static void SpawnImpact(Vector3 position, Color color)
        {
            EnsureFxRunner();

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Impact";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.08f;
            go.GetComponent<MeshRenderer>().sharedMaterial = TintedMaterial(color);
            var growth = go.AddComponent<ImpactFlash>();
            growth.color = color;
        }

        /// <summary>
        /// Spawns a floating damage number on the overlay CombatFx canvas, projected
        /// from world space each frame so it drifts upwards while fading.
        /// </summary>
        public static void SpawnDamageNumber(Vector3 worldPos, int amount)
        {
            EnsureFxRunner();

            var go = new GameObject("DamageNumber", typeof(RectTransform), typeof(Text));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(_fxCanvas.transform, false);
            rt.sizeDelta = new Vector2(160f, 40f);

            Text label = go.GetComponent<Text>();
            label.font = UIFactory.Font();
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 22;
            label.fontStyle = FontStyle.Bold;
            label.color = amount >= 0 ? new Color(1f, 0.95f, 0.75f, 1f) : new Color(0.4f, 1f, 0.5f, 1f);
            label.text = Mathf.Abs(amount).ToString();
            label.raycastTarget = false;

            var fx = go.AddComponent<DamageNumber>();
            fx.targetWorldPos = worldPos;
            fx.age = 0f;
            fx.life = 0.9f;
        }

        private sealed class ImpactFlash : MonoBehaviour
        {
            public Color color;
            private float _age;

            private void Update()
            {
                _age += Time.deltaTime;
                float t = _age / 0.25f;
                transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.42f, t);
                Renderer r = GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    r.sharedMaterial.color = Color.Lerp(color, Color.white, t) * Mathf.Lerp(1f, 0f, t);
                if (_age >= 0.25f) Destroy(gameObject);
            }
        }

        private sealed class DamageNumber : MonoBehaviour
        {
            public Vector3 targetWorldPos;
            public float age;
            public float life = 0.9f;
            private RectTransform _rt;
            private Text _label;
            private Canvas _canvas;

            private void Awake()
            {
                _rt = GetComponent<RectTransform>();
                _label = GetComponent<Text>();
                _canvas = GetComponentInParent<Canvas>();
            }

            private void Update()
            {
                age += Time.deltaTime;
                float t = Mathf.Clamp01(age / life);

                Camera cam = CombatFx.CameraEnsure();
                Vector3 pos = targetWorldPos + Vector3.up * (age * 1.4f);
                if (cam != null)
                {
                    Vector3 screen = cam.WorldToScreenPoint(pos);
                    Vector2 canvasSize = _canvas != null
                        ? ((RectTransform)_canvas.transform).rect.size
                        : new Vector2(1920f, 1080f);
                    Vector2 anchored = new Vector2(
                        (screen.x - Screen.width * 0.5f) * (canvasSize.x / Screen.width),
                        (screen.y - Screen.height * 0.5f) * (canvasSize.y / Screen.height));
                    _rt.anchoredPosition = anchored;
                }

                Color c = _label.color;
                c.a = 1f - t;
                _label.color = c;

                if (age >= life) Destroy(gameObject);
            }
        }
    }
}