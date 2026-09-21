using System;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Combat;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Player;

namespace Davidmon.UI
{
    /// <summary>
    /// Persistent gameplay HUD. Shows the active creature's name, species, level and
    /// HP/EXP bars, rebuilt whenever a progression event fires. Self-constructs its
    /// canvas at runtime so scenes stay tiny.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private static readonly Color HpFillColor = new Color(0.22f, 0.80f, 0.32f, 0.95f);
        private static readonly Color ExpFillColor = new Color(0.24f, 0.55f, 1.00f, 0.95f);
        private static readonly Color BarBgColor = new Color(0.08f, 0.08f, 0.10f, 0.85f);
        private static readonly Color PanelColor = new Color(0.06f, 0.06f, 0.09f, 0.62f);
        private static readonly Color TitleColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        private static readonly Color MutedColor = new Color(0.70f, 0.70f, 0.72f, 1f);

        [SerializeField] private PlayerManager playerManager;

        private Text _nameText;
        private Text _subText;
        private Text _hpText;
        private Text _expText;
        private Image _hpFill;
        private Image _expFill;

        private const int AbilitySlots = 4;
        private AbilityCaster _caster;
        private Image[] _slotBgs = new Image[AbilitySlots];
        private Image[] _slotCds = new Image[AbilitySlots];
        private Text[] _slotNums = new Text[AbilitySlots];
        private Text[] _slotNames = new Text[AbilitySlots];
        private static readonly Color SlotIdleColor = new Color(0.09f, 0.09f, 0.12f, 0.80f);
        private static readonly Color SlotReadyColor = new Color(0.16f, 0.20f, 0.28f, 0.92f);
        private static readonly Color SlotSelectedColor = new Color(0.85f, 0.72f, 0.30f, 1f);
        private static readonly Color CdOverlayColor = new Color(0f, 0f, 0f, 0.62f);

        private void Awake()
        {
            if (playerManager == null) playerManager = ServiceLocator.Get<PlayerManager>();
            UIFactory.EnsureEventSystem();

            Canvas canvas = UIFactory.CreateCanvas("HudCanvas", 40);
            RectTransform panel = UIFactory.CreatePanel(canvas.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), 0f, 0f, PanelColor);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 0f);
            panel.pivot = new Vector2(0f, 0f);
            panel.anchoredPosition = new Vector2(24f, 24f);
            panel.sizeDelta = new Vector2(440f, 168f);
            SetRaycastTarget(panel.gameObject, false);

            _nameText = UIFactory.CreateText(panel, "No creature", 30, TitleColor,
                TextAnchor.UpperLeft, FontStyle.Bold, "Name");
            _nameText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _nameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _nameText.rectTransform.pivot = new Vector2(0f, 1f);
            _nameText.rectTransform.anchoredPosition = new Vector2(16f, -10f);
            _nameText.rectTransform.sizeDelta = Vector2.zero;
            _nameText.raycastTarget = false;

            _subText = UIFactory.CreateText(panel, "", 18, MutedColor,
                TextAnchor.UpperLeft, FontStyle.Normal, "Sub");
            _subText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _subText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _subText.rectTransform.pivot = new Vector2(0f, 1f);
            _subText.rectTransform.anchoredPosition = new Vector2(16f, -44f);
            _subText.rectTransform.sizeDelta = Vector2.zero;
            _subText.raycastTarget = false;

            Image hpBg = CreateBar(panel, new Vector2(408f, 22f), new Vector2(16f, -72f), 0.62f, HpFillColor, out _hpFill, out _hpText);
            Image expBg = CreateBar(panel, new Vector2(408f, 22f), new Vector2(16f, -100f), 0.62f, ExpFillColor, out _expFill, out _expText);

            BuildAbilityBar(canvas.transform);
        }

        private void OnEnable()
        {
            GameEvents.ExpGained += OnRefresh;
            GameEvents.LevelUp += OnRefresh;
            GameEvents.CreatureEvolved += OnEvolved;
            GameEvents.CreatureSelected += OnSelected;
            GameEvents.AbilitySelected += OnAbilitySelected;
            GameEvents.CreatureHpChanged += OnCreatureHpChanged;
        }

        private void OnDisable()
        {
            GameEvents.ExpGained -= OnRefresh;
            GameEvents.LevelUp -= OnRefresh;
            GameEvents.CreatureEvolved -= OnEvolved;
            GameEvents.CreatureSelected -= OnSelected;
            GameEvents.AbilitySelected -= OnAbilitySelected;
            GameEvents.CreatureHpChanged -= OnCreatureHpChanged;
        }

        private void Start()
        {
            Refresh();
        }

        private void OnRefresh(int creatureId, long a, long b) => Refresh();
        private void OnEvolved(string fromId, string toId) => Refresh();
        private void OnSelected(string creatureId) => Refresh();
        private void OnAbilitySelected(int slotIndex) => Refresh();
        private void OnCreatureHpChanged(int currentHp, int maxHp)
        {
            CreatureInstance inst = CurrentInstance();
            if (inst != null)
            {
                float frac = inst.MaxHp > 0 ? (float)inst.CurrentHp / inst.MaxHp : 0f;
                SetBar(_hpFill, _hpText, frac, "HP " + inst.CurrentHp + "/" + inst.MaxHp);
            }
        }

        private void Update()
        {
            RefreshAbilityBar();
        }

        private void BuildAbilityBar(Transform canvas)
        {
            RectTransform bar = UIFactory.CreatePanel(canvas,
                new Vector2(0f, 0f), new Vector2(0f, 0f), 0f, 0f, Color.clear);
            bar.anchorMin = new Vector2(0.5f, 0f);
            bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 24f);
            bar.sizeDelta = Vector2.zero;
            SetRaycastTarget(bar.gameObject, false);

            const float slotWidth = 96f;
            const float slotHeight = 62f;
            const float gap = 8f;
            float total = AbilitySlots * slotWidth + (AbilitySlots - 1) * gap;
            float startX = -total * 0.5f;

            for (int i = 0; i < AbilitySlots; i++)
            {
                var slotGo = new GameObject("Ability" + (i + 1), typeof(RectTransform), typeof(Image));
                RectTransform slotRt = slotGo.GetComponent<RectTransform>();
                slotRt.SetParent(bar, false);
                slotRt.sizeDelta = new Vector2(slotWidth, slotHeight);
                slotRt.anchorMin = new Vector2(0f, 0f);
                slotRt.anchorMax = new Vector2(0f, 0f);
                slotRt.pivot = new Vector2(0f, 0f);
                slotRt.anchoredPosition = new Vector2(startX + i * (slotWidth + gap), 0f);

                Image bg = slotGo.GetComponent<Image>();
                bg.sprite = UIFactory.WhiteSprite();
                bg.color = SlotIdleColor;
                bg.raycastTarget = false;
                _slotBgs[i] = bg;

                var cdGo = new GameObject("Cd", typeof(RectTransform), typeof(Image));
                RectTransform cdRt = cdGo.GetComponent<RectTransform>();
                cdRt.SetParent(slotRt, false);
                cdRt.anchorMin = new Vector2(0f, 0f);
                cdRt.anchorMax = Vector2.one;
                cdRt.offsetMin = Vector2.zero;
                cdRt.offsetMax = Vector2.zero;
                Image cd = cdGo.GetComponent<Image>();
                cd.sprite = UIFactory.WhiteSprite();
                cd.color = CdOverlayColor;
                cd.raycastTarget = false;
                _slotCds[i] = cd;

                Text num = UIFactory.CreateText(slotRt, (i + 1).ToString(), 22, MutedColor,
                    TextAnchor.UpperCenter, FontStyle.Bold, "Num");
                num.rectTransform.anchorMin = Vector2.zero;
                num.rectTransform.anchorMax = Vector2.one;
                num.rectTransform.offsetMin = new Vector2(0f, 34f);
                num.rectTransform.offsetMax = new Vector2(0f, 0f);
                num.raycastTarget = false;
                _slotNums[i] = num;

                Text name = UIFactory.CreateText(slotRt, "-", 15, TitleColor,
                    TextAnchor.UpperCenter, FontStyle.Normal, "Name");
                name.rectTransform.anchorMin = Vector2.zero;
                name.rectTransform.anchorMax = Vector2.one;
                name.rectTransform.offsetMin = new Vector2(0f, 4f);
                name.rectTransform.offsetMax = new Vector2(0f, 32f);
                name.raycastTarget = false;
                _slotNames[i] = name;
            }
        }

        private void RefreshAbilityBar()
        {
            if (_caster == null)
            {
                _caster = ServiceLocator.Get<AbilityCaster>();
                if (_caster == null)
                {
                    var pm = ServiceLocator.Get<PlayerManager>();
                    if (pm != null) _caster = pm.GetComponentInChildren<AbilityCaster>();
                }
            }
            if (_caster == null) return;

            var abilities = _caster.Abilities;
            for (int i = 0; i < AbilitySlots; i++)
            {
                bool hasAbility = i < _caster.AbilityCount && abilities[i] != null;
                _slotBgs[i].gameObject.SetActive(hasAbility);
                if (!hasAbility) continue;

                _slotNames[i].text = abilities[i].DisplayName;
                bool selected = _caster.SelectedIndex == i;
                float cdFrac = _caster.CooldownFraction(i);
                bool cdReady = cdFrac <= 0.001f;

                _slotBgs[i].color = selected ? SlotSelectedColor : (cdReady ? SlotReadyColor : SlotIdleColor);
                Color numColor = selected ? new Color(0.14f, 0.10f, 0.02f, 1f) : MutedColor;
                _slotNums[i].color = numColor;

                RectTransform cdRt = _slotCds[i].rectTransform;
                cdRt.anchorMin = new Vector2(0f, cdFrac);
                cdRt.anchorMax = new Vector2(1f, 1f);
                cdRt.offsetMin = Vector2.zero;
                cdRt.offsetMax = Vector2.zero;
                _slotCds[i].color = cdReady ? new Color(0f, 0f, 0f, 0f) : CdOverlayColor;
            }
        }

        private void Refresh()
        {
            CreatureInstance inst = CurrentInstance();
            if (inst == null || inst.Data == null)
            {
                _nameText.text = "No creature";
                _subText.text = "";
                SetBar(_hpFill, _hpText, 0f, "HP --/--");
                SetBar(_expFill, _expText, 0f, "Exp --/--");
                return;
            }

            _nameText.text = inst.Data.DisplayName;
            _subText.text = inst.Data.Element + "  \u00b7  Lv." + inst.Level
                + (inst.IsMaxLevel ? "  (MAX)" : "");

            float hpFrac = inst.MaxHp > 0 ? (float)inst.CurrentHp / inst.MaxHp : 0f;
            SetBar(_hpFill, _hpText, hpFrac, "HP " + inst.CurrentHp + "/" + inst.MaxHp);

            float expFrac = CreatureLevels.ProgressIntoLevel(inst.Level, inst.Exp);
            string expLine = inst.IsMaxLevel
                ? "Exp MAX"
                : "Exp " + inst.Exp + "/" + inst.ExpNextLevel;
            SetBar(_expFill, _expText, expFrac, expLine);
        }

        private CreatureInstance CurrentInstance()
        {
            PlayerManager pm = playerManager != null ? playerManager : ServiceLocator.Get<PlayerManager>();
            return pm != null && pm.HasCreature ? pm.ActiveCreature : null;
        }

        /// <summary>Creates a bar: background image plus a left-anchored fill and a label.</summary>
        private static Image CreateBar(Transform parent, Vector2 size, Vector2 anchoredPos, float fillStart,
            Color fillColor, out Image fill, out Text label)
        {
            var bgGo = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.SetParent(parent, false);
            bgRt.sizeDelta = size;
            bgRt.anchorMin = new Vector2(0f, 1f);
            bgRt.anchorMax = new Vector2(0f, 1f);
            bgRt.pivot = new Vector2(0f, 0.5f);
            bgRt.anchoredPosition = anchoredPos;
            Image bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = UIFactory.WhiteSprite();
            bgImg.color = BarBgColor;
            SetRaycastTarget(bgImg.gameObject, false);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(bgRt, false);
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(fillStart, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fill = fillGo.GetComponent<Image>();
            fill.sprite = UIFactory.WhiteSprite();
            fill.color = fillColor;
            SetRaycastTarget(fill.gameObject, false);

            label = UIFactory.CreateText(bgRt, "", 13, TitleColor,
                TextAnchor.MiddleLeft, FontStyle.Bold, "Label");
            label.rectTransform.offsetMin = new Vector2(8f, 0f);
            label.rectTransform.offsetMax = Vector2.zero;
            label.raycastTarget = false;
            return bgImg;
        }

        private static void SetBar(Image fill, Text label, float frac, string line)
        {
            frac = Mathf.Clamp01(frac);
            RectTransform rt = fill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(frac, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (label != null) label.text = line;
        }

        private static void SetRaycastTarget(GameObject go, bool value)
        {
            foreach (Image img in go.GetComponentsInChildren<Image>(true))
                img.raycastTarget = value;
        }
    }
}