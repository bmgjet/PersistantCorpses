using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Persistant Corpses", "bmgjet", "1.0.0")]
    [Description("Player Corpses don't despawn unless there health is 0.")]
    public class PersistantCorpses : RustPlugin
    {
        #region Vars
        private const string permClean = "PersistantCorpses.clean";
        private const string permCount = "PersistantCorpses.count";
        private const string permView = "PersistantCorpses.view";
        private BasePlayer player;
        private Coroutine _routine;
        private float RefreshTimer = 6f;
        private Dictionary<BaseNetworkable, Vector3> Corpses = new Dictionary<BaseNetworkable, Vector3> { };
        #endregion

        #region Language
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
            {"Info", "There are {0} corpses on map!"},
            {"Clean", "{0} corpses have been removed from the map!"},
            {"Stop", "Stopped corpse view!"},
            {"Start", "Started corpse view!"},
            {"Permission", "You need permission to do that!"}
            }, this);
        }

        private void message(BasePlayer chatplayer, string key, params object[] args)
        {
            if (chatplayer == null) { return; }
            var message = string.Format(lang.GetMessage(key, this, chatplayer.UserIDString), args);
            chatplayer.ChatMessage(message);
        }
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            permission.RegisterPermission(permClean, this);
            permission.RegisterPermission(permCount, this);
            permission.RegisterPermission(permView, this);
        }

        private void Unload()
        {
            if (_routine != null)
            {
                try
                {
                    ServerMgr.Instance.StopCoroutine(_routine);
                }
                catch { }
                _routine = null; 
            }
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity != null && entity.name.Contains("player_corpse"))
            {
                PlayerCorpse corpse = entity as PlayerCorpse;
                if (corpse != null)
                {
                    BasePlayer moddedpayer = BasePlayer.Find(corpse.playerSteamID.ToString());
                    if (moddedpayer != null)
                    {
                        corpse._playerName = "[" + moddedpayer.lastDamage.ToString() + "]" + corpse._playerName.ToString();
                    }
                }
            }
        }

        private object OnEntityKill(BaseNetworkable entity)
        {
            if (entity != null && entity.name.Contains("player_corpse"))
            {
                PlayerCorpse corpse = entity as PlayerCorpse;
                if (corpse.health == 0) { return null; }
                return false;
            }
            return null;
        }

        #endregion

        #region Core
        bool isAdmin { get; set; }
        private void DrawMarker(Color color, Vector3 position, string text){player.SendConsoleCommand("ddraw.text", RefreshTimer, color, position, $"<size=22>{text}</size>");}
        private void BuildCorpseDict()
        {
            foreach (var entity in BaseNetworkable.serverEntities.OfType<BaseNetworkable>())
            {
                if (!Corpses.ContainsKey(entity))
                {
                    PlayerCorpse x = entity as PlayerCorpse;
                    if (x != null)
                    {
                        Corpses.Add(entity, x.transform.position);
                        continue;
                    }
                }
            }
        }

          private void ShowCorpses()
        {
            try
            {
                foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses)
                {
                    PlayerCorpse corpse = ent.Key as PlayerCorpse;
                    if (corpse == null || corpse.transform == null) { continue; }
                    DrawMarker(Color.red, corpse.transform.position + new Vector3(0f, 0.20f, 0f), string.Format("{0}", corpse.playerName));
                }
            }
            catch{}
        }

        IEnumerator CorpseScanRoutine()
        {
            do
            {
                if (!player || !player.IsConnected){yield break;}
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                }
                ShowCorpses();
                if (!isAdmin && player.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
                yield return CoroutineEx.waitForSeconds(RefreshTimer);
            } while (player.IsValid() && player.IsConnected && !player.IsSleeping());
            message(player, "Stop");
            _routine = null;
        }

        [ChatCommand("cleancorpses")]
        private void CmdCorpseClean(BasePlayer chatplayer, string command, string[] args)
        {
            if (chatplayer.IPlayer.HasPermission(permClean))
            {
                Corpses.Clear();
                BuildCorpseDict();
                foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses){ent.Key.Kill();}
                message(chatplayer, "Clean", Corpses.Count.ToString());
            }
            else { message(chatplayer, "Permission"); }
        }
        [ChatCommand("viewcorpses")]
        private void CmdCorpseView(BasePlayer chatplayer, string command, string[] args)
        {
            if (chatplayer.IPlayer.HasPermission(permView))
            {
                isAdmin = chatplayer.IsAdmin;
                player = chatplayer;
                Corpses.Clear();
                BuildCorpseDict();
                if (_routine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_routine);
                    _routine = null;
                    message(chatplayer, "Stop");
                    return;
                }
                _routine = ServerMgr.Instance.StartCoroutine(CorpseScanRoutine());
                message(chatplayer, "Start");
            }
            else { message(chatplayer, "Permission"); }
        }
        [ChatCommand("countcorpses")]
        private void CmdCorpseCount(BasePlayer chatplayer, string command, string[] args)
        {
            if (chatplayer.IPlayer.HasPermission(permCount))
            {
                Corpses.Clear();
                BuildCorpseDict();
                message(chatplayer, "Info", Corpses.Count.ToString());
            }
            else { message(chatplayer, "Permission"); }
        }
        #endregion
    }
}