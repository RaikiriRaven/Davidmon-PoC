using UnityEngine;
using Davidmon.Core;
using Davidmon.Player;

namespace Davidmon.NPC
{
    /// <summary>
    /// Plain (non-MonoBehaviour) conversation state machine, registered as a singleton
    /// through the <see cref="ServiceLocator"/> so any system can open or advance a
    /// conversation without editor references. The UI listens to the raised
    /// <see cref="GameEvents"/> dialogue channels; this service owns the rules.
    /// </summary>
    public sealed class DialogueSystem
    {
        private Npc _current;
        private int _lineIndex;
        private bool _showingChoices;
        private bool _waitingForClose;

        public bool IsActive => _current != null && _current.Data != null;
        public bool IsShowingChoices => _showingChoices;
        public Npc Current => _current;

        /// <summary>Starts a conversation with an NPC. No-ops when one is already open.</summary>
        public void Open(Npc npc)
        {
            if (npc == null || npc.Data == null || IsActive) return;

            _current = npc;
            _lineIndex = 0;
            _showingChoices = false;
            _waitingForClose = false;

            GameEvents.RaiseDialogueOpened(npc.Data);
            if (npc.Data.IntroLines != null && npc.Data.IntroLines.Length > 0)
                RaiseLine();
            else
                CompleteIntro();
        }

        /// <summary>Advances to the next line, shows choices, or closes (E key).</summary>
        public void Advance()
        {
            if (!IsActive || _showingChoices) return;

            if (_waitingForClose)
            {
                Close();
                return;
            }

            if (_lineIndex + 1 < _current.Data.IntroLines.Length)
            {
                _lineIndex++;
                RaiseLine();
            }
            else
            {
                CompleteIntro();
            }
        }

        /// <summary>Selects a choice button by index and resolves its effect.</summary>
        public void SelectChoice(int index)
        {
            if (!IsActive || !_showingChoices) return;

            DialogueChoice[] choices = _current.Data.Choices;
            if (choices == null || index < 0 || index >= choices.Length) return;

            DialogueChoice choice = choices[index];
            _showingChoices = false;
            GameEvents.RaiseDialogueChoiceSelected(_current.Data, choice);

            if (choice.Action == DialogueAction.Close ||
                (choice.Action == DialogueAction.None && string.IsNullOrEmpty(choice.Reply)))
            {
                Close();
                return;
            }

            HandleAction(choice);

            if (_waitingForClose) return;
            if (!string.IsNullOrEmpty(choice.Reply))
            {
                _waitingForClose = true;
                GameEvents.RaiseDialogueReplyShown(_current.Data, choice.Reply);
            }
            else
            {
                Close();
            }
        }

        /// <summary>Closes the active conversation. No-op when none is open.</summary>
        public void Close()
        {
            if (!IsActive) return;

            NpcData data = _current.Data;
            _current = null;
            _showingChoices = false;
            _waitingForClose = false;
            GameEvents.RaiseDialogueClosed(data);
        }

        private void RaiseLine()
        {
            GameEvents.RaiseDialogueLineChanged(_current.Data, _lineIndex);
        }

        private void CompleteIntro()
        {
            if (_current.Data.Choices != null && _current.Data.Choices.Length > 0)
            {
                _showingChoices = true;
                GameEvents.RaiseDialogueChoicesShown(_current.Data);
            }
            else
            {
                _waitingForClose = true;
                GameEvents.RaiseDialogueReplyShown(_current.Data, "");
            }
        }

        private void HandleAction(DialogueChoice choice)
        {
            switch (choice.Action)
            {
                case DialogueAction.Heal:
                    var pm = ServiceLocator.Get<PlayerManager>();
                    if (pm != null && pm.HasCreature)
                    {
                        pm.HealActiveToFull();
                        GameEvents.RaiseShowNotification(
                            pm.ActiveCreature.Data.DisplayName + " is fully restored!");
                    }
                    break;

                case DialogueAction.Shop:
                    NpcData merchant = _current != null ? _current.Data : null;
                    Close();
                    if (merchant != null) GameEvents.RaiseShopRequested(merchant);
                    _waitingForClose = true;
                    break;

                case DialogueAction.Quest:
                    GameEvents.RaiseShowNotification("No active quests right now.");
                    break;
            }
        }
    }
}