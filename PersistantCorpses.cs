using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Persistant Corpses", "bmgjet", "1.0.2")]
    [Description("Player Corpses don't despawn unless there health is 0.")]
    public class PersistantCorpses : RustPlugin
    {
        #region Vars
        //User Editable
        private float RefreshTimer = 6f;
        private bool NPCSupport = false;
        private int textsize = 22;
        private Color textcolor = Color.red;
        private bool stripitems = false;
        private int scorelimit = 10;
        //Dont change below
        private const string permClean = "PersistantCorpses.clean";
        private const string permCount = "PersistantCorpses.count";
        private const string permScore = "PersistantCorpses.score";
        private const string permView = "PersistantCorpses.view";
        private Dictionary<BasePlayer, Dictionary<bool, string>> Viewers = new Dictionary<BasePlayer, Dictionary<bool, string>>();
        private Coroutine _routine;
        private Dictionary<string, int> Score = new Dictionary<string, int> { };
        private Dictionary<BaseNetworkable, Vector3> Corpses = new Dictionary<BaseNetworkable, Vector3> { };
        #endregion

        #region Language
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
            {"Info", "There are {0} corpses on map!"},
            {"Score", "Players Deaths {0}"},
            {"Clean", "{0} corpses have been removed from the map!"},
            {"View", "{0} Corpse view!"},
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
            permission.RegisterPermission(permScore, this);
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
            if (entity != null && (entity.name.Contains("player_corpse") || entity.name.Contains("scientist_corpse")))
            {
                PlayerCorpse corpse = entity as PlayerCorpse;
                if (corpse != null)
                {
                    BasePlayer moddedpayer = BasePlayer.Find(corpse.playerSteamID.ToString());
                    if (moddedpayer != null)
                    {
                        corpse._playerName = "[" + moddedpayer.lastDamage.ToString() + "]" + corpse._playerName.ToString();
                        if (stripitems)
                        {
                            corpse.DropItems();
                        }
                    }
                    else
                    {
                        //bot
                        if (stripitems && NPCSupport)
                        {
                            corpse.DropItems();
                        }
                    }
                }
            }
        }

        private object OnEntityKill(BaseNetworkable entity)
        {
            if (entity != null && (entity.name.Contains("player_corpse") || entity.name.Contains("scientist_corpse")))
            {
                if (entity.name.Contains("scientist_corpse") && !NPCSupport) { return null; }
                PlayerCorpse corpse = entity as PlayerCorpse;
                if (corpse.health == 0) { return null; }
                return false;
            }
            return null;
        }

        #endregion

        #region Core
        private void CheckIfViewed()
        {
            if (Viewers.Count == 0) //If no viewers left remove routine
            {
                ServerMgr.Instance.StopCoroutine(_routine);
                _routine = null;
                Puts("Corpse View Thread Stopped!");
            }
        }

        private void CleanOut(Dictionary<BaseNetworkable, Vector3> co)
        {
            foreach (KeyValuePair<BaseNetworkable, Vector3> ent in co.ToList())
            {
                PlayerCorpse OldCorpse = ent.Key as PlayerCorpse;
                if (OldCorpse != null)
                {
                    OldCorpse.health = 0f;
                    OldCorpse.Kill();
                }
            }
        }

        private void BuildCorpseDict()
        {
            Corpses.Clear();
            foreach (var entity in BaseNetworkable.serverEntities.OfType<BaseNetworkable>())
            {
                if (!Corpses.ContainsKey(entity))
                {
                    PlayerCorpse x = entity as PlayerCorpse;
                    if (x != null)
                    {
                        Corpses.Add(entity, x.transform.position);
                    }
                }
            }
        }

        private void ShowCorpses(BasePlayer viewplayer, string filter)
        {
            foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses)
            {
                try
                {
                    PlayerCorpse corpse = ent.Key as PlayerCorpse;
                    if (corpse == null || corpse.transform == null) { continue; }
                    if (corpse.playerName.ToLower().Contains(filter.ToLower()) || filter == "")
                    {
                        viewplayer.SendConsoleCommand("ddraw.text", RefreshTimer, textcolor, corpse.transform.position, "<size=" + textsize + ">" + corpse.playerName + "</size>");
                    }
                }
                catch { }
            }
        }

        IEnumerator CorpseScanRoutine()
        {
            do //start loop
            {
                foreach (KeyValuePair<BasePlayer, Dictionary<bool, string>> viewer in Viewers.ToList())
                {
                    foreach (KeyValuePair<bool, string> viewerinfo in viewer.Value.ToList())
                    {
                        //toggle admin flag so you can show a normal user with out it auto banning them for cheating
                        if (!viewerinfo.Key)
                        {
                            viewer.Key.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                            viewer.Key.SendNetworkUpdateImmediate();
                        }
                        ShowCorpses(viewer.Key, viewerinfo.Value);

                        if (!viewerinfo.Key && viewer.Key.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                        {
                            viewer.Key.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                            viewer.Key.SendNetworkUpdateImmediate();
                        }
                    }
                    if (!viewer.Key.IsConnected || viewer.Key.IsSleeping())
                    {
                        //Remove from viewers list
                        Viewers.Remove(viewer.Key);
                        message(viewer.Key, "View", "Stopped");
                    }
                }
                yield return CoroutineEx.waitForSeconds(RefreshTimer);
            } while (Viewers.Count != 0);
            _routine = null;
            Puts("Corpse View Thread Stopped!");
        }

        [ChatCommand("corpse.clean")]
        private void CmdCorpseClean(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(permCount))
            {
                message(player, "Permission", "count");
                return;
            }
            BuildCorpseDict();
            if (args.Length == 0)
            {
                CleanOut(Corpses);
                message(player, "Clean", Corpses.Count.ToString());
                return;
            }
            Dictionary<BaseNetworkable, Vector3> CleanOutList = new Dictionary<BaseNetworkable, Vector3>();
            foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses)
            {
                try
                {
                    PlayerCorpse OldCorpse = ent.Key as PlayerCorpse;
                    if (OldCorpse != null)
                    {
                        if (OldCorpse.playerName.ToLower().Contains(args[0].ToLower()))
                        {
                            CleanOutList.Add(ent.Key, ent.Value);
                        }
                    }
                }
                catch { }
            }
            CleanOut(CleanOutList);
            message(player, "Clean", CleanOutList.Count.ToString());
        }

        [ChatCommand("corpse.view")]
        private void CmdCorpseView(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(permView))
            {
                message(player, "Permission", "view");
                return;
            }

            if (_routine != null) //Check if already running
            {
                if (args.Length == 0) //No Args passed
                {
                    if (Viewers.ContainsKey(player))
                    {
                        Viewers.Remove(player); //Remove player from list
                        message(player, "View", "Stopped");
                        CheckIfViewed();
                        return;
                    }
                }
                CheckIfViewed();
            }
            if (args.Length > 0) //args passed
            {
                if (Viewers.ContainsKey(player)) //Updates filter on player
                {
                    Viewers[player] = new Dictionary<bool, string> { { player.IsAdmin, args[0] } };
                }
                else
                {
                    Viewers.Add(player, new Dictionary<bool, string> { { player.IsAdmin, args[0] } }); //Filter "" = all players
                }
                message(player, "View", "Started filtered");
            }
            else
            {
                Viewers.Add(player, new Dictionary<bool, string> { { player.IsAdmin, "" } }); //Filter "" = all players
                message(player, "View", "Started");
            }
            BuildCorpseDict();
            if (_routine == null) //Start routine
            {
                Puts("Corpse View Thread Started");
                _routine = ServerMgr.Instance.StartCoroutine(CorpseScanRoutine());
            }
        }

        [ChatCommand("corpse.count")]
        private void CmdCorpseCount(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(permCount))
            {
                message(player, "Permission", "count");
                return;
            }

            BuildCorpseDict();
            if (args.Length == 0)
            {
                message(player, "Info", Corpses.Count.ToString());
                return;
            }
            int i = 0;
            foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses)
            {
                try
                {
                    PlayerCorpse OldCorpse = ent.Key as PlayerCorpse;
                    if (OldCorpse != null)
                    {
                        if (OldCorpse.playerName.ToLower().Contains(args[0].ToLower()))
                        {
                            i++;
                        }
                    }
                }
                catch { }
            }
            message(player, "Info", i.ToString());
        }

        [ChatCommand("corpse.score")]
        private void CmdCorpseScore(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(permScore))
            {
                message(player, "Permission", "score");
                return;
            }

            BuildCorpseDict();
            Score.Clear();
            foreach (KeyValuePair<BaseNetworkable, Vector3> ent in Corpses)
            {

                PlayerCorpse OldCorpse = ent.Key as PlayerCorpse;
                if (OldCorpse != null)
                {
                    try
                    {
                        if (!Score.ContainsKey(OldCorpse.playerSteamID.ToString()))
                        {
                            if (OldCorpse.playerSteamID != 0)
                                Score.Add(OldCorpse.playerSteamID.ToString(), 1);
                            continue;
                        }
                        Score[OldCorpse.playerSteamID.ToString()]++;
                        continue;
                    }
                    catch { }
                }
            }
            string output = "\n";

            var orderByDescendingResult = from s in Score
                                          orderby s.Value descending
                                          select s;

            int sl = 0;
            foreach (KeyValuePair<string, int> o in orderByDescendingResult)
            {
                if (sl > scorelimit)
                {
                    break;
                }
                BasePlayer dead = BasePlayer.FindAwakeOrSleeping(o.Key.ToString());
                if (dead != null)
                {
                    output += dead.displayName + " Died " + o.Value.ToString() + " Times\n";
                    sl++;
                }
            }
            message(player, "Score", output);
        }
        #endregion
    }
}