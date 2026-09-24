using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Combat;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Player;

namespace Davidmon.UI
{
    /// <summary>
    /// Evolution menu. A persistent "EVOLUTIONS" button opens a panel listing every
    /// evolution stage of the active creature. Stages whose level (or item) requirement
    /// has not been met are shown as blacked-out packages with an unknown name; unlocked
    /// stages reveal the target creature and an Evolve button that plays a brief white
    /// flash before swapping species.
    /// </summary>
    public sealed class EvolutionUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.11f, 0.97f);
        private static readonly Color CardColor = new Color(0.13f, 0.16f, 0.22f, 1f);
        private static readonly Color AccentColor = new Color(0.95f, 0.65f, 0.15f, 1f);
        private static readonly Color TextColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.65f, 0.70f, 1f);
        private static readonly Color LockedColor = new Color(0.04f, 0.04f, 0.06f, 1f);
        private static readonly Color LockedSymbolColor = new Color(0.35f, 0.35f, 0.38f, 1f);

        private readonly Dictionary<ElementType, Sprite> _iconCache = new Dictionary<ElementType, Sprite>();

        private PlayerManager _playerManager;
        private GameObject _panelRoot;
        private RectTransform _cardsRoot;
        private Text _headerText;
        private Image _flash;
        private bool _busy;
        private CanvasScaler _scaler;
        private float _cardWidth = 280f;
        private float _cardGap = 30f;

        private void Awake()
        {
            _playerManager = ServiceLocator.Get<PlayerManager>();
            if (_playerManager == null) _playerManager = FindAnyObjectByType<PlayerManager>();

            UIFactory.EnsureEventSystem();
            Build();
        }

        private void OnEnable()
        {
            GameEvents.ExpGained += OnExpGained;
            GameEvents.LevelUp += OnLevelUp;
            GameEvents.CreatureEvolved += OnEvolved;
            GameEvents.CreatureSelected += OnSelected;
            GameEvents.EscapeRequested += OnEscapeRequested;
        }

        private void OnDisable()
        {
            GameEvents.ExpGained -= OnExpGained;
            GameEvents.LevelUp -= OnLevelUp;
            GameEvents.CreatureEvolved -= OnEvolved;
            GameEvents.CreatureSelected -= OnSelected;
            GameEvents.EscapeRequested -= OnEscapeRequested;
        }

        private void OnEscapeRequested()
        {
            if (_panelRoot != null && _panelRoot.activeSelf) ClosePanel();
        }

        /// <summary>Opens the panel when closed, closes it when open.</summary>
        public void TogglePanel()
        {
            if (_busy) return;
            if (_panelRoot != null && _panelRoot.activeSelf) ClosePanel();
            else OpenPanel();
        }

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("EvolutionCanvas", 80);
            _scaler = canvas.GetComponent<CanvasScaler>();

            Button toggle = UIFactory.CreateButton(canvas.transform, new Vector2(280f, 64f),
                "EVOLUTIONS", 27, TogglePanel, new Color(0.24f, 0.17f, 0.05f, 0.98f), AccentColor, "EvolutionsButton");
            RectTransform toggleRt = (RectTransform)toggle.transform;
            toggleRt.anchorMin = new Vector2(0.5f, 0f);
            toggleRt.anchorMax = new Vector2(0.5f, 0f);
            toggleRt.pivot = new Vector2(0.5f, 0f);
            toggleRt.anchoredPosition = new Vector2(0f, 116f);

            RectTransform panel = UIFactory.CreateFullPanel(canvas.transform, PanelColor);
            panel.gameObject.name = "EvolutionPanel";
            _panelRoot = panel.gameObject;

            UIFactory.CreateText(panel, "EVOLUTIONS", 58, AccentColor,
                TextAnchor.UpperCenter, FontStyle.Bold, "Title").rectTransform.anchoredPosition = new Vector2(0f, -40f);

            _headerText = UIFactory.CreateText(panel, "", 28, TextColor,
                TextAnchor.UpperCenter, FontStyle.Normal, "Header");
            _headerText.rectTransform.anchoredPosition = new Vector2(0f, -100f);

            RectTransform listArea = UIFactory.CreateCenteredPanel(panel, new Vector2(1600f, 600f), Color.clear);
            _cardsRoot = listArea;

            Button close = UIFactory.CreateButton(panel, new Vector2(220f, 60f), "CLOSE", 24,
                ClosePanel, new Color(0.16f, 0.16f, 0.18f, 1f), TextColor, "CloseButton");
            ((RectTransform)close.transform).anchoredPosition = new Vector2(0f, -400f);

            _flash = UIFactory.CreateFullPanel(panel, new Color(1f, 1f, 1f, 0f)).GetComponent<Image>();
            _flash.gameObject.name = "Flash";
            _flash.raycastTarget = false;
            _flash.gameObject.SetActive(false);

            _panelRoot.SetActive(false);
        }

        private void OpenPanel()
        {
            _panelRoot.SetActive(true);
            PlayerInputProvider.LocalOrAny()?.SetMovementLocked(true);
            Core.CursorManager.Current?.RequestModal();
            RefreshHeader();
            Rebuild();
        }

        private void ClosePanel()
        {
            if (_busy) return;
            _panelRoot.SetActive(false);
            PlayerInputProvider.LocalOrAny()?.SetMovementLocked(false);
            Core.CursorManager.Current?.ReleaseModal();
        }

        private void RefreshHeader()
        {
            CreatureInstance inst = ActiveInstance();
            _headerText.text = inst != null && inst.Data != null
                ? inst.Data.DisplayName + "   \u00b7   Lv." + inst.Level
                : "No creature selected.";
        }

        private void Rebuild()
        {
            for (int i = _cardsRoot.childCount - 1; i >= 0; i--)
                Destroy(_cardsRoot.GetChild(i).gameObject);

            CreatureInstance inst = ActiveInstance();
            if (inst == null || inst.Data == null)
            {
                CreateMessage(inst != null && inst.Data != null ? null : "No creature selected yet.");
                return;
            }

            var stages = inst.Data.EvolutionStages;
            if (stages == null || stages.Count == 0)
            {
                CreateMessage(inst.Data.DisplayName + " is in its final form \u2014 no further evolutions.");
                return;
            }

            UpdateCardLayout(stages.Count);
            for (int i = 0; i < stages.Count; i++)
                CreateCard(stages[i], i, stages.Count);
        }

        private void UpdateCardLayout(int count)
        {
            _cardWidth = 280f;
            _cardGap = 30f;
            if (count <= 1) return;

            float refW = _scaler != null ? _scaler.referenceResolution.x : 1920f;
            float refH = _scaler != null ? _scaler.referenceResolution.y : 1080f;
            float match = _scaler != null ? _scaler.matchWidthOrHeight : 0.5f;

            float sx = Screen.width / refW;
            float sy = Screen.height / refH;
            float scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(sx), Mathf.Log(sy), match));
            if (scale < 0.001f) scale = 1f;

            float available = (Screen.width * 0.96f) / scale - 160f;
            float total = count * 280f + (count - 1) * 30f;
            if (total <= available) return;

            for (float w = 260f; w >= 150f; w -= 10f)
            {
                float gap = Mathf.Max(12f, Mathf.Min(30f, (available - count * w) / (count - 1)));
                if (count * w + (count - 1) * gap <= available)
                {
                    _cardWidth = w;
                    _cardGap = gap;
                    return;
                }
            }
            _cardWidth = 150f;
            _cardGap = 12f;
        }

        private void CreateMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            UIFactory.CreateText(_cardsRoot, text, 26, MutedColor,
                TextAnchor.MiddleCenter, FontStyle.Normal, "EmptyText");
        }

        private void CreateCard(EvolutionStage stage, int index, int count)
        {
            CreatureInstance inst = ActiveInstance();
            PlayerManager effective = EffectiveManager();
            bool unlocked = inst != null && effective != null && effective.IsEvolutionUnlocked(stage);
            CreatureData target = stage.nextCreature;
            string targetName = target != null ? target.DisplayName : "Unknown";

            RectTransform card = CreateCardBox(index, count);

            float iconSize = Mathf.Min(216f, _cardWidth - 28f);
            Image icon = CreateImage(card, new Vector2(iconSize, iconSize), new Vector2(0f, 80f));
            icon.sprite = IconFor(target);
            icon.color = unlocked ? Color.white : LockedColor;

            UIFactory.CreateText(icon.rectTransform,
                unlocked ? Monogram(targetName) : "?",
                unlocked ? (int)Mathf.Min(64f, iconSize * 0.3f) : (int)Mathf.Min(74f, iconSize * 0.34f),
                unlocked ? new Color(1f, 1f, 1f, 0.85f) : LockedSymbolColor,
                TextAnchor.MiddleCenter, FontStyle.Bold, "Monogram");

            int nameSize = _cardWidth >= 240 ? 30 : (_cardWidth >= 180 ? 24 : 18);
            UIFactory.CreateText(card, unlocked ? targetName : "???", nameSize,
                unlocked ? TextColor : MutedColor, TextAnchor.MiddleCenter, FontStyle.Bold, "Name")
                .rectTransform.anchoredPosition = new Vector2(0f, -60f);

            string requirement = "Reach Lv. " + stage.requiredLevel;
            if (!string.IsNullOrEmpty(stage.requiredItemId))
                requirement += "  +  " + stage.requiredItemId;
            if (unlocked) requirement = "Ready \u00b7 Lv. " + stage.requiredLevel;

            UIFactory.CreateText(card, requirement, 20, unlocked ? AccentColor : MutedColor,
                TextAnchor.MiddleCenter, FontStyle.Normal, "Req")
                .rectTransform.anchoredPosition = new Vector2(0f, -96f);

            Button evolve = UIFactory.CreateButton(card, new Vector2(Mathf.Min(170f, _cardWidth - 40f), 52f), "EVOLVE", 22,
                () => Evolve(stage), AccentColor, new Color(0.10f, 0.07f, 0.02f, 1f), "EvolveButton");
            ((RectTransform)evolve.transform).anchoredPosition = new Vector2(0f, -160f);
            evolve.interactable = unlocked && !_busy;
        }

        private void Evolve(EvolutionStage stage)
        {
            if (_busy || stage == null || stage.nextCreature == null) return;
            if (EffectiveManager() == null || !EffectiveManager().IsEvolutionUnlocked(stage)) return;
            // Online: server validates requirements (TryEvolve); the new species
            // arrives via CreatureId mirror. Never apply EvolveTo locally online.
            if (Davidmon.Multiplayer.ServerApi.IsOnline)
            {
                StartCoroutine(RunEvolutionOnline(stage.nextCreature));
                return;
            }
            StartCoroutine(RunEvolution(stage.nextCreature));
        }

        /// <summary>Online evolution: flash, request, then refresh from the mirror.</summary>
        private IEnumerator RunEvolutionOnline(CreatureData next)
        {
            _busy = true;
            SetButtonsInteractable(false);

            _flash.gameObject.SetActive(true);
            yield return FadeFlash(1f, 0.18f);
            Davidmon.Multiplayer.ServerApi.RequestEvolve(next.CreatureId);
            yield return FadeFlash(0f, 0.35f);
            _flash.gameObject.SetActive(false);

            _busy = false;
            SetButtonsInteractable(true);
            RefreshHeader();
            Rebuild();
        }

        private IEnumerator RunEvolution(CreatureData next)
        {
            _busy = true;
            SetButtonsInteractable(false);

            _flash.gameObject.SetActive(true);
            yield return FadeFlash(1f, 0.18f);
            EffectiveManager()?.EvolveTo(next);
            yield return FadeFlash(0f, 0.35f);
            _flash.gameObject.SetActive(false);

            _busy = false;
            SetButtonsInteractable(true);
            RefreshHeader();
            Rebuild();
        }

        private IEnumerator FadeFlash(float alpha, float seconds)
        {
            float elapsed = 0f;
            Color start = _flash.color;
            Color target = new Color(1f, 1f, 1f, alpha);
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _flash.color = Color.Lerp(start, target, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }
            _flash.color = target;
        }

        private void SetButtonsInteractable(bool value)
        {
            foreach (Button b in GetComponentsInChildren<Button>(true)) b.interactable = value;
        }

        private CreatureInstance ActiveInstance()
        {
            // Prefer the locally controlled avatar (multiplayer-safe).
            PlayerManager effective = EffectiveManager();
            if (effective != null && effective.HasCreature) return effective.ActiveCreature;
            PlayerManager pm = ServiceLocator.Get<PlayerManager>();
            return pm != null && pm.HasCreature ? pm.ActiveCreature : null;
        }

        /// <summary>Locally controlled manager (multiplayer-safe), scene fallback.</summary>
        private PlayerManager EffectiveManager()
        {
            PlayerManager local = Davidmon.Multiplayer.PlayerLookup.LocalManager();
            if (local != null) return local;
            if (_playerManager != null) return _playerManager;
            return ServiceLocator.Get<PlayerManager>();
        }

        private void OnExpGained(int creatureId, long gained, long total)
        {
            if (_panelRoot != null && _panelRoot.activeSelf) RefreshHeader();
        }

        private void OnLevelUp(int creatureId, long newLevel, long maxHp)
        {
            if (_panelRoot != null && _panelRoot.activeSelf)
            {
                RefreshHeader();
                Rebuild();
            }
        }

        private void OnEvolved(string fromId, string toId)
        {
            OnCreatureChanged(toId);
        }

        private void OnSelected(string creatureId)
        {
            OnCreatureChanged(creatureId);
        }

        private void OnCreatureChanged(string id)
        {
            if (_panelRoot != null && _panelRoot.activeSelf)
            {
                RefreshHeader();
                Rebuild();
            }
        }

        private Sprite IconFor(CreatureData data)
        {
            if (data != null && data.Icon != null) return data.Icon;
            ElementType element = data != null ? data.Element : ElementType.Neutral;
            if (_iconCache.TryGetValue(element, out Sprite cached) && cached != null) return cached;

            Sprite sprite = BuildElementIcon(element);
            _iconCache[element] = sprite;
            return sprite;
        }

        private static Sprite BuildElementIcon(ElementType element)
        {
            Color tint = CombatFx.ElementColor(element);
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    bool border = Mathf.Abs(dx) > 0.9f || Mathf.Abs(dy) > 0.9f;
                    Color c = border ? tint * 0.45f : Color.Lerp(tint, tint * 1.15f, 0.5f);
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        private static string Monogram(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "?";
            string[] words = displayName.Split(' ');
            string result = words.Length >= 2
                ? "" + words[0][0] + words[1][0]
                : displayName.Substring(0, 1);
            return result.ToUpperInvariant();
        }

        private RectTransform CreateCardBox(int index, int count)
        {
            var go = new GameObject("EvoCard", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(_cardsRoot, false);
            rt.sizeDelta = new Vector2(_cardWidth, 400f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((index - (count - 1) * 0.5f) * (_cardWidth + _cardGap), 0f);
            Image img = go.GetComponent<Image>();
            img.sprite = UIFactory.WhiteSprite();
            img.color = CardColor;
            return rt;
        }

        private static Image CreateImage(RectTransform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            Image img = go.GetComponent<Image>();
            img.sprite = UIFactory.WhiteSprite();
            img.color = Color.white;
            return img;
        }
    }
}