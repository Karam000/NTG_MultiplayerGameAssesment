using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace NTG
{
    public static class SessionManager
    {
        public const int MaxPlayers = 8;
        public const string SessionType = "ntg.match";

        public const string ReadyKey = "ready";
        public const string ServerIpKey = "serverIp";
        public const string ServerPortKey = "serverPort";

        public static ISession CurrentSession { get; private set; }
        public static string LastError { get; private set; }

        public static bool InSession => CurrentSession != null;
        public static bool IsLeader => InSession && CurrentSession.IsHost;
        public static string LocalPlayerId => AuthenticationService.Instance?.PlayerId;

        public static async Task<bool> Initialize()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[SESSION] Init failed: {e.Message}");
                return false;
            }
        }

        public static async Task<bool> QuickJoin()
        {
            if (!await Initialize()) return false;
            await LeaveSession();

            try
            {
                var sessionOptions = new SessionOptions
                {
                    Name = "NTG Match",
                    Type = SessionType,
                    MaxPlayers = MaxPlayers,
                    IsPrivate = false // private sessions are not visible to quick join
                };
                var quickJoinOptions = new QuickJoinOptions
                {
                    Timeout = TimeSpan.FromSeconds(5),
                    CreateSession = true
                };
                CurrentSession = await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoinOptions, sessionOptions);
                Debug.Log($"[SESSION] In session {CurrentSession.Id}, players={CurrentSession.PlayerCount}, isHost={CurrentSession.IsHost}");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[SESSION] QuickJoin failed: {e.Message}");
                return false;
            }
        }

        public static async Task LeaveSession()
        {
            if (CurrentSession == null) return;
            try
            {
                await CurrentSession.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SESSION] Leave failed: {e.Message}");
            }
            CurrentSession = null;
        }

        public static async Task SetReady(bool ready)
        {
            if (!InSession) return;
            try
            {
                CurrentSession.CurrentPlayer.SetProperty(ReadyKey, new PlayerProperty(ready ? "1" : "0"));
                await CurrentSession.SaveCurrentPlayerDataAsync();
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[SESSION] SetReady failed: {e.Message}");
            }
        }

        public static bool IsPlayerReady(IReadOnlyPlayer player)
        {
            return player.Properties.TryGetValue(ReadyKey, out var prop) && prop.Value == "1";
        }

        public static string PlayerName(IReadOnlyPlayer player)
        {
            string id = player.Id;
            return "Player-" + (id.Length > 6 ? id.Substring(0, 6) : id);
        }

        // Leader only: publish the dedicated server's public endpoint to the session.
        public static async Task<bool> SetServerEndpoint(string ip, ushort port)
        {
            if (!IsLeader) return false;
            try
            {
                var host = CurrentSession.AsHost();
                host.SetProperty(ServerIpKey, new SessionProperty(ip));
                host.SetProperty(ServerPortKey, new SessionProperty(port.ToString()));
                await host.SavePropertiesAsync();
                Debug.Log($"[SESSION] Published server endpoint {ip}:{port}");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[SESSION] SetServerEndpoint failed: {e.Message}");
                return false;
            }
        }

        public static bool TryGetServerEndpoint(out string ip, out ushort port)
        {
            ip = null;
            port = 0;
            if (!InSession) return false;

            var props = CurrentSession.Properties;
            if (!props.TryGetValue(ServerIpKey, out var ipProp) || string.IsNullOrEmpty(ipProp.Value)) return false;
            if (!props.TryGetValue(ServerPortKey, out var portProp) || !ushort.TryParse(portProp.Value, out port)) return false;

            ip = ipProp.Value;
            return true;
        }

        // Leader only: clear the published endpoint (e.g. when the match ends)
        // so returning to the lobby does not auto-reconnect to a stale server.
        public static async Task ClearServerEndpoint()
        {
            if (!IsLeader) return;
            try
            {
                var host = CurrentSession.AsHost();
                host.SetProperty(ServerIpKey, null);
                host.SetProperty(ServerPortKey, null);
                await host.SavePropertiesAsync();
                Debug.Log("[SESSION] Cleared server endpoint");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SESSION] ClearServerEndpoint failed: {e.Message}");
            }
        }
    }
}
