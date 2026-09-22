using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Player;
using Davidmon.UI;

namespace Davidmon.NPC
{
    /// <summary>
    /// Runtime dialogue box. Subscribes to the <see cref="GameEvents"/> dialogue
    /// channels, renders the current line at the bottom of the screen and rebuilds
    /// the choice buttons each time choices appear. While a conversation is open it
    /// locks character movement and requests modal cursor access so the player can
    /// read and pick options; E advances/continues, Esc closes.
    /// </summary>
    public sealed class DialogueUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.05f, 0.05f, 0.08f, 0.96f);
        private static readonly Color NameColor = new Color(0.96f, 0.72f, 0.22f, 1f);
        private static readonly Color BodyColor = new Color(0.94f, 0.94f, 0.92f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.65f, 0.70f, 1f);
        private static readonly Color ChoiceColor = new Color(0.15f, 0.18f, 0.26f, 1f);

        private PlayerInputProvider _input;
        private DialogueSystem _dialogue;

        private GameObject _root;
        private Text _nameText;
        private Text _bodyText;
        private Text _hintText;
        private RectTransform _choicesRoot;

        private void Awake()
        {
            _input = FindAnyObjectByType<PlayerInputProvider>();
            _dialogue = ServiceLocator.GetOrCreate(() => new DialogueSystem());
            UIFactory.EnsureEventSystem();
            Build();
        }

        private void OnEnable()
        {
            GameEvents.DialogueOpened += OnOpened;
            GameEvents.DialogueLineChanged += OnLineChanged;
            GameEvents.DialogueChoicesShown += OnChoicesShown;
            GameEvents.DialogueChoiceSelected += OnChoiceSelected;
            GameEvents.DialogueReplyShown += OnReplyShown;
            GameEvents.DialogueClosed += OnClosed;
            GameEvents.EscapeRequested += OnEscapeRequested;
            if (_input != null) _input.InteractPressed += OnInteractPressed;
        }

        private void OnDisable()
        {
            GameEvents.DialogueOpened -= OnOpened;
            GameEvents.DialogueLineChanged -= OnLineChanged;
            GameEvents.DialogueChoicesShown -= OnChoicesShown;
            GameEvents.DialogueChoiceSelected -= OnChoiceSelected;
            GameEvents.DialogueReplyShown -= OnReplyShown;
            GameEvents.DialogueClosed -= OnClosed;
            GameEvents.EscapeRequested -= OnEscapeRequested;
            if (_input != null) _input.InteractPressed -= OnInteractPressed;
        }

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("DialogueCanvas", 70);
            _root = canvas.gameObject;

            RectTransform panel = CreateBottomPanel(canvas.transform);

            _nameText = UIFactory.CreateText(panel, "", 28, NameColor, TextAnchor.UpperLeft, FontStyle.Bold, "Name");
            RectTransform nameRt = _nameText.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0f, 1f);
            nameRt.sizeDelta = new Vector2(0f, 44f);
            nameRt.anchoredPosition = new Vector2(28f, -14f);

            _bodyText = UIFactory.CreateText(panel, "", 30, BodyColor, TextAnchor.UpperLeft, FontStyle.Normal, "Body");
            RectTransform bodyRt = _bodyText.rectTransform;
            bodyRt.offsetMin = new Vector2(28f, 56f);
            bodyRt.offsetMax = new Vector2(-28f, -70f);

            _hintText = UIFactory.CreateText(panel, "", 20, MutedColor, TextAnchor.LowerRight, FontStyle.Normal, "Hint");
            RectTransform hintRt = _hintText.rectTransform;
            hintRt.anchorMin = new Vector2(1f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(1f, 0f);
            hintRt.sizeDelta = new Vector2(420f, 34f);
            hintRt.anchoredPosition = new Vector2(-26f, 12f);

            var choicesGo = new GameObject("Choices", typeof(RectTransform));
            RectTransform choicesRt = choicesGo.GetComponent<RectTransform>();
            choicesRt.SetParent(panel, false);
            choicesRt.anchorMin = Vector2.zero;
            choicesRt.anchorMax = Vector2.one;
            choicesRt.offsetMin = Vector2.zero;
            choicesRt.offsetMax = Vector2.zero;
            _choicesRoot = choicesRt;

            _root.SetActive(false);
        }

        private static RectTransform CreateBottomPanel(Transform parent)
        {
            var go = new GameObject("DialoguePanel", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.04f, 0f);
            rt.anchorMax = new Vector2(0.96f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 300f);
            rt.anchoredPosition = new Vector2(0f, 210f);
            rt.GetComponent<Image>().color = PanelColor;
            return rt;
        }

        private void OnOpened(NpcData data)
        {
            _root.SetActive(true);
            _input?.SetMovementLocked(true);
            Core.CursorManager.Current?.RequestModal();

            _nameText.text = FormatName(data);
            _bodyText.text = "";
            _bodyText.color = BodyColor;
            _hintText.text = "Press E to continue";
            HideChoices();
        }

        private void OnLineChanged(NpcData data, int index)
        {
            if (!_root.activeSelf) _root.SetActive(true);
            if (data == null || data.IntroLines == null || index < 0 || index >= data.IntroLines.Length) return;

            DialogueLine line = data.IntroLines[index];
            _bodyText.text = string.IsNullOrEmpty(line.Speaker) ? line.Text : "<b>" + line.Speaker + ":</b> " + line.Text;
            _bodyText.color = BodyColor;
            _hintText.text = "Press E to continue";
            HideChoices();
        }

        private void OnChoicesShown(NpcData data)
        {
            _bodyText.text = "";
            _hintText.text = "";
            RebuildChoices(data);
        }

        private void OnChoiceSelected(NpcData data, DialogueChoice choice)
        {
            // The follow-up event replaces the buttons; nothing else to do here.
        }

        private void OnReplyShown(NpcData data, string text)
        {
            _bodyText.text = text;
            _bodyText.color = BodyColor;
            _hintText.text = "Press E to close";
            HideChoices();
        }

        private void OnClosed(NpcData data)
        {
            _root.SetActive(false);
            HideChoices();
            _input?.SetMovementLocked(false);
            Core.CursorManager.Current?.ReleaseModal();
        }

        private void OnEscapeRequested()
        {
            if (_root.activeSelf && _dialogue != null && _dialogue.IsActive)
                _dialogue.Close();
        }

        private void OnInteractPressed()
        {
            if (_dialogue == null || !_dialogue.IsActive) return;
            if (_dialogue.IsShowingChoices) return;
            _dialogue.Advance();
        }

        private void RebuildChoices(NpcData data)
        {
            HideChoices();
            if (data == null || data.Choices == null || data.Choices.Length == 0) return;

            int count = Mathf.Min(data.Choices.Length, 4);
            for (int i = 0; i < count; i++)
            {
                DialogueChoice choice = data.Choices[i];
                int selectedIndex = i;
                Button button = UIFactory.CreateButton(_choicesRoot, new Vector2(560f, 62f), choice.Label, 24,
                    () => { if (_dialogue != null) _dialogue.SelectChoice(selectedIndex); },
                    ChoiceColor, new Color(0.94f, 0.94f, 0.94f, 1f), "Choice" + (i + 1));
                RectTransform rt = (RectTransform)button.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 60f - i * 84f);
            }
        }

        private void HideChoices()
        {
            for (int i = _choicesRoot.childCount - 1; i >= 0; i--)
                Destroy(_choicesRoot.GetChild(i).gameObject);
        }

        private static string FormatName(NpcData data)
        {
            if (data == null) return "";
            return string.IsNullOrEmpty(data.Title) ? data.DisplayName : data.DisplayName + " \u2014 " + data.Title;
        }
    }
}