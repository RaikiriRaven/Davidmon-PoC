using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Inventory;
using Davidmon.NPC;
using Davidmon.Player;

namespace Davidmon.UI
{
    /// <summary>
    /// Purchase screen for milestone 8: lists a merchant's stock and spends the player's
    /// wallet coins into the inventory. Follows the same modal rules as every other screen
    /// (Esc closes, movement locked, cursor captured) and rebuilds its rows from live
    /// inventory/wallet state so the HUD and inventory update through normal events.
    /// </summary>
    public sealed class ShopUI : MonoBehaviour
    {
        [SerializeField] private ShopStock stock;

        private Wallet _wallet;
        private PlayerInventory _inventory;
        private PlayerInputProvider _input;

        private GameObject _root;
        private RectTransform _rowsRoot;
        private Text _titleText;
        private Text _coinsText;
        private Text _emptyText;
        private Text _moreText;
        private readonly List<RectTransform> _rows = new List<RectTransform>();
        private readonly List<Text> _rowNames = new List<Text>();
        private readonly List<Text> _rowOwned = new List<Text>();
        private readonly List<Text> _rowPrices = new List<Text>();
        private readonly List<Button> _rowBuy = new List<Button>();
        private bool _dirty;

        private const float RowHeight = 72f;
        private const int MaxRows = 7;

        private static readonly Color ShadeBack = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color ShopBack = new Color(0.07f, 0.08f, 0.11f, 0.96f);
        private static readonly Color RowBack = new Color(0.13f, 0.15f, 0.21f, 0.9f);
        private static readonly Color NameColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color MutedColor = new Color(0.68f, 0.7f, 0.76f, 1f);
        private static readonly Color CoinColor = new Color(0.98f, 0.78f, 0.24f, 1f);
        private static readonly Color BuyColor = new Color(0.16f, 0.42f, 0.72f, 1f);

        private void Awake()
        {
            _wallet = ServiceLocator.GetOrCreate(() => new Wallet());
            _inventory = ServiceLocator.GetOrCreate(() => new PlayerInventory());
            _input = FindAnyObjectByType<PlayerInputProvider>();
            if (stock == null) stock = LoadStockFromEditor();
            Build();
            if (_root != null) _root.SetActive(false);
        }

        private void OnEnable()
        {
            GameEvents.CurrencyChanged += OnCurrencyChanged;
            GameEvents.InventoryChanged += OnInventoryChanged;
            GameEvents.EscapeRequested += OnEscapeRequested;
            GameEvents.ShopRequested += OnShopRequested;
        }

        private void OnDisable()
        {
            GameEvents.CurrencyChanged -= OnCurrencyChanged;
            GameEvents.InventoryChanged -= OnInventoryChanged;
            GameEvents.EscapeRequested -= OnEscapeRequested;
            GameEvents.ShopRequested -= OnShopRequested;
        }

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("ShopCanvas", 60);
            _root = canvas.gameObject;

            UIFactory.CreateFullPanel(_root.transform, ShadeBack).name = "Dim";

            RectTransform panel = UIFactory.CreateCenteredPanel(_root.transform, new Vector2(920f, 720f), ShopBack);
            panel.name = "ShopPanel";

            _titleText = UIFactory.CreateText(panel, "SHOP", 34, NameColor, TextAnchor.UpperLeft, FontStyle.Bold, "Title");
            RectTransform titleRt = _titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.sizeDelta = new Vector2(0f, 48f);
            titleRt.anchoredPosition = new Vector2(28f, -16f);

            _coinsText = UIFactory.CreateText(panel, "", 26, CoinColor, TextAnchor.UpperRight, FontStyle.Bold, "Coins");
            RectTransform coinsRt = _coinsText.rectTransform;
            coinsRt.anchorMin = new Vector2(0f, 1f);
            coinsRt.anchorMax = new Vector2(1f, 1f);
            coinsRt.pivot = new Vector2(1f, 1f);
            coinsRt.sizeDelta = new Vector2(0f, 48f);
            coinsRt.anchoredPosition = new Vector2(-28f, -16f);

            RectTransform rowsArea = UIFactory.CreatePanel(panel, Vector2.zero, Vector2.one, 24f, 24f, new Color(0f, 0f, 0f, 0f));
            rowsArea.name = "Rows";
            rowsArea.offsetMax = new Vector2(-24f, -64f);
            _rowsRoot = rowsArea;

            _emptyText = UIFactory.CreateText(_rowsRoot, "The shelves are empty.", 24, MutedColor);
            _emptyText.rectTransform.anchoredPosition = Vector2.zero;
            _emptyText.gameObject.SetActive(false);

            _moreText = UIFactory.CreateText(_rowsRoot, "+N more (coming soon)", 18, MutedColor, TextAnchor.LowerCenter);
            RectTransform moreRt = _moreText.rectTransform;
            moreRt.anchorMin = new Vector2(0f, 0f);
            moreRt.anchorMax = new Vector2(1f, 0f);
            moreRt.pivot = new Vector2(0.5f, 0f);
            moreRt.sizeDelta = new Vector2(0f, 30f);
            moreRt.anchoredPosition = new Vector2(0f, 8f);
            _moreText.gameObject.SetActive(false);

            for (int i = 0; i < MaxRows; i++)
                CreateRow(i);

            Text hint = UIFactory.CreateText(panel, "Esc to close", 20, MutedColor, TextAnchor.LowerRight);
            RectTransform hintRt = hint.rectTransform;
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(1f, 0f);
            hintRt.sizeDelta = new Vector2(0f, 34f);
            hintRt.anchoredPosition = new Vector2(-26f, 12f);
        }

        private void OnShopRequested(NpcData npc)
        {
            EnsureBuilt();
            if (npc != null && !string.IsNullOrEmpty(npc.DisplayName))
                _titleText.text = "SHOP \u2014 " + npc.DisplayName.ToUpperInvariant();
            else
                _titleText.text = "SHOP";
            Show();
        }

        private void OnEscapeRequested()
        {
            if (_root != null && _root.activeSelf) Hide();
        }

        private void OnCurrencyChanged(long oldValue, long newValue) => MarkDirty();
        private void OnInventoryChanged(string itemId) => MarkDirty();

        private void LateUpdate()
        {
            if (_dirty)
            {
                _dirty = false;
                Refresh();
            }
        }

        private void MarkDirty() => _dirty = true;

        private void Show()
        {
            EnsureBuilt();
            _root.SetActive(true);
            _input?.SetMovementLocked(true);
            Core.CursorManager.Current?.RequestModal();
            Refresh();
        }

        private void EnsureBuilt()
        {
            if (_root == null) Build();
        }

        private void Hide()
        {
            if (_root == null) return;
            _root.SetActive(false);
            _input?.SetMovementLocked(false);
            Core.CursorManager.Current?.ReleaseModal();
        }

        private void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            _coinsText.text = "\u25c8 " + (_wallet != null ? _wallet.Coins : 0L);
            UpdateRows();
        }

        private void UpdateRows()
        {
            int count = (stock != null && stock.Entries != null) ? stock.Entries.Count : 0;
            int shown = Mathf.Min(count, MaxRows);
            bool empty = count == 0;

            for (int i = 0; i < MaxRows; i++)
            {
                bool visible = !empty && i < shown;
                _rows[i].gameObject.SetActive(visible);
                if (visible)
                    PopulateRow(i, stock.Entries[i]);
            }

            if (_emptyText != null) _emptyText.gameObject.SetActive(empty);
            if (_moreText != null)
            {
                bool showMore = !empty && count > MaxRows;
                _moreText.gameObject.SetActive(showMore);
                if (showMore)
                    _moreText.text = "+" + (count - MaxRows) + " more (coming soon)";
            }
        }

        private void CreateRow(int index)
        {
            var rowGo = new GameObject("Row", typeof(RectTransform), typeof(Image));
            RectTransform row = rowGo.GetComponent<RectTransform>();
            row.SetParent(_rowsRoot, false);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, 60f);
            row.anchoredPosition = new Vector2(0f, -12f - index * 72f);
            rowGo.GetComponent<Image>().color = RowBack;

            Text name = UIFactory.CreateText(row, "", 24, NameColor,
                TextAnchor.MiddleLeft, FontStyle.Bold, "Name");
            RectTransform nameRt = name.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.offsetMin = new Vector2(14f, 0f);
            nameRt.offsetMax = new Vector2(-320f, 0f);

            Text ownedText = UIFactory.CreateText(row, "", 20, MutedColor, TextAnchor.MiddleCenter, FontStyle.Normal, "Owned");
            RectTransform ownedRt = ownedText.rectTransform;
            ownedRt.anchorMin = new Vector2(0f, 0f);
            ownedRt.anchorMax = new Vector2(1f, 1f);
            ownedRt.offsetMin = new Vector2(-300f, 0f);
            ownedRt.offsetMax = new Vector2(-190f, 0f);

            Text price = UIFactory.CreateText(row, "", 22, CoinColor, TextAnchor.MiddleCenter, FontStyle.Bold, "Price");
            RectTransform priceRt = price.rectTransform;
            priceRt.anchorMin = new Vector2(0f, 0f);
            priceRt.anchorMax = new Vector2(1f, 1f);
            priceRt.offsetMin = new Vector2(-190f, 0f);
            priceRt.offsetMax = new Vector2(-80f, 0f);

            int slotIndex = index;
            Button buyButton = UIFactory.CreateButton(row, new Vector2(140f, 44f), "BUY", 20,
                () => Buy(slotIndex), BuyColor, new Color(0.95f, 0.95f, 0.95f, 1f), "BuyButton");
            RectTransform button = (RectTransform)buyButton.transform;
            button.anchorMin = new Vector2(1f, 0.5f);
            button.anchorMax = new Vector2(1f, 0.5f);
            button.pivot = new Vector2(1f, 0.5f);
            button.anchoredPosition = new Vector2(-14f, 0f);

            _rows.Add(row);
            _rowNames.Add(name);
            _rowOwned.Add(ownedText);
            _rowPrices.Add(price);
            _rowBuy.Add(buyButton);
        }

        private void PopulateRow(int index, ShopEntry entry)
        {
            if (index < 0 || index >= _rows.Count) return;
            ItemData item = entry != null ? entry.Item : null;
            bool valid = item != null && _inventory != null;

            _rowNames[index].text = valid ? item.DisplayName : "Unknown item";
            _rowOwned[index].text = valid ? "Owned: " + _inventory.GetCount(item.ItemId) : "-";
            _rowPrices[index].text = entry != null ? "\u25c8 " + entry.Price : "-";
            _rowBuy[index].gameObject.SetActive(valid);
        }

        private void Buy(int index)
        {
            if (stock == null || stock.Entries == null || index < 0 || index >= stock.Entries.Count) return;
            ShopEntry entry = stock.Entries[index];
            if (entry == null || entry.Item == null || _wallet == null || _inventory == null) return;

            ItemData item = entry.Item;
            int owned = _inventory.GetCount(item.ItemId);
            if (owned >= item.MaxStack)
            {
                GameEvents.RaiseShowNotification("You already carry the maximum amount of " + item.DisplayName + ".");
                return;
            }

            long price = entry.Price;
            if (_wallet.Coins < price)
            {
                GameEvents.RaiseShowNotification("Not enough coins for " + item.DisplayName + " (" + price + " needed).");
                return;
            }

            if (_wallet.TrySpend(price))
            {
                _inventory.Add(item, 1);
                GameEvents.RaiseShowNotification("Bought " + item.DisplayName + " for " + price + " coins.");
            }
        }

        private static ShopStock LoadStockFromEditor()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ShopStock>("Assets/ScriptableObjects/Items/StarterShop.asset");
#else
            return null;
#endif
        }
    }
}