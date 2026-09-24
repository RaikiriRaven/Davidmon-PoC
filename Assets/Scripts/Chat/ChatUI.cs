using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Multiplayer;
using Davidmon.Player;
using Davidmon.UI;

namespace Davidmon.Chat
{
    /// <summary>
    /// Milestone 13 (part 2) — multiplayer text chat UI.
    ///
    /// Spec behavior: while moving normally the player presses ENTER, the chat
    /// input activates, they type, ENTER sends to all players and closes the
    /// input (movement resumes), ESC cancels. Movement is locked while typing so
    /// gameplay keys do not leak into movement or combat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChatUI : MonoBehaviour
    {
        private const int MaxLines = 8;

        private static readonly Color PanelColor = new Color(0.05f, 0.05f, 0.08f, 0.70f);
        private static readonly Color TextColor = new Color(0.93f, 0.93f, 0.95f, 1f);
        private static readonly Color InputBgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);

        private Text _historyText;
        private GameObject _inputRoot;
        private InputField _inputField;
        private readonly Queue<string> _lines = new Queue<string>();

        private bool _open;
        private int _openedFrame = -1;

        private void Awake()
        {
            UIFactory.EnsureEventSystem();
            BuildUI();
            ChatNetwork.EnsureClientHandler();
        }

        private void OnEnable()
        {
            ChatNetwork.MessageReceived += AddMessage;
            ChatNetwork.EnsureClientHandler();
        }

        private void OnDisable()
        {
            ChatNetwork.MessageReceived -= AddMessage;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            bool enter = kb.enterKey.wasPressedThisFrame
                || kb.numpadEnterKey.wasPressedThisFrame;

            if (!_open)
            {
                // Only hijack ENTER when no modal UI owns the input.
                PlayerInputProvider local = PlayerInputProvider.LocalOrAny();
                bool free = local == null || local.MovementEnabled;
                if (free && enter) OpenChat();
                return;
            }

            if (Time.frameCount == _openedFrame) return; // ignore the opening keypress
            if (kb.escapeKey.wasPressedThisFrame)
            {
                CloseChat();
                GameEvents.RaiseEscapeRequested();
            }
            else if (enter)
            {
                SendAndClose();
            }
        }

        private void BuildUI()
        {
            Canvas canvas = UIFactory.CreateCanvas("ChatCanvas", 100);

            RectTransform history = UIFactory.CreatePanel(canvas.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), 0f, 0f, PanelColor);
            history.name = "ChatHistory";
            history.anchorMin = new Vector2(0f, 0f);
            history.anchorMax = new Vector2(0f, 0f);
            history.pivot = new Vector2(0f, 0f);
            history.anchoredPosition = new Vector2(24f, 420f);
            history.sizeDelta = new Vector2(520f, 190f);
            foreach (Image img in history.GetComponentsInChildren<Image>(true))
                img.raycastTarget = false;

            _historyText = UIFactory.CreateText(history, "", 17, TextColor,
                TextAnchor.LowerLeft, FontStyle.Normal, "History");
            _historyText.rectTransform.offsetMin = new Vector2(10f, 8f);
            _historyText.rectTransform.offsetMax = new Vector2(-10f, -8f);
            _historyText.raycastTarget = false;

            _inputRoot = new GameObject("ChatInputRoot", typeof(RectTransform), typeof(Image));
            RectTransform inputRt = _inputRoot.GetComponent<RectTransform>();
            inputRt.SetParent(canvas.transform, false);
            inputRt.anchorMin = new Vector2(0f, 0f);
            inputRt.anchorMax = new Vector2(0f, 0f);
            inputRt.pivot = new Vector2(0f, 0f);
            inputRt.anchoredPosition = new Vector2(24f, 388f);
            inputRt.sizeDelta = new Vector2(520f, 30f);
            _inputRoot.GetComponent<Image>().color = InputBgColor;

            _inputField = _inputRoot.AddComponent<InputField>();
            Text textComp = UIFactory.CreateText(inputRt, "", 17, TextColor,
                TextAnchor.MiddleLeft, FontStyle.Normal, "InputText");
            textComp.rectTransform.offsetMin = new Vector2(10f, 0f);
            textComp.rectTransform.offsetMax = new Vector2(-10f, 0f);
            textComp.supportRichText = false;
            _inputField.textComponent = textComp;

            Text placeholder = UIFactory.CreateText(inputRt, "Type a message...", 17,
                new Color(0.6f, 0.6f, 0.62f, 1f), TextAnchor.MiddleLeft, FontStyle.Italic, "Placeholder");
            placeholder.rectTransform.offsetMin = new Vector2(10f, 0f);
            placeholder.rectTransform.offsetMax = new Vector2(-10f, 0f);
            _inputField.placeholder = placeholder;

            _inputField.lineType = InputField.LineType.SingleLine;
            _inputField.characterLimit = ChatNetwork.MaxMessageLength;
            _inputRoot.SetActive(false);

            AddMessage("System", "Press ENTER to chat.");
        }

        private void OpenChat()
        {
            _open = true;
            _openedFrame = Time.frameCount;
            _inputRoot.SetActive(true);
            _inputField.text = "";
            EventSystem.current?.SetSelectedGameObject(_inputField.gameObject);
            _inputField.ActivateInputField();

            PlayerInputProvider.LocalOrAny()?.SetMovementLocked(true);
            GameEvents.RaiseChatInputChanged(true);
        }

        private void SendAndClose()
        {
            string text = _inputField != null ? _inputField.text : "";
            string name = ResolveLocalName();
            CloseChat();
            if (string.IsNullOrWhiteSpace(text)) return;

            bool sent = ChatNetwork.Send(name, text);
            if (!sent) GameEvents.RaiseShowNotification("Chatting too fast — slow down.");
        }

        private void CloseChat()
        {
            _open = false;
            if (_inputField != null) _inputField.text = "";
            if (_inputRoot != null) _inputRoot.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            PlayerInputProvider.LocalOrAny()?.SetMovementLocked(false);
            GameEvents.RaiseChatInputChanged(false);
        }

        private void AddMessage(string sender, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            string stamp = DateTime.Now.ToString("HH:mm");
            _lines.Enqueue(string.Format("[{0}] [{1}]: {2}", stamp, sender, message));
            while (_lines.Count > MaxLines) _lines.Dequeue();
            if (_historyText != null) _historyText.text = string.Join("\n", _lines.ToArray());
        }

        private static string ResolveLocalName()
        {
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && players[i].IsOwner && !string.IsNullOrEmpty(players[i].PlayerName.Value))
                    return players[i].PlayerName.Value;
            }
            return "Player";
        }
    }
}
