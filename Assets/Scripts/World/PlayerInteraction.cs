using UnityEngine;
using UnityEngine.UI;
using Davidmon.Core;
using Davidmon.Player;
using Davidmon.UI;

namespace Davidmon.World
{
    /// <summary>
    /// Player-side interaction hub. Displays the context prompt raised by nearby
    /// <see cref="IInteractable"/>s via <see cref="GameEvents.InteractPrompt"/> and routes
    /// the Interact keypress to the nearest in-range enemy (or any IInteractable target).
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        private static readonly Color PromptBackColor = new Color(0f, 0f, 0f, 0.55f);

        [SerializeField] private PlayerInputProvider input;

        private PlayerInputProvider _input;
        private Canvas _canvas;
        private Image _promptBg;
        private Text _promptText;

        private bool _hasPrompt;

        private void Awake()
        {
            _input = input != null ? input : GetComponent<PlayerInputProvider>();
            if (_input == null) _input = GetComponentInChildren<PlayerInputProvider>();

            UIFactory.EnsureEventSystem();
            BuildPromptUi();
            HidePrompt();
        }

        private void OnEnable()
        {
            GameEvents.InteractPrompt += OnInteractionPrompt;
            if (_input != null) _input.InteractPressed += OnInteractPressed;
        }

        private void OnDisable()
        {
            GameEvents.InteractPrompt -= OnInteractionPrompt;
            if (_input != null) _input.InteractPressed -= OnInteractPressed;
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

        private void OnInteractionPrompt(string message)
        {
            _hasPrompt = !string.IsNullOrEmpty(message);
            if (_hasPrompt)
            {
                _promptText.text = message;
                ShowPrompt();
            }
            else
            {
                HidePrompt();
            }
        }

        private void OnInteractPressed()
        {
            if (!_hasPrompt) return;

            Vector3 origin = _input != null ? _input.transform.position : transform.position;
            RoamingEnemy target = EnemyRegistry.Nearest(origin, 20f, requireInRange: true);
            if (target != null && target.CanInteract)
                target.Interact();
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