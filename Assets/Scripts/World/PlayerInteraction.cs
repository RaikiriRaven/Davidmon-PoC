using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.NPC;
using Davidmon.Player;
using Davidmon.UI;

namespace Davidmon.World
{
    /// <summary>
    /// Player-side interaction hub. Polls the NPC and enemy registries every frame,
    /// shows a context prompt for the nearest in-range target and routes the Interact
    /// keypress to it. While a dialogue is open the prompt is hidden and E is handled
    /// by the dialogue UI instead.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        private static readonly Color PromptBackColor = new Color(0f, 0f, 0f, 0.55f);
        private const float NpcSearchRange = 8f;
        private const float EnemySearchRange = 24f;

        [SerializeField] private PlayerInputProvider input;

        private PlayerInputProvider _input;
        private DialogueSystem _dialogue;
        private Canvas _canvas;
        private Image _promptBg;
        private Text _promptText;

        private IInteractable _currentTarget;
        private bool _hasPrompt;

        private void Awake()
        {
            _input = input != null ? input : GetComponent<PlayerInputProvider>();
            if (_input == null) _input = GetComponentInChildren<PlayerInputProvider>();
            _dialogue = ServiceLocator.GetOrCreate(() => new DialogueSystem());

            UIFactory.EnsureEventSystem();
            BuildPromptUi();
            HidePrompt();
        }

        private void OnEnable()
        {
            if (_input != null) _input.InteractPressed += OnInteractPressed;
        }

        private void OnDisable()
        {
            if (_input != null) _input.InteractPressed -= OnInteractPressed;
        }

        private void Update()
        {
            if (_dialogue != null && _dialogue.IsActive)
            {
                ClearTarget();
                return;
            }

            Vector3 origin = _input != null ? _input.transform.position : transform.position;

            Npc npc = NPCRegistry.Nearest(origin, NpcSearchRange, requireInRange: true);
            RoamingEnemy enemy = EnemyRegistry.Nearest(origin, EnemySearchRange, requireInRange: true);

            IInteractable target = null;
            float npcDist = npc != null ? (npc.transform.position - origin).sqrMagnitude : float.MaxValue;
            float enemyDist = enemy != null ? (enemy.transform.position - origin).sqrMagnitude : float.MaxValue;

            if (npc != null && npcDist <= enemyDist) target = npc;
            else if (enemy != null) target = enemy;

            if (target != null && target.CanInteract)
                ShowTarget(target);
            else
                ClearTarget();
        }

        private void OnInteractPressed()
        {
            if (_dialogue != null && _dialogue.IsActive) return;
            if (_currentTarget != null && _currentTarget.CanInteract)
                _currentTarget.Interact();
        }

        private void ShowTarget(IInteractable target)
        {
            _currentTarget = target;
            string verb = target is Npc ? "talk to" : "engage";
            _promptText.text = "Press E to " + verb + " " + target.DisplayName;
            _hasPrompt = true;
            ShowPrompt();
        }

        private void ClearTarget()
        {
            _currentTarget = null;
            _hasPrompt = false;
            HidePrompt();
        }

        private void BuildPromptUi()
        {
            _canvas = UIFactory.CreateCanvas("InteractPromptCanvas", 60);

            RectTransform bg = UIFactory.CreateCenteredPanel(_canvas.transform, new Vector2(620f, 56f), PromptBackColor);
            bg.anchorMin = new Vector2(0.5f, 0f);
            bg.anchorMax = new Vector2(0.5f, 0f);
            bg.pivot = new Vector2(0.5f, 0f);
            bg.anchoredPosition = new Vector2(0f, 140f);
            _promptBg = bg.GetComponent<Image>();
            _promptBg.raycastTarget = false;

            _promptText = UIFactory.CreateText(bg, "", 22, new Color(0.95f, 0.95f, 0.92f, 1f),
                TextAnchor.MiddleCenter, FontStyle.Bold, "Prompt");
            _promptText.rectTransform.offsetMin = Vector2.zero;
            _promptText.rectTransform.offsetMax = Vector2.zero;
            _promptText.raycastTarget = false;
        }

        private void ShowPrompt()
        {
            if (_promptBg != null) _promptBg.gameObject.SetActive(true);
        }

        private void HidePrompt()
        {
            if (_promptBg != null) _promptBg.gameObject.SetActive(false);
        }
    }
}