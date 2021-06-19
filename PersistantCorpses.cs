using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Persistant Corpses", "bmgjet", "1.0.1")]
    [Description("Player Corpses don't despawn unless there health is 0.")]
    public class PersistantCorpses : RustPlugin
    {
        #region Vars
        //User Editable
        private float RefreshTimer = 6f;  
        private int textsize = 22;
        private Color textcolor = Color.red;
        //Dont change below
        private const string permClean = "PersistantCorpses.clean";
        private const string permCount = "PersistantCorpses.count";
        private const string permView = "PersistantCorpses.view";
        private BasePlayer player;
        private Coroutine _routine;
        private Dictionary<BaseNetworkable, Vector3> Corpses = new Dictionary<BaseNetworkable, Vector3> { };
        #endregion

        #region Language
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
            {"Info", "There are {0} corpses on map!"},
            {"Clean", "{0} corpses have been removed from the map!"},
            {"Stop", "Stopped corpse view! "},
            {"Start", "Started corpse view {0} corpses!"},
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
        private void BuildCorpseDict(string Filter = "")
        {
            foreach (var entity in BaseNetworkable.serverEntities.OfType<BaseNetworkable>())
            {
                if (!Corpses.ContainsKey(entity))
                {
                    PlayerCorpse x = entity as PlayerCorpse;
                    if (x != null)
                    {
                        if (Filter == "")
                        {
                            Corpses.Add(entity, x.transform.position);
                        }
                        else
                        {
                            string name = x._playerName;
                            if (name != null)
                            {
                                if (name.ToLower().Contains(Filter.ToLower()))
                                {
                                    Corpses.Add(entity, x.transform.position);
                                }
                            }
                        }
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
                    player.SendConsoleCommand("ddraw.text", RefreshTimer, textcolor, corpse.transform.position, "<size=" + textsize + ">"+corpse.playerName+"</size>");
                }
            }
            catch { }
        }

        IEnumerator CorpseScanRoutine()
        {
            do
            {
                if (!player || !player.IsConnected) { yield break; }
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

        [ChatCommand("cleandead")]
        private void CmdCorpseClean(BasePlayer chatplayer, string command, string[] args)
        {
            if (chatplayer.IPlayer.HasPermission(permClean))
            {
                Corpses.Clear();
                if (args.Length == 1) { BuildCorpseDict(args[0].ToString()); }
                else { BuildCorpseDict(); }
                foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses) 
                {
                    PlayerCorpse OldCorpse = ent.Key as PlayerCorpse;
                    if (OldCorpse != null)
                    {
                        OldCorpse.health = 0f;
                        OldCorpse.Kill();
                    }
                }
                message(chatplayer, "Clean", Corpses.Count.ToString());
            }
            else { message(chatplayer, "Permission"); }
        }
        [ChatCommand("viewdead")]
        private void CmdCorpseView(BasePlayer chatplayer, string command, string[] args)
        {
            if (chatplayer.IPlayer.HasPermission(permView))
            {
                if (_routine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_routine);
                    _routine = null;
                    if (args.Length == 0)
                    {
                        message(chatplayer, "Stop");
                        return;
                    }
                }
                isAdmin = chatplayer.IsAdmin;
                player = chatplayer;
                Corpses.Clear();
                if (args.Length == 1) { BuildCorpseDict(args[0].ToString()); }
                else { BuildCorpseDict(); }
                    _routine = ServerMgr.Instance.StartCoroutine(CorpseScanRoutine());
                    message(chatplayer, "Start", Corpses.Count.ToString());
            }
            else { message(chatplayer, "Permission"); }
        }
        [ChatCommand("countdead")]
        private void CmdCorpseCount(BasePlayer chatplayer, string command, string[] args)
        {
            if (chatplayer.IPlayer.HasPermission(permCount))
            {
                Corpses.Clear();
                if (args.Length == 1) { BuildCorpseDict(args[0].ToString()); }
                else { BuildCorpseDict(); }
                message(chatplayer, "Info", Corpses.Count.ToString());
            }
            else { message(chatplayer, "Permission"); }
        }
        #endregion
    }
}