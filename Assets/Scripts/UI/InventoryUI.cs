using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Inventory;
using Davidmon.Player;

namespace Davidmon.UI
{
    /// <summary>
    /// Inventory screen. Toggles with the Inventory action (I / Tab), lists every held
    /// item stack with its quantity and shows the current coin balance. Listens to the
    /// inventory and currency change events so it stays live without polling. Shares
    /// the same modal pattern as the other screens: movement locked, cursor captive,
    /// Esc or the close button dismisses it.
    /// </summary>
    public sealed class InventoryUI : MonoBehaviour
    {
        private static readonly Color PanelBackColor = new Color(0.06f, 0.07f, 0.11f, 0.97f);
        private static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.17f, 1f);
        private static readonly Color AccentColor = new Color(0.95f, 0.65f, 0.15f, 1f);
        private static readonly Color TextColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.65f, 0.70f, 1f);
        private static readonly Color RowColor = new Color(0.13f, 0.16f, 0.22f, 1f);

        private const float RowHeight = 64f;
        private const float MaxRows = 12f;

        private PlayerInventory _inventory;
        private Wallet _wallet;
        private PlayerInputProvider _input;

        private GameObject _root;
        private RectTransform _listArea;
        private Text _coinsText;
        private Text _rowsText;
        private RectTransform _scrollRoot;

        private void Awake()
        {
            _inventory = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            _wallet = ServiceLocator.GetOrCreate(() => new Wallet());
            _input = FindAnyObjectByType<PlayerInputProvider>();

            UIFactory.EnsureEventSystem();
            Build();
            Hide();
        }

        private void OnEnable()
        {
            if (_input != null) _input.InventoryPressed += Toggle;
            GameEvents.InventoryChanged += OnInventoryChanged;
            GameEvents.CurrencyChanged += OnCurrencyChanged;
            GameEvents.EscapeRequested += OnEscapeRequested;
        }

        private void OnDisable()
        {
            if (_input != null) _input.InventoryPressed -= Toggle;
            GameEvents.InventoryChanged -= OnInventoryChanged;
            GameEvents.CurrencyChanged -= OnCurrencyChanged;
            GameEvents.EscapeRequested -= OnEscapeRequested;
        }

        private void OnEscapeRequested()
        {
            if (_root != null && _root.activeSelf) Hide();
        }

        private void OnInventoryChanged(string _) => Refresh();

        private void OnCurrencyChanged(long oldAmount, long newAmount) => Refresh();

        /// <summary>Opens the inventory when closed, closes it when open.</summary>
        public void Toggle()
        {
            if (_root != null && _root.activeSelf) Hide();
            else Show();
        }

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("InventoryCanvas", 50);
            RectTransform backdrop = UIFactory.CreateFullPanel(canvas.transform, PanelBackColor);

            RectTransform panel = UIFactory.CreateCenteredPanel(backdrop, new Vector2(760f, 640f), PanelColor);
            panel.gameObject.name = "InventoryPanel";

            UIFactory.CreateText(panel, "INVENTORY", 44, AccentColor,
                TextAnchor.UpperCenter, FontStyle.Bold, "Title")
                .rectTransform.anchoredPosition = new Vector2(0f, -26f);

            _coinsText = UIFactory.CreateText(panel, "", 24, TextColor,
                TextAnchor.UpperRight, FontStyle.Bold, "Coins");
            RectTransform coinsRt = _coinsText.rectTransform;
            coinsRt.anchorMin = new Vector2(0f, 1f);
            coinsRt.anchorMax = new Vector2(1f, 1f);
            coinsRt.pivot = new Vector2(1f, 1f);
            coinsRt.anchoredPosition = new Vector2(-28f, -36f);
            coinsRt.sizeDelta = new Vector2(300f, 44f);

            RectTransform listBg = UIFactory.CreateCenteredPanel(panel, new Vector2(700f, 456f), RowColor);
            listBg.gameObject.name = "ListBackground";
            listBg.anchoredPosition = new Vector2(0f, -46f);
            Image listImg = listBg.GetComponent<Image>();
            listImg.sprite = UIFactory.WhiteSprite();
            listImg.raycastTarget = false;

            _scrollRoot = listBg;
            _listArea = listBg;

            _rowsText = UIFactory.CreateText(listBg, "", 22, TextColor,
                TextAnchor.UpperLeft, FontStyle.Normal, "Rows");
            RectTransform rowsRt = _rowsText.rectTransform;
            rowsRt.anchorMin = Vector2.zero;
            rowsRt.anchorMax = Vector2.one;
            rowsRt.offsetMin = new Vector2(20f, 20f);
            rowsRt.offsetMax = new Vector2(-20f, -20f);
            rowsRt.pivot = new Vector2(0f, 1f);

            Button close = UIFactory.CreateButton(panel, new Vector2(240f, 58f), "CLOSE", 24,
                Hide, new Color(0.16f, 0.16f, 0.18f, 1f), TextColor, "CloseButton");
            ((RectTransform)close.transform).anchoredPosition = new Vector2(0f, -290f);

            _root = canvas.gameObject;
        }

        private void Show()
        {
            _root.SetActive(true);
            _input?.SetMovementLocked(true);
            Core.CursorManager.Current?.RequestModal();
            Refresh();
        }

        private void Hide()
        {
            _root.SetActive(false);
            _input?.SetMovementLocked(false);
            Core.CursorManager.Current?.ReleaseModal();
        }

        private void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            _coinsText.text = "\u25c8 " + (_wallet != null ? _wallet.Coins : 0L);

            var stacks = new List<KeyValuePair<ItemData, int>>(_inventory.EnumerateStacks());
            if (stacks.Count == 0)
            {
                _rowsText.text = "Your inventory is empty.";
                _rowsText.alignment = TextAnchor.MiddleCenter;
                return;
            }

            _rowsText.alignment = TextAnchor.UpperLeft;
            var builder = new System.Text.StringBuilder();
            int shown = Mathf.Min(stacks.Count, (int)MaxRows);
            for (int i = 0; i < shown; i++)
            {
                KeyValuePair<ItemData, int> stack = stacks[i];
                builder.Append(stack.Key.DisplayName);
                builder.Append("   x");
                builder.Append(stack.Value);
                builder.Append('\n');
            }
            if (stacks.Count > shown)
                builder.Append("\n... and ").Append(stacks.Count - shown).Append(" more");

            _rowsText.text = builder.ToString();
            _rowsText.lineSpacing = 1.25f;
        }
    }
}