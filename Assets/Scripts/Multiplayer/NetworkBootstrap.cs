using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Davidmon.Chat;
using Davidmon.UI;

namespace Davidmon.Multiplayer
{
    /// <summary>
    /// Milestone 11 — FishNet session bootstrap (player synchronization wiring).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Guarantees a <see cref="NetworkManager"/> + Tugboat transport exists,
    /// creating one at runtime if the scene does not define it.</item>
    /// <item>Spawns one <see cref="NetworkPlayer"/> avatar per connection from the
    /// assigned player prefab (server-authoritative spawn).</item>
    /// <item>Relays <see cref="ChatBroadcast"/> chat messages to all clients.</item>
    /// <item>Offers a small development Host / Server / Client / Stop panel so two
    /// editors or builds can join the same TestArena quickly.</item>
    /// </list>
    ///
    /// Single-player is untouched: while no server/client is running, the
    /// scene-placed player keeps working. Starting Host/Server parks it and
    /// stopping the session restores it.
    ///
    /// Milestone 12 follow-ups (not done here): server-authoritative EXP, coins,
    /// purchases, evolution, enemy/boss state. Those must go through ServerRpcs on
    /// the server before being applied — never trust client-sent reward values.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [Header("Spawning")]
        [Tooltip("Player prefab with NetworkObject + NetworkTransform + NetworkPlayer. Must also be registered in the FishNet spawnable prefabs.")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Development GUI")]
        [SerializeField] private bool showDevGui = true;
        [SerializeField] private string defaultAddress = "localhost";
        [SerializeField] private ushort defaultPort = 7770;

        private NetworkManager _net;
        private GameObject _scenePlayer;
        private Vector3 _spawnOrigin = Vector3.zero;
        private readonly HashSet<NetworkConnection> _spawned = new HashSet<NetworkConnection>();
        private readonly HashSet<int> _spawnedClientIds = new HashSet<int>();

        private bool _relayRegistered;
        private string _address;
        private ushort _port;
        private int _spawnIndex;

        [Header("Dev panel layout")]
        private const float PanelWidth = 280f;
        private const float PanelHeight = 352f;
        private const float TitleHeight = 32f;

        private Canvas _netCanvas;
        private RectTransform _panelRt;
        private GameObject _contentRoot;
        private Text _statusText;
        private Text _playersText;
        private Text _warnText;
        private InputField _addressField;
        private InputField _portField;
        private Button _hostBtn;
        private Button _serverBtn;
        private Button _clientBtn;
        private Button _stopBtn;
        private Button _collapseBtn;
        private Text _collapseLabel;
        private bool _collapsed;

        private void Awake()
        {
            _address = defaultAddress;
            _port = defaultPort;

            _net = FishNet.InstanceFinder.NetworkManager;
            GameObject managerGo = null;
            if (_net == null)
            {
                managerGo = new GameObject("NetworkManager (Runtime)");
                _net = managerGo.AddComponent<NetworkManager>();
                managerGo.AddComponent<Tugboat>();
            }
            else managerGo = _net.gameObject;

            // Milestone 12: server-side record keeper lives next to the manager.
            // (Its shop-stock reference is assigned in the scene; without it,
            // purchases fail closed with "Shop unavailable".)
            if (managerGo != null && managerGo.GetComponent<ServerGameState>() == null)
                managerGo.AddComponent<ServerGameState>();

            GameObject tagged = null;
            try { tagged = GameObject.FindGameObjectWithTag("Player"); }
            catch (Exception) { tagged = null; }
            if (tagged != null)
            {
                _scenePlayer = tagged;
                _spawnOrigin = tagged.transform.position;
            }

            BuildNetPanel();
        }

        private void OnEnable()
        {
            if (_net == null) return;
            _net.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _net.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnDisable()
        {
            if (_net == null) return;
            _net.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _net.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                RegisterChatRelay();
                if (_scenePlayer != null) _scenePlayer.SetActive(false);
                // Host edge: the local client may already be connected before any
                // remote event fires — spawn for everyone currently connected.
                SpawnForAllClients();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped
                || args.ConnectionState == LocalConnectionState.Stopping)
            {
                _spawned.Clear();
                _spawnedClientIds.Clear();
                if (_scenePlayer != null) _scenePlayer.SetActive(true);
            }
        }

        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
                SpawnPlayer(conn);
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                _spawned.Remove(conn);
                if (conn != null) _spawnedClientIds.Remove(conn.ClientId);
            }
        }

        private void SpawnForAllClients()
        {
            if (_net == null || _net.ServerManager == null || !_net.ServerManager.Started) return;
            foreach (KeyValuePair<int, NetworkConnection> kvp in _net.ServerManager.Clients)
            {
                if (kvp.Value != null) SpawnPlayer(kvp.Value);
            }
        }

        /// <summary>Server-authoritative player spawn for one connection.</summary>
        private void SpawnPlayer(NetworkConnection conn)
        {
            if (conn == null || _spawned.Contains(conn)) return;
            if (playerPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] No player prefab assigned — cannot spawn network player.", this);
                return;
            }

            _spawned.Add(conn);
            _spawnedClientIds.Add(conn.ClientId);

            float angle = _spawnIndex * 2.39996f; // golden angle: spreads spawns out
            float radius = 1.5f + 0.75f * (_spawnIndex / 6);
            Vector3 pos = _spawnOrigin + new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
            _spawnIndex++;

            GameObject go = Instantiate(playerPrefab, pos, Quaternion.identity);
            go.name = "Player_Client" + conn.ClientId;
            _net.ServerManager.Spawn(go, conn);
        }

        private void RegisterChatRelay()
        {
            if (_relayRegistered || _net == null || _net.ServerManager == null) return;
            _relayRegistered = true;
            _net.ServerManager.RegisterBroadcast<ChatBroadcast>(OnChatFromClient);
        }

        /// <summary>Server relay: validates length, then re-broadcasts to everyone.</summary>
        private void OnChatFromClient(NetworkConnection conn, ChatBroadcast msg, Channel channel)
        {
            if (string.IsNullOrWhiteSpace(msg.Message)) return;
            var clean = new ChatBroadcast
            {
                Sender = ChatNetwork.SanitizeName(msg.Sender),
                Message = ChatNetwork.SanitizeMessage(msg.Message)
            };
            _net.ServerManager.Broadcast(clean);
        }

        /// <summary>
        /// Builds the dev panel as uGUI (same approach as the other screens) so it
        /// anchors inside the screen on any resolution. Drag it by the title bar;
        /// it stays clamped on screen and can be collapsed with the [-] button.
        /// </summary>
        private void BuildNetPanel()
        {
            UIFactory.EnsureEventSystem();
            _netCanvas = UIFactory.CreateCanvas("NetCanvas", 200);

            _panelRt = UIFactory.CreatePanel(_netCanvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), 0f, 0f,
                new Color(0.06f, 0.06f, 0.09f, 0.94f));
            _panelRt.name = "NetPanel";
            _panelRt.pivot = new Vector2(1f, 1f);
            _panelRt.anchoredPosition = new Vector2(-10f, -10f);
            _panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            // Title bar (drag handle).
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Image));
            RectTransform titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.SetParent(_panelRt, false);
            PlaceTopLeft(titleRt, 0f, 0f, PanelWidth, TitleHeight);
            titleGo.GetComponent<Image>().color = new Color(0.13f, 0.14f, 0.20f, 1f);

            Text title = UIFactory.CreateText(titleRt, "Davidmon Net", 16,
                new Color(0.93f, 0.93f, 0.95f, 1f), TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
            title.rectTransform.offsetMin = new Vector2(10f, 0f);
            title.rectTransform.offsetMax = new Vector2(-40f, 0f);

            _collapseBtn = UIFactory.CreateButton(titleRt, new Vector2(28f, 24f), "-",
                18, ToggleCollapsed, new Color(0.25f, 0.27f, 0.35f, 1f),
                new Color(1f, 1f, 1f, 1f), "Collapse");
            RectTransform collapseRt = _collapseBtn.GetComponent<RectTransform>();
            collapseRt.anchorMin = new Vector2(1f, 0.5f);
            collapseRt.anchorMax = new Vector2(1f, 0.5f);
            collapseRt.pivot = new Vector2(1f, 0.5f);
            collapseRt.anchoredPosition = new Vector2(-4f, 0f);
            _collapseLabel = _collapseBtn.GetComponentInChildren<Text>();

            var drag = titleGo.AddComponent<PanelDrag>();
            drag.Init(_panelRt, _netCanvas);

            // Content.
            _contentRoot = new GameObject("Content", typeof(RectTransform));
            RectTransform contentRt = _contentRoot.GetComponent<RectTransform>();
            contentRt.SetParent(_panelRt, false);
            PlaceTopLeft(contentRt, 0f, TitleHeight, PanelWidth, PanelHeight - TitleHeight);

            _statusText = UIFactory.CreateText(contentRt, "Status: OFFLINE", 15,
                new Color(0.93f, 0.93f, 0.95f, 1f), TextAnchor.UpperLeft, FontStyle.Bold, "Status");
            PlaceTopLeft(_statusText.rectTransform, 10f, 6f, PanelWidth - 20f, 22f);

            _playersText = UIFactory.CreateText(contentRt, "", 14,
                new Color(0.70f, 0.70f, 0.72f, 1f), TextAnchor.UpperLeft, FontStyle.Normal, "Players");
            PlaceTopLeft(_playersText.rectTransform, 10f, 28f, PanelWidth - 20f, 20f);

            Text addrLabel = UIFactory.CreateText(contentRt, "Address", 13,
                new Color(0.70f, 0.70f, 0.72f, 1f), TextAnchor.LowerLeft, FontStyle.Normal, "AddrLabel");
            PlaceTopLeft(addrLabel.rectTransform, 10f, 50f, PanelWidth - 20f, 16f);
            _addressField = CreateField(contentRt, 68f, _address ?? "localhost");
            _addressField.onEndEdit.AddListener(v => _address = v);

            Text portLabel = UIFactory.CreateText(contentRt, "Port", 13,
                new Color(0.70f, 0.70f, 0.72f, 1f), TextAnchor.LowerLeft, FontStyle.Normal, "PortLabel");
            PlaceTopLeft(portLabel.rectTransform, 10f, 100f, PanelWidth - 20f, 16f);
            _portField = CreateField(contentRt, 118f, _port.ToString());
            _portField.onEndEdit.AddListener(v => { if (ushort.TryParse(v, out ushort p)) _port = p; });

            float btnW = (PanelWidth - 20f - 8f) * 0.5f;
            _hostBtn = UIFactory.CreateButton(contentRt, new Vector2(btnW, 30f), "Host",
                15, StartHost, new Color(0.20f, 0.55f, 0.30f, 1f), Color.white, "Host");
            PlaceTopLeft(_hostBtn.GetComponent<RectTransform>(), 10f, 152f, btnW, 30f);
            _serverBtn = UIFactory.CreateButton(contentRt, new Vector2(btnW, 30f), "Server",
                15, StartServerOnly, new Color(0.35f, 0.37f, 0.45f, 1f), Color.white, "Server");
            PlaceTopLeft(_serverBtn.GetComponent<RectTransform>(), 10f + btnW + 8f, 152f, btnW, 30f);
            _clientBtn = UIFactory.CreateButton(contentRt, new Vector2(btnW, 30f), "Client",
                15, StartClientOnly, new Color(0.16f, 0.42f, 0.72f, 1f), Color.white, "Client");
            PlaceTopLeft(_clientBtn.GetComponent<RectTransform>(), 10f, 188f, btnW, 30f);
            _stopBtn = UIFactory.CreateButton(contentRt, new Vector2(btnW, 30f), "Stop",
                15, StopAll, new Color(0.60f, 0.25f, 0.25f, 1f), Color.white, "Stop");
            PlaceTopLeft(_stopBtn.GetComponent<RectTransform>(), 10f + btnW + 8f, 188f, btnW, 30f);

            _warnText = UIFactory.CreateText(contentRt, "WARNING: player prefab not assigned.", 13,
                new Color(1f, 0.6f, 0.3f, 1f), TextAnchor.UpperLeft, FontStyle.Normal, "Warn");
            PlaceTopLeft(_warnText.rectTransform, 10f, 224f, PanelWidth - 20f, 40f);

            _netCanvas.gameObject.SetActive(showDevGui);
            RefreshNetPanel();
        }

        private InputField CreateField(Transform parent, float yTop, string value)
        {
            var go = new GameObject("Field", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            PlaceTopLeft(rt, 10f, yTop, PanelWidth - 20f, 26f);
            go.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f, 1f);

            var field = go.AddComponent<InputField>();
            Text text = UIFactory.CreateText(rt, value ?? "", 14,
                new Color(0.93f, 0.93f, 0.95f, 1f), TextAnchor.MiddleLeft, FontStyle.Normal, "Text");
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            text.rectTransform.offsetMax = new Vector2(-8f, 0f);
            field.textComponent = text;
            field.lineType = InputField.LineType.SingleLine;
            field.text = value ?? "";
            return field;
        }

        private static void PlaceTopLeft(RectTransform rt, float x, float yTop, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yTop);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            if (_contentRoot != null) _contentRoot.SetActive(!_collapsed);
            if (_collapseLabel != null) _collapseLabel.text = _collapsed ? "+" : "-";
            if (_panelRt != null)
                _panelRt.sizeDelta = new Vector2(PanelWidth, _collapsed ? TitleHeight + 6f : PanelHeight);
        }

        private void Update()
        {
            RefreshNetPanel();
        }

        /// <summary>Keeps labels/buttons in sync and the panel inside the screen.</summary>
        private void RefreshNetPanel()
        {
            if (_netCanvas == null || _panelRt == null) return;
            if (_netCanvas.gameObject.activeSelf != showDevGui)
                _netCanvas.gameObject.SetActive(showDevGui);
            if (!showDevGui) return;

            bool serverOn = _net != null && _net.ServerManager != null && _net.ServerManager.Started;
            bool clientOn = _net != null && _net.ClientManager != null && _net.ClientManager.Started;

            if (_statusText != null)
                _statusText.text = serverOn
                    ? (clientOn ? "Status: HOST" : "Status: SERVER")
                    : (clientOn ? "Status: CLIENT" : "Status: OFFLINE");
            if (_playersText != null)
            {
                _playersText.gameObject.SetActive(serverOn);
                if (serverOn) _playersText.text = "Players: " + _net.ServerManager.Clients.Count;
            }
            if (_hostBtn != null) _hostBtn.gameObject.SetActive(!serverOn);
            if (_serverBtn != null) _serverBtn.gameObject.SetActive(!serverOn);
            if (_clientBtn != null) _clientBtn.gameObject.SetActive(!clientOn);
            if (_stopBtn != null) _stopBtn.gameObject.SetActive(serverOn || clientOn);
            if (_warnText != null) _warnText.gameObject.SetActive(playerPrefab == null);

            ClampPanelToScreen();
        }

        private void ClampPanelToScreen()
        {
            if (_panelRt == null || _netCanvas == null) return;
            float s = Mathf.Max(0.01f, _netCanvas.scaleFactor);
            float w = _panelRt.sizeDelta.x;
            float h = _panelRt.sizeDelta.y;
            float scrW = Screen.width / s;
            float scrH = Screen.height / s;
            const float m = 8f;

            float minX = scrW > w ? m + w - scrW : (w - scrW) * 0.5f;
            float maxX = scrW > w ? -m : (w - scrW) * 0.5f;
            float minY = scrH > h ? m + h - scrH : (h - scrH) * 0.5f;
            float maxY = scrH > h ? -m : (h - scrH) * 0.5f;

            Vector2 p = _panelRt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
            p.y = Mathf.Clamp(p.y, Mathf.Min(minY, maxY), Mathf.Max(minY, maxY));
            _panelRt.anchoredPosition = p;
        }

        /// <summary>Title-bar drag handle that keeps the panel on screen.</summary>
        private sealed class PanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private RectTransform _panel;
            private Canvas _canvas;
            private Vector2 _grabOffset;

            public void Init(RectTransform panel, Canvas canvas)
            {
                _panel = panel;
                _canvas = canvas;
            }

            public void OnBeginDrag(PointerEventData e)
            {
                if (_panel == null || _canvas == null || e == null) return;
                _grabOffset = _panel.anchoredPosition - ScreenToAnchor(e.position);
            }

            public void OnDrag(PointerEventData e)
            {
                if (_panel == null || _canvas == null || e == null) return;
                _panel.anchoredPosition = ScreenToAnchor(e.position) + _grabOffset;
            }

            private Vector2 ScreenToAnchor(Vector2 screenPos)
            {
                float s = Mathf.Max(0.01f, _canvas.scaleFactor);
                float scrW = Screen.width / s;
                float scrH = Screen.height / s;
                // Anchors/pivot are top-right: anchor corner is (scrW, scrH).
                return new Vector2(screenPos.x / s - scrW, screenPos.y / s - scrH);
            }
        }

        public void StartHost()
        {
            if (_net == null) return;
            ApplyTransportSettings();
            _net.ServerManager.StartConnection(_port);
            _net.ClientManager.StartConnection(_address, _port);
        }

        public void StartServerOnly()
        {
            if (_net == null) return;
            ApplyTransportSettings();
            _net.ServerManager.StartConnection(_port);
        }

        public void StartClientOnly()
        {
            if (_net == null) return;
            ApplyTransportSettings();
            _net.ClientManager.StartConnection(_address, _port);
        }

        public void StopAll()
        {
            if (_net == null) return;
            try { _net.ClientManager.StopConnection(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            try { _net.ServerManager.StopConnection(true); } catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        private void ApplyTransportSettings()
        {
            if (_net == null || _net.TransportManager == null || _net.TransportManager.Transport == null) return;
            Transport t = _net.TransportManager.Transport;
            try
            {
                if (!string.IsNullOrWhiteSpace(_address)) t.SetClientAddress(_address.Trim());
                t.SetPort(_port);
            }
            catch (Exception e) { Debug.LogWarning("[NetworkBootstrap] Transport settings skipped: " + e.Message); }
        }
    }
}
