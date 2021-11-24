#region License (GPL v3)
/*
    Copyright (c) 2021 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v3)
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Forestry Zones", "RFC1920", "1.0.8")]
    [Description("Protect the forest and ore deposits in specific areas, specifically around TCs.")]
    internal class ForestryZones : RustPlugin
    {
        [PluginReference]
        private readonly Plugin ZoneManager, Friends, Clans;

        private ConfigData configData;
        private const string permFZones = "forestryzones.use";
        private Dictionary<ulong, string> tcToZone = new Dictionary<ulong, string>();
        private List<string> zoneIDs = new List<string>();
        private Dictionary<ulong, List<string>> notified = new Dictionary<ulong, List<string>>();
        private bool isEnabled;

        private void Init()
        {
            permission.RegisterPermission(permFZones, this);

            LoadConfigVariables();
            LoadData();
        }

        private void OnServerInitialized()
        {
            isEnabled = true;
        }

        private void Loaded()
        {
            foreach (KeyValuePair<ulong, string> zonemap in tcToZone)
            {
                ZoneManager?.Call("EraseZone", zonemap.Value);
                DoLog($"Erased ForestryZone {zonemap.Value}");
            }
            tcToZone = new Dictionary<ulong, string>();
            zoneIDs = new List<string>();
            SaveData();

            foreach (BuildingPrivlidge tc in Resources.FindObjectsOfTypeAll<BuildingPrivlidge>())
            {
                DoLog($"Checking TC at {tc.transform.position.ToString()}");
                if (!(tc as BaseEntity).IsValid())
                {
                    DoLog("Invalid TC.  Skipping...");
                    continue;
                }

                BasePlayer pl = BasePlayer.FindByID(tc.OwnerID);
                if (pl == null)
                {
                    pl = BasePlayer.FindSleeping(tc.OwnerID);
                    if (pl == null)
                    {
                        DoLog("No valid owner, or owner is server.  Skipping...");
                        continue;
                    }
                }
                if (!permission.UserHasPermission(pl.UserIDString, permFZones) && configData.requirePermission)
                {
                    DoLog($"Permission required.  Skipping {tc.net.ID.ToString()}");
                    continue;
                }
                CreateZone(tc);
            }
        }

        private void Unload()
        {
            foreach (KeyValuePair<ulong, string> zonemap in tcToZone)
            {
                ZoneManager?.Call("EraseZone", zonemap.Value);
                DoLog($"Erased ForestryZone {zonemap.Value}");
            }
        }

        private void OnEntitySpawned(BuildingPrivlidge tc)
        {
            if (!isEnabled) return;
            BasePlayer pl = BasePlayer.FindByID(tc.OwnerID);
            if (!permission.UserHasPermission(pl?.UserIDString, permFZones) && configData.requirePermission)
            {
                return;
            }
            CreateZone(tc);
        }

        private void LoadData()
        {
            tcToZone = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, string>>(Name + "/tcToZone");
            foreach (KeyValuePair<ulong, string> zonemap in tcToZone)
            {
                zoneIDs.Add(zonemap.Value);
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/tcToZone", tcToZone);
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

        private object OnEntityTakeDamage(TreeEntity tree, HitInfo hitinfo)
        {
            BasePlayer player = hitinfo.Initiator as BasePlayer;
            if (player == null) return null;

            BuildingPrivlidge tc = GetLocalTC(tree);

            string[] zones = GetEntityZones(tree);
            if (zones.Length == 0) return null;
            foreach (string zone in zones)
            {
                DoLog($"OnEntityTakeDamage: Tree in zone {zone}");
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

        private void CreateZone(BuildingPrivlidge tc)
        {
            if (tc == null) return;

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
                    DoLog($"Found TC in existing zone {zone} at {tc.transform.position.ToString()}.  Adding to tables and skipping re-creation.");
                    if (!tcToZone.ContainsKey(tc.net.ID))
                    {
                        tcToZone.Add(tc.net.ID, zone);
                        zoneIDs.Add(zone);
                    }
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

                DoLog($"Creating zone {zoneID} for TC 'ForestryZones' with radius {radius.ToString()} at {tc.transform.position.ToString()}");
                ZoneManager.Call("CreateOrUpdateZone", zoneID, zoneArgs, tc.transform.position);

                tcToZone.Add(tc.net.ID, zoneID);
                zoneIDs.Add(zoneID);
            }
            SaveData();
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
                    DoLog($"Friends plugin reports that {playerid.ToString()} and {ownerid.ToString()} are friends.");
                    return true;
                }
            }
            if (configData.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan != null && ownerclan != null && playerclan == ownerclan)
                {
                    DoLog($"Clans plugin reports that {playerid.ToString()} and {ownerid.ToString()} are clanmates.");
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
                        DoLog($"Rust teams reports that {playerid.ToString()} and {ownerid.ToString()} are on the same team.");
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
