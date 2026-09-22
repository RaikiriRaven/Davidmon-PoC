using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Creatures;
using Davidmon.Player;

namespace Davidmon.UI
{
    /// <summary>
    /// First-time creature selection screen. Builds its own canvas, lists the starter
    /// creatures from the catalog, previews stats and confirms the choice. On confirm
    /// the selection is persisted (PlayerPrefs for the prototype) and the player's
    /// avatar is spawned.
    /// </summary>
    public sealed class CreatureSelectionUI : MonoBehaviour
    {
        public const string SaveKey = "davidmon.selected_creature";

        [SerializeField] private CreatureCatalog catalog;
        [SerializeField] private PlayerInputProvider input;
        [SerializeField] private PlayerManager playerManager;

        private Canvas _canvas;
        private GameObject _root;
        private Text _detailText;
        private Button _confirmButton;
        private Button _retryButton;
        private bool _confirmed;

        private readonly List<Button> _starterButtons = new List<Button>();
        private CreatureData _selected;

        private static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.18f, 0.96f);
        private static readonly Color CardColor = new Color(0.16f, 0.21f, 0.30f, 1f);
        private static readonly Color AccentColor = new Color(0.95f, 0.65f, 0.15f, 1f);
        private static readonly Color TextColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        private static readonly Color MutedColor = new Color(0.65f, 0.68f, 0.72f, 1f);

        private void Awake()
        {
            if (input == null) input = FindAnyObjectByType<PlayerInputProvider>();
            if (playerManager == null) playerManager = FindAnyObjectByType<PlayerManager>();

            UIFactory.EnsureEventSystem();
            GameEvents.EscapeRequested += OnEscapeRequested;
        }

        private void Start()
        {
            catalog = ResolveCatalog();
            BuildScreen();
            Hide();

            if (PlayerPrefs.HasKey(SaveKey))
            {
                string savedId = PlayerPrefs.GetString(SaveKey);
                CreatureData saved = catalog != null ? catalog.Find(savedId) : null;
                if (saved != null)
                {
                    playerManager?.SetCreature(saved);
                    _confirmed = true;
                    Hide();
                    GameEvents.RaiseShowNotification("Press [ESC] to toggle the cursor");
                    return;
                }
            }
            Show();
        }

        private void OnDestroy()
        {
            GameEvents.EscapeRequested -= OnEscapeRequested;
        }

        /// <summary>
        /// Prefers the serialized reference and falls back to the runtime registry.
        /// Called from Start (after every Awake) so the bootstrap has populated it,
        /// and again on RETRY so a late-loading catalog becomes usable.
        /// </summary>
        private CreatureCatalog ResolveCatalog()
        {
            return catalog != null ? catalog : CreatureRegistry.Catalog;
        }

        private void BuildScreen()
        {
            _canvas = UIFactory.CreateCanvas("CreatureSelectionCanvas", 100);
            _root = _canvas.gameObject;
            RectTransform backdrop = UIFactory.CreateFullPanel(_root.transform, PanelColor);

            RectTransform titleRt = UIFactory.CreateText(backdrop, "CHOOSE YOUR PARTNER", 62, AccentColor,
                TextAnchor.UpperCenter, FontStyle.Bold, "Title").rectTransform;
            titleRt.offsetMin = new Vector2(0f, 180f);
            titleRt.offsetMax = new Vector2(0f, -180f);
            UIFactory.CreateText(backdrop, "Pick a starter creature to enter the world.", 26, MutedColor,
                TextAnchor.UpperCenter, FontStyle.Normal, "Subtitle").rectTransform.anchoredPosition = new Vector2(0f, -125f);

            RectTransform listArea = UIFactory.CreateCenteredPanel(backdrop, new Vector2(1500f, 480f), new Color(0f, 0f, 0f, 0f));
            listArea.anchoredPosition = new Vector2(0f, 5f);

            DrawStarterButtons(listArea);

            RectTransform detailArea = UIFactory.CreateCenteredPanel(backdrop, new Vector2(1200f, 180f), new Color(0f, 0f, 0f, 0f));
            detailArea.anchoredPosition = new Vector2(0f, 200f);
            _detailText = UIFactory.CreateText(detailArea, "Select a creature to see its details.", 24, MutedColor,
                TextAnchor.UpperCenter, FontStyle.Normal, "DetailText");
            _detailText.rectTransform.anchoredPosition = new Vector2(0f, -10f);

            bool hasStarters = catalog != null && catalog.Starters.Count > 0;
            if (hasStarters)
            {
                _confirmButton = UIFactory.CreateButton(backdrop, new Vector2(320f, 70f), "CONFIRM", 30,
                    OnConfirmClicked, AccentColor, new Color(0.1f, 0.07f, 0.02f, 1f), "ConfirmButton");
                _confirmButton.transform.SetParent(backdrop, false);
                _confirmButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -325f);
                _confirmButton.interactable = false;
            }
            else
            {
                _retryButton = UIFactory.CreateButton(backdrop, new Vector2(320f, 70f), "RETRY", 30,
                    OnRetryClicked, AccentColor, new Color(0.1f, 0.07f, 0.02f, 1f), "RetryButton");
                _retryButton.transform.SetParent(backdrop, false);
                _retryButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -325f);
            }
        }

        private void OnRetryClicked()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            catalog = ResolveCatalog();
            if (_canvas != null) Destroy(_canvas.gameObject);

            _starterButtons.Clear();
            _detailText = null;
            _confirmButton = null;
            _retryButton = null;

            BuildScreen();
            Show();
        }

        /// <summary>
        /// Escape only dismisses the broken no-catalog state so the player can never
        /// get soft-locked. In the healthy flow the selection is mandatory, so Esc
        /// intentionally does nothing there while a selection is pending.
        /// </summary>
        private void OnEscapeRequested()
        {
            if (_root != null && _root.activeSelf && (catalog == null || catalog.Starters.Count == 0))
            {
                Hide();
                GameEvents.RaiseShowNotification("No starter catalog available. Press RETRY or fix the creature catalog asset.");
            }
        }

        private void DrawStarterButtons(RectTransform parent)
        {
            if (catalog == null || catalog.Starters.Count == 0)
            {
                UIFactory.CreateText(parent, "No starter creatures found in the catalog.", 24, MutedColor,
                TextAnchor.MiddleCenter, FontStyle.Normal, "Missing");
                return;
            }

            int count = catalog.Starters.Count;
            float spacing = 320f;
            for (int i = 0; i < count; i++)
            {
                CreatureData data = catalog.Starters[i];
                int index = i;
                Button button = UIFactory.CreateButton(parent, new Vector2(280f, 380f), data.DisplayName + "\n" + ElementLabel(data.Element),
                    30, () => SelectCreature(catalog.Starters[index]), CardColor, TextColor, "Starter" + index);
                button.transform.SetParent(parent, false);
                ((RectTransform)button.transform).anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * spacing, 0f);
                _starterButtons.Add(button);
            }
        }

        private static string ElementLabel(ElementType element)
        {
            return element == ElementType.Neutral ? "" : "[" + element + "]";
        }

        private void SelectCreature(CreatureData data)
        {
            if (data == null) return;
            _selected = data;
            _confirmButton.interactable = true;

            StatBlock s = data.BaseStats;
            _detailText.text = $"{data.DisplayName}  {ElementLabel(data.Element)}\n" +
                               $"{data.Description}\n" +
                               $"HP {s.maxHp}   ATK {s.attack}   DEF {s.defense}   SPD {s.speed}";
            _detailText.fontSize = 24;
            _detailText.color = TextColor;
        }

        private void OnConfirmClicked()
        {
            if (_selected == null) return;

            PlayerPrefs.SetString(SaveKey, _selected.CreatureId);
            playerManager?.SetCreature(_selected);
            _confirmed = true;
            Hide();
            GameEvents.RaiseShowNotification("Press [ESC] to toggle the cursor");
        }

        public void Show()
        {
            if (!_confirmed) _root.SetActive(true);
            input?.SetMovementLocked(true);
            Core.CursorManager.Current?.RequestModal();
        }

        public void Hide()
        {
            _root.SetActive(false);
            input?.SetMovementLocked(false);
            Core.CursorManager.Current?.ReleaseModal();
            GameEvents.RaiseCreatureSpawned(_selected != null ? _selected.CreatureId : "");
        }
    }
}