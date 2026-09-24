using System;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Transporting;
using UnityEngine;

namespace Davidmon.Chat
{
    /// <summary>
    /// Milestone 13 (part 1) — chat message transported over FishNet Broadcasts.
    /// Broadcasts need no NetworkObject: clients send to the server, the server
    /// relay in <see cref="Multiplayer.NetworkBootstrap"/> re-broadcasts to all.
    /// </summary>
    public struct ChatBroadcast : IBroadcast
    {
        public string Sender;
        public string Message;
    }

    /// <summary>
    /// Static chat transport. Offline (no client running) it echoes locally so
    /// single-player still sees its own messages. Includes sender/message
    /// sanitizing plus a small send-rate limit.
    /// </summary>
    public static class ChatNetwork
    {
        public const int MaxMessageLength = 120;
        public const int MaxNameLength = 24;
        private const float SendCooldownSeconds = 0.5f;

        /// <summary>Raised on every client for each chat line (sender, message).</summary>
        public static event Action<string, string> MessageReceived;

        private static bool _clientHandlerRegistered;
        private static float _lastSendUnscaled = -10f;

        /// <summary>Registers the client broadcast handler (safe to call often).</summary>
        public static void EnsureClientHandler()
        {
            if (_clientHandlerRegistered) return;
            ClientManager cm = null;
            try { cm = InstanceFinder.ClientManager; } catch (Exception) { cm = null; }
            if (cm == null) return;
            try
            {
                cm.RegisterBroadcast<ChatBroadcast>(OnChatReceived);
                _clientHandlerRegistered = true;
            }
            catch (Exception e) { Debug.LogWarning("[ChatNetwork] Handler registration skipped: " + e.Message); }
        }

        private static void OnChatReceived(ChatBroadcast msg, Channel channel)
        {
            MessageReceived?.Invoke(SanitizeName(msg.Sender), SanitizeMessage(msg.Message));
        }

        /// <summary>Sends a chat line. Returns false when rate-limited.</summary>
        public static bool Send(string sender, string message)
        {
            string cleanSender = SanitizeName(sender);
            string cleanMessage = SanitizeMessage(message);
            if (string.IsNullOrEmpty(cleanMessage)) return true;

            if (Time.unscaledTime - _lastSendUnscaled < SendCooldownSeconds) return false;
            _lastSendUnscaled = Time.unscaledTime;

            bool online = false;
            try { online = InstanceFinder.IsClientStarted; } catch (Exception) { online = false; }

            if (!online)
            {
                // Single-player / offline: local echo only.
                MessageReceived?.Invoke(cleanSender, cleanMessage);
                return true;
            }

            EnsureClientHandler();
            try
            {
                InstanceFinder.ClientManager.Broadcast(new ChatBroadcast
                {
                    Sender = cleanSender,
                    Message = cleanMessage
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ChatNetwork] Send failed: " + e.Message);
                MessageReceived?.Invoke(cleanSender, cleanMessage);
            }
            return true;
        }

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Player";
            string clean = name.Trim();
            if (clean.Length > MaxNameLength) clean = clean.Substring(0, MaxNameLength);
            return clean;
        }

        public static string SanitizeMessage(string message)
        {
            if (message == null) return "";
            string clean = message.Trim();
            if (clean.Length > MaxMessageLength) clean = clean.Substring(0, MaxMessageLength);
            return clean;
        }
    }
}
