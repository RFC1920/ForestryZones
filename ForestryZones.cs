#region License (GPL v2)
/*
    Copyright (c) 2022 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    modify it under the terms of the GNU General Public License v2.0.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Forestry Zones", "RFC1920", "1.1.1")]
    [Description("Protect the forest and ore deposits in specific areas, specifically around TCs.")]
    internal class ForestryZones : RustPlugin
    {
        [PluginReference]
        private readonly Plugin ZoneManager, Friends, Clans;

        private ConfigData configData;
        private const string permFZones = "forestryzones.use";
        private readonly Dictionary<ulong, string> tcToZone = new Dictionary<ulong, string>();
        private Dictionary<ulong, Dictionary<ulong, string>> playerZones = new Dictionary<ulong, Dictionary<ulong, string>>();
        private List<string> zoneIDs = new List<string>();
        private Dictionary<ulong, List<string>> notified = new Dictionary<ulong, List<string>>();
        private bool isEnabled;

        private bool do109upgrade;

        private void OnServerInitialized()
        {
            isEnabled = true;
        }

        private void Init()
        {
            permission.RegisterPermission(permFZones, this);
        }

        private void OnNewSave()
        {
            playerZones = new Dictionary<ulong, Dictionary<ulong, string>>();
            SaveData();
        }

        private void Loaded()
        {
            LoadConfigVariables();
            LoadData();

            foreach (KeyValuePair<ulong, Dictionary<ulong, string>> playertcs in new Dictionary<ulong, Dictionary<ulong, string>>(playerZones))
            {
                foreach (KeyValuePair<ulong, string> zonemap in playertcs.Value)
                {
                    BaseNetworkable tc = BaseNetworkable.serverEntities.Find(new NetworkableId((uint)zonemap.Key));
                    if (tc == null) continue;
                    BuildingPrivlidge bp = tc as BuildingPrivlidge;

                    DoLog($"Checking TC at {tc.transform.position}");
                    if (!(tc as BaseEntity).IsValid())
                    {
                        DoLog("Invalid TC.  Skipping...");
                        continue;
                    }
                    BasePlayer pl = BasePlayer.FindByID(bp.OwnerID);
                    if (pl == null)
                    {
                        pl = BasePlayer.FindSleeping(bp.OwnerID);
                        if (pl == null)
                        {
                            DoLog("No valid owner, or owner is server.  Skipping...");
                            continue;
                        }
                    }
                    if (!permission.UserHasPermission(pl.UserIDString, permFZones) && configData.requirePermission)
                    {
                        DoLog($"Permission required.  Skipping {tc.net.ID}");
                        continue;
                    }
                    if (configData.useZoneManager) CreateZone(bp);
                }
            }
        }

        private void Unload()
        {
            string[] zoneIDs = GetZoneIDs();
            if (zoneIDs == null || zoneIDs.Length == 0)
            {
                return;
            }

            foreach (string zoneID in zoneIDs)
            {
                string zoneName = GetZoneName(zoneID);
                if (zoneName == "ForestryZones")
                {
                    ZoneManager?.Call("EraseZone", zoneID);
                    DoLog($"Erased ForestryZone with ID {zoneID}");
                }
            }
        }

        private void OnEntitySpawned(BuildingPrivlidge tc)
        {
            if (!isEnabled) return;
            if (tc == null) return;
            if (!tc.IsValid()) return;

            if (!permission.UserHasPermission(tc.OwnerID.ToString(), permFZones) && configData.requirePermission)
            {
                return;
            }
            // Limits, etc. will be checked there:
            if (configData.useZoneManager) CreateZone(tc);
        }

        private void LoadData()
        {
            if (do109upgrade)
            {
                foreach (KeyValuePair<ulong, string> zonemap in Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, string>>(Name + "/tcToZone"))
                {
                    BuildingPrivlidge bp = BaseNetworkable.serverEntities.Find(new NetworkableId((uint)zonemap.Key)) as BuildingPrivlidge;
                    if (!playerZones.ContainsKey(bp.OwnerID))
                    {
                        playerZones.Add(bp.OwnerID, new Dictionary<ulong, string>());
                    }
                    playerZones[bp.OwnerID].Add(zonemap.Key, zonemap.Value);

                    zoneIDs.Add(zonemap.Value);
                }
                return;
            }

            playerZones = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, Dictionary<ulong, string>>>(Name + "/playerTCs");
            foreach (KeyValuePair<ulong, Dictionary<ulong, string>> playertcs in playerZones)
            {
                foreach (KeyValuePair<ulong, string> zonemap in playertcs.Value)
                {
                    zoneIDs.Add(zonemap.Value);
                }
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/playerTCs", playerZones);
        }

        private BuildingPrivlidge GetLocalTC(BaseEntity tree)
        {
            if (configData.allowOwner || configData.useFriends || configData.useClans || configData.useTeams)
            {
                // Find any matching TC in the protectionRadius
                List<BuildingPrivlidge> tcs = new List<BuildingPrivlidge>();
                Vis.Entities(tree.transform.position, configData.protectionRadius, tcs);
                foreach (BuildingPrivlidge tc in tcs)
                {
                    return tc;
                }
            }

            return null;
        }

        private void OnEntityKill(BaseNetworkable bn)
        {
            if (bn == null) return;
            if (bn is BuildingPrivlidge)
            {
                BuildingPrivlidge bp = bn as BuildingPrivlidge;
                if (playerZones.ContainsKey(bp.OwnerID) && playerZones[bp.OwnerID].ContainsKey(bp.net.ID.Value))
                {
                    DoLog($"Removing TC {bp.net.ID} from playerZones for {bp.OwnerID}");
                    string zoneID = playerZones[bp.OwnerID]?[bp.net.ID.Value];
                    if (zoneID.Length > 0)
                    {
                        if (configData.useZoneManager) ZoneManager?.Call("EraseZone", zoneID);
                    }
                    playerZones[bp.OwnerID].Remove(bp.net.ID.Value);
                    SaveData();
                }
            }
        }

        private object OnEntityTakeDamage(TreeEntity tree, HitInfo hitinfo)
        {
            BasePlayer player = hitinfo.Initiator as BasePlayer;
            if (player == null) return null;

            BuildingPrivlidge tc = GetLocalTC(tree);

            string[] zones = GetEntityZones(tree);
            if (zones.Length == 0) return null;
            foreach (string zone in zones)
            {
                DoLog($"OnEntityTakeDamage: Tree in zone {zone} from {hitinfo.GetType()}");
                if (zoneIDs.Contains(zone))
                {
                    if (tc != null && CheckPerms(tc.OwnerID, player.userID))
                    {
                        return null;
                    }
                    DoLog($"OnEntityTakeDamage: Found protected tree in {zone}");
                    if (!notified.ContainsKey(player.userID))
                    {
                        notified.Add(player.userID, new List<string>());
                    }
                    if (!notified[player.userID].Contains(zone))
                    {
                        SendReply(player, configData.message);
                        notified[player.userID].Add(zone);
                    }
                    return true;
                }
            }

            return null;
        }

        private object OnEntityTakeDamage(OreResourceEntity ore, HitInfo hitinfo)
        {
            BasePlayer player = hitinfo.Initiator as BasePlayer;
            if (player == null) return null;

            BuildingPrivlidge tc = GetLocalTC(ore);

            string[] zones = GetEntityZones(ore);
            if (zones.Length == 0) return null;
            foreach (string zone in zones)
            {
                DoLog($"OnEntityTakeDamage: Ore in zone {zone}");
                if (zoneIDs.Contains(zone))
                {
                    if (tc != null && CheckPerms(tc.OwnerID, player.userID))
                    {
                        return null;
                    }
                    DoLog($"OnEntityTakeDamage: Found protected ore in {zone}");
                    if (!notified.ContainsKey(player.userID))
                    {
                        notified.Add(player.userID, new List<string>());
                    }
                    if (!notified[player.userID].Contains(zone))
                    {
                        SendReply(player, configData.message);
                        notified[player.userID].Add(zone);
                    }
                    return true;
                }
            }

            return null;
        }

        private object OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            TreeEntity tree = dispenser.GetComponentInParent<TreeEntity>();
            OreResourceEntity ore = dispenser.GetComponentInParent<OreResourceEntity>();
            BasePlayer player = entity as BasePlayer;

            if (tree != null)
            {
                BuildingPrivlidge tc = GetLocalTC(tree);
                if (!configData.useZoneManager)
                {
                    if (tc != null && !CheckPerms(tc.OwnerID, player.userID))
                    {
                        DoLog($"OnDispenserGather: Found protected tree {Vector3.Distance(tc.transform.position, tree.transform.position)} from TC, which is within {configData.protectionRadius}");
                        return true;
                    }
                    return null;
                }
                string[] zones = GetEntityZones(tree);
                if (zones.Length == 0) return null;
                foreach (string zone in zones)
                {
                    DoLog($"OnDispenserGather: Tree in zone {zone}");
                    if (zoneIDs.Contains(zone))
                    {
                        if (tc != null && CheckPerms(tc.OwnerID, player.userID))
                        {
                            return null;
                        }
                        DoLog($"OnDispenserGather: Found protected tree in {zone}");
                        return true;
                    }
                }
            }
            else if (ore != null && configData.protectOreDeposits)
            {
                BuildingPrivlidge tc = GetLocalTC(ore);
                if (!configData.useZoneManager)
                {
                    if (tc != null && !CheckPerms(tc.OwnerID, player.userID))
                    {
                        DoLog($"OnDispenserGather: Found protected ore {Vector3.Distance(tc.transform.position, ore.transform.position)} from TC, which is within {configData.protectionRadius}");
                        return true;
                    }
                    return null;
                }
                string[] zones = GetEntityZones(ore);
                if (zones.Length == 0) return null;
                foreach (string zone in zones)
                {
                    DoLog($"OnDispenserGather: ore in zone {zone}");
                    if (zoneIDs.Contains(zone))
                    {
                        if (tc != null && CheckPerms(tc.OwnerID, player.userID))
                        {
                            return null;
                        }
                        DoLog($"OnDispenserGather: Found protected ore in {zone}");
                        return true;
                    }
                }
            }
            return null;
        }

        private object OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            TreeEntity tree = dispenser.GetComponentInParent<TreeEntity>();
            OreResourceEntity ore = dispenser.GetComponentInParent<OreResourceEntity>();

            if (tree != null)
            {
                BuildingPrivlidge tc = GetLocalTC(tree);
                string[] zones = GetEntityZones(tree);
                if (zones.Length == 0) return null;
                foreach (string zone in zones)
                {
                    DoLog($"OnDispenserBonus: Tree in zone {zone}");
                    if (zoneIDs.Contains(zone))
                    {
                        if (tc != null && CheckPerms(tc.OwnerID, player.userID))
                        {
                            return null;
                        }
                        DoLog($"OnDispenserBonus: Found protected tree in {zone}");
                        return true;
                    }
                }
            }
            else if (ore != null && configData.protectOreDeposits)
            {
                BuildingPrivlidge tc = GetLocalTC(ore);
                string[] zones = GetEntityZones(ore);
                if (zones.Length == 0) return null;
                foreach (string zone in zones)
                {
                    DoLog($"OnDispenserBonus: Ore in zone {zone}");
                    if (zoneIDs.Contains(zone))
                    {
                        if (tc != null && CheckPerms(tc.OwnerID, player.userID))
                        {
                            return null;
                        }
                        DoLog($"OnDispenserBonus: Found protected ore in {zone}");
                        return true;
                    }
                }
            }
            return null;
        }

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Require permission to prevent harvesting of trees")]
            public bool requirePermission;

            [JsonProperty(PropertyName = "Allow building owner to harvest trees")]
            public bool allowOwner;

            [JsonProperty(PropertyName = "Use ZoneManager plugin to locate TCs instead of proximity")]
            public bool useZoneManager;

            [JsonProperty(PropertyName = "Use Friends plugin to allow harvesting by friends")]
            public bool useFriends;

            [JsonProperty(PropertyName = "Use Clans plugin to allow harvesting by clan members")]
            public bool useClans;

            [JsonProperty(PropertyName = "Use Rust Teams to allow harvesting by team members")]
            public bool useTeams;

            [JsonProperty(PropertyName = "Message to send to offending player")]
            public string message;

            [JsonProperty(PropertyName = "Radius of zone around building")]
            public float protectionRadius;

            [JsonProperty(PropertyName = "Allow overlap with existing zone")]
            public bool allowZoneOverlap;

            [JsonProperty(PropertyName = "Also protect ore deposits")]
            public bool protectOreDeposits;

            [JsonProperty(PropertyName = "Limit player to this many zones")]
            public int playerLimit;

            [JsonProperty(PropertyName = "If limit is reached, do not update the list with each new TC")]
            public bool noUpdate;

            [JsonProperty(PropertyName = "If limit is reached, update with the most recently placed TC")]
            public bool updateLast;

            public bool debug;
            public VersionNumber Version;
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                message = "This area is protected by the local Forestry Service.",
                protectionRadius = 120f,
                Version = Version
            };
            SaveConfig(config);
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < new VersionNumber(1, 0, 2))
            {
                configData.message = "This area is protected by the local Forestry Service.";
            }

            if (configData.Version < new VersionNumber(1, 0, 9))
            {
                do109upgrade = true;
                configData.useZoneManager = false;
            }

            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private bool CheckPerms(ulong ownerid, ulong playerid)
        {
            if (ownerid == playerid || IsFriend(playerid, ownerid))
            {
                if (configData.allowOwner && !configData.requirePermission)
                {
                    return true;
                }
                else if (configData.allowOwner && permission.UserHasPermission(ownerid.ToString(), permFZones))
                {
                    return true;
                }
            }
            return false;
        }

        private string[] GetZoneIDs() => (string[])ZoneManager?.Call("GetZoneIDs");

        private string GetZoneName(string zoneID) => (string)ZoneManager?.Call("GetZoneName", zoneID);

        private void CreateZone(BuildingPrivlidge tc)
        {
            if (tc == null) return;

            if (playerZones.ContainsKey(tc.OwnerID))
            {
                int playerCount = playerZones[tc.OwnerID].Count;
                if (playerCount >= configData.playerLimit && configData.noUpdate)
                {
                    DoLog($"Player is at or over the limit of {configData.playerLimit} TCs, and noUpdate is set.");
                    return;
                }
            }

            bool fz_exists = false;
            string inzone = "";

            foreach (string zone in GetEntityZones(tc))
            {
                // Check existing zones to prevent creating new zones on each load.
                // Also check for overlap with zones from other plugins.
                string nom = (string)ZoneManager?.Call("GetZoneName", zone);
                if (nom == "ForestryZones")
                {
                    fz_exists = true;
                    DoLog($"Found TC in existing zone {zone} at {tc.transform.position}.  Adding to tables and skipping re-creation.");

                    AddOrUpdatePlayerZones(tc.OwnerID, tc.net.ID.Value, zone);
                    zoneIDs.Add(zone);
                    break;
                }

                inzone = nom;
                break;
            }

            if (!fz_exists)
            {
                if (!string.IsNullOrEmpty(inzone) && !configData.allowZoneOverlap)
                {
                    DoLog($"TC in overlapping zone, {inzone}.  Skipping ForestryZone creation.");
                    return;
                }

                string zoneID = UnityEngine.Random.Range(1, 99999999).ToString();
                int radius = (int)configData.protectionRadius;
                string[] zoneArgs = { "name", "ForestryZones", "radius", radius.ToString() };

                DoLog($"Creating zone {zoneID} for TC 'ForestryZones' with radius {radius} at {tc.transform.position}");
                ZoneManager.Call("CreateOrUpdateZone", zoneID, zoneArgs, tc.transform.position);

                AddOrUpdatePlayerZones(tc.OwnerID, tc.net.ID.Value, zoneID);
                zoneIDs.Add(zoneID);
            }
            SaveData();
        }

        private void AddOrUpdatePlayerZones(ulong userid, ulong tcid, string zone)
        {
            DoLog("AddOrUpdatePlayerZones called");
            if (!playerZones.ContainsKey(userid))
            {
                DoLog($"Initializing table for userid {userid}");
                playerZones.Add(userid, new Dictionary<ulong, string>());
            }

            Dictionary<ulong, string> playerTCs = new Dictionary<ulong, string>();
            int playerCount = playerZones[userid].Count;

            DoLog($"Player {userid} has {playerCount} TC(s).");
            if (playerCount > configData.playerLimit && configData.updateLast)
            {
                DoLog($"They are above the configured limit of {configData.playerLimit} and updateLast is TRUE.");
                int i = 0;
                foreach (KeyValuePair<ulong, string> oldlist in playerZones[userid])
                {
                    if (i == playerCount - 1) continue;
                    i++;
                    DoLog($"Adding TC {i}: {oldlist.Key}");
                    playerTCs.Add(oldlist.Key, oldlist.Value);
                }
                if (!playerTCs.ContainsKey(tcid))
                {
                    i++;
                    DoLog($"Adding TC {i}: {tcid}");
                    playerTCs.Add(tcid, zone);
                }
                playerZones[userid] = playerTCs;
                return;
            }
            else if (playerCount > configData.playerLimit)
            {
                DoLog($"They are above the configured limit of {configData.playerLimit} and updateLast is FALSE.");
                int i = 0;
                if (!playerTCs.ContainsKey(tcid))
                {
                    DoLog($"Adding TC {i}: {tcid}");
                    playerTCs.Add(tcid, zone);
                }
                foreach (KeyValuePair<ulong, string> oldlist in playerZones[userid])
                {
                    if (i == 0)
                    {
                        DoLog($"Skipping TC {oldlist.Key}");
                        i++;
                        continue;
                    }
                    i++;
                    if (!playerTCs.ContainsKey(oldlist.Key))
                    {
                        DoLog($"Adding TC {i}: {oldlist.Key}");
                        playerTCs.Add(oldlist.Key, oldlist.Value);
                    }
                }
                playerZones[userid] = playerTCs;
                return;
            }

            if (!playerZones[userid].ContainsKey(tcid))
            {
                DoLog($"Adding zone {zone} to tc {tcid} for userid {userid}");
                playerZones[userid].Add(tcid, zone);
                return;
            }
            playerTCs[tcid] = zone;
            playerZones[userid] = playerTCs;
        }

        private string[] GetEntityZones(BaseEntity entity)
        {
            if (ZoneManager && entity.IsValid())
            {
                DoLog($"Checking zone for {entity.GetType()}");
                return (string[])ZoneManager?.Call("GetEntityZoneIDs", new object[] { entity });
            }
            return new string[0];
        }

        private bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (configData.useFriends && Friends != null)
            {
                object fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && (bool)fr)
                {
                    DoLog($"Friends plugin reports that {playerid} and {ownerid} are friends.");
                    return true;
                }
            }
            if (configData.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan != null && ownerclan != null && playerclan == ownerclan)
                {
                    DoLog($"Clans plugin reports that {playerid} and {ownerid} are clanmates.");
                    return true;
                }
            }
            if (configData.useTeams)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if (player != null && player.currentTeam != 0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                    if (playerTeam?.members.Contains(ownerid) == true)
                    {
                        DoLog($"Rust teams reports that {playerid} and {ownerid} are on the same team.");
                        return true;
                    }
                }
            }
            return false;
        }

        private void DoLog(string message)
        {
            if (configData.debug) Puts($"{message}");
        }
    }
}
