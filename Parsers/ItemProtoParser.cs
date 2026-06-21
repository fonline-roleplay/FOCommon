using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.ComponentModel;
using System.Globalization;
using FOCommon.Parsers;
using FOCommon.Items;

namespace FOCommon.Parsers
{
    public enum ItemTypes
    {
        ITEM_NONE = 0,
        ITEM_ARMOR = 1,
        ITEM_DRUG = 2,
        ITEM_WEAPON = 3,
        ITEM_AMMO = 4,
        ITEM_MISC = 5,
        ITEM_MISC_EX = 6, // deprecated
        ITEM_KEY = 7,
        ITEM_CONTAINER = 8,
        ITEM_DOOR = 9,
        ITEM_GRID = 10,
        ITEM_GENERIC = 11,
        ITEM_WALL = 12,
        ITEM_CAR = 13,
        ITEM_MAGIC = 20,  // User types
    };

    // For parsing proto file
    public class ItemProtoParser
    {
        private string _fieldName;
        private string _fieldValue;
        private List<ItemProto> _loadedProtos;

        private readonly List<String> _saveLines = new List<string>();
        private List<ItemProto> _saveProtos;

        private bool _formatWithSpaces;

        private String AddSpaces(string str)
        {
            int Num = 25 - str.Length;
            if (Num < 0)
                Num = 0;
            for (int i = 0; i < Num; i++)
                str += " ";
            return str;
        }

        private void AddSaveLine(string Name, string Value)
        {
            if(_formatWithSpaces)
                _saveLines.Add(AddSpaces(Name)+ "= " + Value);
            else
                _saveLines.Add(Name + "=" + Value);
        }

        #region ParseLine
        private bool ParseLine(string ifText, string oldText, ref string value, bool compatibility, bool load, bool ignoreZero = false)
        {
            if (!load && !string.IsNullOrEmpty(value) && (!ignoreZero || value != "0"))
            {
                AddSaveLine(ifText, value);
                return true;
            }

            if (_fieldName == ifText) { value = _fieldValue; return true; }
            if (!compatibility) return false;
            if (_fieldName == ifText.Replace('.', '_')) { value = _fieldValue; return true; }
            if (_fieldName == oldText) { value = _fieldValue; return true; }
            return false;
        }
        private bool ParseLine<T>(string ifText, string oldText, ref T value, bool compatibility, bool load, bool ignoreZero = true)
            where T : IConvertible
        {
            string vStr = (value is bool b) ? (b ? "1" : "0") : value.ToString();

            bool retVal = ParseLine(ifText, oldText, ref vStr, compatibility, load, ignoreZero);

            if (typeof(T) == typeof(bool))
            {
                value = (T)(object)(vStr == "1");
            }
            else
            {
                value = (T)Convert.ChangeType(vStr, typeof(T));
            }

            return retVal;
        }
        #endregion

        private string ConvertLines(String Lines)
        {
            if (Lines == "") return "";
            if (Lines == null) return "";
            char[] str = Lines.ToCharArray();
            List<Char> Line = new List<char>();
            Lines = "";
            for (int k = 0, l = str.Length; k < l; k++) { Line.Add(str[k]); Line.Add('1'); }
            for (int k = 0; k < Line.Count; )
            {
                if (k + 2 == Line.Count) break;
                if (Line[k] == Line[k + 2]) { Line.RemoveRange(k + 2, 2); Line[k + 1]++; }
                else k += 2;
            }
            return new String(Line.ToArray());
        }

        // Convert from 2.14.3
        private void ConvertStuff(ref ItemProto Prot, bool IsCar, bool IsNoOpen)
        {
            if (IsCar)
            {
                Prot.Type = (int)ItemTypes.ITEM_CAR;
                Prot.ChildPid[0] = (ushort)Prot.StartValue[0];
                Prot.ChildPid[1] = (ushort)Prot.StartValue[1];
                Prot.ChildPid[2] = (ushort)Prot.StartValue[2];
                Prot.ChildPid[3] = (ushort)Prot.StartValue[3];
            }
            if (Prot.Type == (int)ItemTypes.ITEM_AMMO ||
                Prot.Type == (int)ItemTypes.ITEM_DRUG ||
                Prot.Type == (int)ItemTypes.ITEM_MISC) Prot.Stackable = true;
            if (Prot.Type == (int)ItemTypes.ITEM_WEAPON) if (!Prot.Stackable) Prot.Deteriorable = true;
            if (Prot.Type == (int)ItemTypes.ITEM_ARMOR) Prot.Deteriorable = true;
            if (Prot.Type == (int)ItemTypes.ITEM_MISC_EX) Prot.Type = (int)ItemTypes.ITEM_MISC;
            if (Prot.Type == (int)ItemTypes.ITEM_CONTAINER) Prot.GroundLevel = Prot.Container_MagicHandsGrnd;
            if (Prot.Weapon_IsUnarmed)
            {
                Prot.Stackable = false;
                Prot.Deteriorable = false;
            }
            if (IsNoOpen) Prot.Locker_Condition = (ushort)(Prot.Locker_Condition | 0x10); // LOCKER_NOOPEN
            for (uint i = 0; i < Prot.ChildLines.Length; i++)
                Prot.ChildLines[i] = ConvertLines(Prot.ChildLines[i]);
        }


        public bool Save(string FilePath, string Version, List<String> Lines, MSGParser FOObj, Dictionary<int, string> definesList)
        {
            List<int> ProcessedPids = new List<int>();
            bool DuplicateFound = false;

            _saveLines.Clear();
            _saveLines.Add("# " + Version);
            _saveLines.Add("");

            _saveProtos.Sort();

            int j = _saveProtos.Count;
            for (int i = 0; i < j; i++)
            {
                ItemProto Prot = _saveProtos[i];
                if(definesList != null && definesList.ContainsKey(Prot.ProtoId))
                    _saveLines.Add($"# {definesList[Prot.ProtoId]}");
                _saveLines.Add("[Proto]");

                SaveData(ref Prot, false);

                if (ProcessedPids.Contains(Prot.ProtoId))
                {
                    Utils.Log("An object with the ProtoId " + Prot.ProtoId + " was already saved. Skipping proto '" + Prot.Name + '\'');
                    //Message.Show("An object with the ProtoId " + Prot.ProtoId + " was already saved. Skipping proto '"+Prot.Name+'\'',
                    //    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error, true);
                    DuplicateFound = true;
                    continue;
                }
                if (Prot != null && Prot.CustomFields != null)
                {
                    foreach (KeyValuePair<String, ItemProtoCustomField> kvp in Prot.CustomFields)
                    {
                        if (kvp.Value.Value is bool)
                            _saveLines.Add(kvp.Key + "=" + ((bool)kvp.Value.Value==true?"1":"0") );
                        else
                            _saveLines.Add(kvp.Key + "=" + kvp.Value.Value.ToString());
                    }
                }
                if(!String.IsNullOrEmpty(Prot.Name) && FOObj != null)
                    FOObj.WriteMSGLine(Prot.ProtoId * 100, Prot.Name, 0);
                if (!String.IsNullOrEmpty(Prot.Description) && FOObj != null)
                    FOObj.WriteMSGLine(Prot.ProtoId * 100 + 1, Prot.Description, 0);
                _saveLines.Add("");

                ProcessedPids.Add(Prot.ProtoId);
            }

            if(FOObj != null)
                FOObj.WriteFile(); // Flush file.
            return DuplicateFound;
        }

        private bool ParseData(ref ItemProto Prot, bool CompatibilityMode)
        {
            #region ParseData
            if(ParseLine("ProtoId", "", ref Prot.ProtoId, CompatibilityMode, true)) return true;
            if(ParseLine("Type", "", ref Prot.Type, CompatibilityMode, true)) return true;
            if(ParseLine("ScriptModule", "", ref Prot.ScriptModule, CompatibilityMode, true)) return true;
            if(ParseLine("ScriptFunc", "", ref Prot.ScriptFunction, CompatibilityMode, true)) return true;
            if(ParseLine("PicMap", "PicMapName", ref Prot.PicMap, CompatibilityMode, true)) return true;
            if(ParseLine("PicInv", "PicInvName", ref Prot.PicInv, CompatibilityMode, true)) return true;
            if(ParseLine("Corner", "", ref Prot.Corner, CompatibilityMode, true)) return true;
            if(ParseLine("Flags", "", ref Prot.Flags, CompatibilityMode, true)) return true;
            if(ParseLine("DisableEgg", "", ref Prot.DisableEgg, CompatibilityMode, true)) return true;
            if(ParseLine("Stackable", "Weapon.NoWear", ref Prot.Stackable, CompatibilityMode, true)) return true;
            if(ParseLine("Deteriorable", "", ref Prot.Deteriorable, CompatibilityMode, true)) return true;
            if(ParseLine("GroundLevel", "", ref Prot.GroundLevel, CompatibilityMode, true)) return true;

            if(ParseLine("Dir", "", ref Prot.Dir, CompatibilityMode, true)) return true;
            if(ParseLine("Slot", "", ref Prot.Slot, CompatibilityMode, true)) return true;
            if(ParseLine("Weight", "", ref Prot.Weight, CompatibilityMode, true)) return true;
            if(ParseLine("Volume", "", ref Prot.Volume, CompatibilityMode, true)) return true;
            if(ParseLine("SoundId", "", ref Prot.SoundId, CompatibilityMode, true)) return true;
            if(ParseLine("Cost", "", ref Prot.Cost, CompatibilityMode, true)) return true;
            if(ParseLine("StartCount", "Ammo.StartCount", ref Prot.StartCount, CompatibilityMode, true)) return true;
            if(ParseLine("Material", "", ref Prot.Material, CompatibilityMode, true)) return true;
            if(ParseLine("LightFlags", "", ref Prot.LightFlags, CompatibilityMode, true)) return true;
            if(ParseLine("LightDistance", "", ref Prot.LightDistance, CompatibilityMode, true)) return true;
            if(ParseLine("LightIntensity", "", ref Prot.LightIntensity, CompatibilityMode, true)) return true;
            if(ParseLine("LightColor", "", ref Prot.LightColor, CompatibilityMode, true)) return true;
            if(ParseLine("AnimWaitBase", "", ref Prot.AnimWaitBase, CompatibilityMode, true)) return true;
            if(ParseLine("AnimWaitRndMin", "", ref Prot.AnimWaitRndMin, CompatibilityMode, true)) return true;
            if(ParseLine("AnimWaitRndMax", "", ref Prot.AnimWaitRndMax, CompatibilityMode, true)) return true;
            if(ParseLine("AnimStay_0", "", ref Prot.AnimStay_0, CompatibilityMode, true)) return true;
            if(ParseLine("AnimStay_1", "", ref Prot.AnimStay_1, CompatibilityMode, true)) return true;
            if(ParseLine("AnimShow_0", "", ref Prot.AnimShow_0, CompatibilityMode, true)) return true;
            if(ParseLine("AnimShow_1", "", ref Prot.AnimShow_1, CompatibilityMode, true)) return true;
            if(ParseLine("AnimHide_0", "", ref Prot.AnimHide_0, CompatibilityMode, true)) return true;
            if(ParseLine("AnimHide_1", "", ref Prot.AnimHide_1, CompatibilityMode, true)) return true;
            if(ParseLine("OffsetX", "", ref Prot.OffsetX, CompatibilityMode, true)) return true;
            if(ParseLine("OffsetY", "", ref Prot.OffsetY, CompatibilityMode, true)) return true;
            if(ParseLine("SpriteCut", "", ref Prot.SpriteCut, CompatibilityMode, true)) return true;
            if(ParseLine("DrawOrderOffsetHexY", "", ref Prot.DrawOrderOffsetHexY, CompatibilityMode, true)) return true;
            if(ParseLine("RadioChannel", "", ref Prot.RadioChannel, CompatibilityMode, true)) return true;
            if(ParseLine("RadioFlags", "", ref Prot.RadioFlags, CompatibilityMode, true)) return true;
            if(ParseLine("RadioBroadcastSend", "", ref Prot.RadioBroadcastSend, CompatibilityMode, true)) return true;
            if(ParseLine("RadioBroadcastRecv", "", ref Prot.RadioBroadcastRecv, CompatibilityMode, true)) return true;
            if(ParseLine("IndicatorStart", "", ref Prot.IndicatorStart, CompatibilityMode, true)) return true;
            if(ParseLine("IndicatorMax", "", ref Prot.IndicatorMax, CompatibilityMode, true)) return true;
            if(ParseLine("HolodiskNum", "", ref Prot.HolodiskNum, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_IsUnarmed", "Weapon.IsUnarmed", ref Prot.Weapon_IsUnarmed, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_UnarmedTree", "Weapon.UnarmedTree", ref Prot.Weapon_UnarmedTree, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_UnarmedPriority", "Weapon.UnarmedPriority", ref Prot.Weapon_UnarmedPriority, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_UnarmedMinAgility", "Weapon.UnarmedMinAgility", ref Prot.Weapon_UnarmedMinAgility, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_UnarmedMinUnarmed", "Weapon.UnarmedMinUnarmed", ref Prot.Weapon_UnarmedMinUnarmed, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_UnarmedMinLevel", "Weapon.UnarmedMinLevel", ref Prot.Weapon_UnarmedMinLevel, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Anim1", "Weapon.Anim1", ref Prot.Weapon_Anim1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_MaxAmmoCount", "Weapon.VolHolder", ref Prot.Weapon_MaxAmmoCount, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Caliber", "Weapon.Caliber", ref Prot.Weapon_Caliber, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DefaultAmmoPid", "Weapon.DefAmmo", ref Prot.Weapon_DefaultAmmoPid, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_MinStrength", "Weapon.MinSt", ref Prot.Weapon_MinStrength, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Perk", "Weapon.Perk", ref Prot.Weapon_Perk, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_ActiveUses", "Weapon.CountAttack", ref Prot.Weapon_ActiveUses, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_CriticalFailture", "Weapon.CrFailture", ref Prot.Weapon_CriticalFailture, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Skill_0", "Weapon.Skill_0", ref Prot.Weapon_Skill_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Skill_1", "Weapon.Skill_1", ref Prot.Weapon_Skill_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Skill_2", "Weapon.Skill_2", ref Prot.Weapon_Skill_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgType_0", "Weapon.DmgType_0", ref Prot.Weapon_DmgType_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgType_1", "Weapon.DmgType_1", ref Prot.Weapon_DmgType_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgType_2", "Weapon.DmgType_2", ref Prot.Weapon_DmgType_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Anim2_0", "Weapon.Anim2_0", ref Prot.Weapon_Anim2_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Anim2_1", "Weapon.Anim2_1", ref Prot.Weapon_Anim2_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Anim2_2", "Weapon.Anim2_2", ref Prot.Weapon_Anim2_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Remove_0", "Weapon.Remove_0", ref Prot.Weapon_Remove_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Remove_1", "Weapon.Remove_1", ref Prot.Weapon_Remove_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Remove_2", "Weapon.Remove_2", ref Prot.Weapon_Remove_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_PicUse_0", "Weapon.PicName_0", ref Prot.Weapon_PicUse_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_PicUse_1", "Weapon.PicName_1", ref Prot.Weapon_PicUse_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_PicUse_2", "Weapon.PicName_2", ref Prot.Weapon_PicUse_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgMin_0", "Weapon.DmgMin_0", ref Prot.Weapon_DmgMin_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgMin_1", "Weapon.DmgMin_1", ref Prot.Weapon_DmgMin_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgMin_2", "Weapon.DmgMin_2", ref Prot.Weapon_DmgMin_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgMax_0", "Weapon.DmgMax_0", ref Prot.Weapon_DmgMax_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgMax_1", "Weapon.DmgMax_1", ref Prot.Weapon_DmgMax_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_DmgMax_2", "Weapon.DmgMax_2", ref Prot.Weapon_DmgMax_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Effect_0", "Weapon.Effect_0", ref Prot.Weapon_FlyEffect_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Effect_1", "Weapon.Effect_1", ref Prot.Weapon_FlyEffect_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Effect_2", "Weapon.Effect_2", ref Prot.Weapon_FlyEffect_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_MaxDist_0", "Weapon.MaxDist_0", ref Prot.Weapon_MaxDist_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_MaxDist_1", "Weapon.MaxDist_1", ref Prot.Weapon_MaxDist_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_MaxDist_2", "Weapon.MaxDist_2", ref Prot.Weapon_MaxDist_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Round_0", "Weapon.Round_0", ref Prot.Weapon_Round_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Round_1", "Weapon.Round_1", ref Prot.Weapon_Round_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Round_2", "Weapon.Round_2", ref Prot.Weapon_Round_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_ApCost_0", "Weapon.Time_0", ref Prot.Weapon_ApCost_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_ApCost_1", "Weapon.Time_1", ref Prot.Weapon_ApCost_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_ApCost_2", "Weapon.Time_2", ref Prot.Weapon_ApCost_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Aim_0", "Weapon.Aim_0", ref Prot.Weapon_Aim_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Aim_1", "Weapon.Aim_1", ref Prot.Weapon_Aim_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_Aim_2", "Weapon.Aim_2", ref Prot.Weapon_Aim_2, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_SoundId_0", "Weapon.SoundId_0", ref Prot.Weapon_SoundId_0, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_SoundId_1", "Weapon.SoundId_1", ref Prot.Weapon_SoundId_1, CompatibilityMode, true)) return true;
            if(ParseLine("Weapon_SoundId_2", "Weapon.SoundId_2", ref Prot.Weapon_SoundId_2, CompatibilityMode, true)) return true;
            if(ParseLine("Ammo_Caliber", "Ammo.Caliber", ref Prot.Ammo_Caliber, CompatibilityMode, true)) return true;
            if(ParseLine("Ammo_AcMod", "Ammo.ACMod", ref Prot.Ammo_AcMod, CompatibilityMode, true)) return true;
            if(ParseLine("Ammo_DrMod", "Ammo.DRMod", ref Prot.Ammo_DrMod, CompatibilityMode, true)) return true;
            if(ParseLine("Door_NoBlockMove", "Door.NoBlockMove", ref Prot.Door_NoBlockMove, CompatibilityMode, true)) return true;
            if(ParseLine("Door_NoBlockShoot", "Door.NoBlockShoot", ref Prot.Door_NoBlockShoot, CompatibilityMode, true)) return true;
            if(ParseLine("Door_NoBlockLight", "Door.NoBlockLight", ref Prot.Door_NoBlockLight, CompatibilityMode, true)) return true;
            if(ParseLine("Container_Volume", "Container.ContVolume", ref Prot.Container_Volume, CompatibilityMode, true)) return true;
            if(ParseLine("Container_Changeble", "Container.Changeble", ref Prot.Container_Changeble, CompatibilityMode, true)) return true;
            if(ParseLine("Container_CannotPickUp", "Container.CannotPickUp", ref Prot.Container_CannotPickUp, CompatibilityMode, true)) return true;
            if(ParseLine("Container_MagicHandsGrnd", "Container.MagicHandsGrnd", ref Prot.Container_MagicHandsGrnd, CompatibilityMode, true)) return true;
            if(ParseLine("Locker_Condition", "Locker.Condition", ref Prot.Locker_Condition, CompatibilityMode, true)) return true;
            if(ParseLine("Grid_Type", "Grid.Type", ref Prot.Grid_Type, CompatibilityMode, true)) return true;
            if(ParseLine("Armor_CrTypeMale", "Armor.Anim0Male", ref Prot.Armor_CrTypeMale, CompatibilityMode, true)) return true;
            if(ParseLine("Armor_CrTypeFemale", "Armor.Anim0Female", ref Prot.Armor_CrTypeFemale, CompatibilityMode, true)) return true;
            if(ParseLine("ChildLines_0", "MiscEx.Car.Bag_0", ref Prot.ChildLines[0], CompatibilityMode, true)) return true;
            if(ParseLine("ChildLines_1", "MiscEx.Car.Bag_1", ref Prot.ChildLines[1], CompatibilityMode, true)) return true;
            if(ParseLine("ChildLines_2", "", ref Prot.ChildLines[2], CompatibilityMode, true)) return true;
            if(ParseLine("ChildLines_3", "", ref Prot.ChildLines[3], CompatibilityMode, true)) return true;
            if(ParseLine("ChildLines_4", "", ref Prot.ChildLines[4], CompatibilityMode, true)) return true;
            if(ParseLine("BlockLines", "MiscEx.Car.Blocks", ref Prot.BlockLines, CompatibilityMode, true)) return true;

            for (uint i = 0; i < Prot.ChildPid.Length; i++)
                if(ParseLine("ChildPid_" + i, "" + i, ref Prot.ChildPid[i], CompatibilityMode, true)) return true;

            for (uint k = 1; k <= Prot.StartValue.Length; k++)
                if(ParseLine("StartValue_" + k, "MiscEx.StartVal" + k, ref Prot.StartValue[k-1], CompatibilityMode, true)) return true;

            if(ParseLine("Car_Speed", "MiscEx.Car.Speed", ref Prot.Car_Speed, CompatibilityMode, true)) return true;
            if(ParseLine("Car_Passability", "MiscEx.Car.Negotiability", ref Prot.Car_Passability, CompatibilityMode, true)) return true;
            if(ParseLine("Car_DeteriorationRate", "MiscEx.Car.WearConsumption", ref Prot.Car_DeteriorationRate, CompatibilityMode, true)) return true;
            if(ParseLine("Car_CrittersCapacity", "MiscEx.Car.CritCapacity", ref Prot.Car_CrittersCapacity, CompatibilityMode, true)) return true;
            if(ParseLine("Car_TankVolume", "MiscEx.Car.FuelTank", ref Prot.Car_TankVolume, CompatibilityMode, true)) return true;
            if(ParseLine("Car_MaxDeterioration", "MiscEx.Car.RunToBreak", ref Prot.Car_MaxDeterioration, CompatibilityMode, true)) return true;
            if(ParseLine("Car_Entrance", "MiscEx.Car.Entire", ref Prot.Car_Entrance, CompatibilityMode, true)) return true;
            if(ParseLine("Car_FuelConsumption", "MiscEx.Car.FuelConsumption", ref Prot.Car_FuelConsumption, CompatibilityMode, true)) return true;
            if(ParseLine("Car_MovementType", "MiscEx.Car.WalkType", ref Prot.Car_MovementType, CompatibilityMode, true)) return true;

            #endregion
            return false;
        }

        private void SaveData(ref ItemProto Prot, bool CompatibilityMode)
        {
            #region SaveData
            ParseLine("ProtoId", "", ref Prot.ProtoId, CompatibilityMode, false);
            ParseLine("Type", "", ref Prot.Type, CompatibilityMode, false);
            ParseLine("ScriptModule", "", ref Prot.ScriptModule, CompatibilityMode, false);
            ParseLine("ScriptFunc", "", ref Prot.ScriptFunction, CompatibilityMode, false);
            ParseLine("PicMap", "PicMapName", ref Prot.PicMap, CompatibilityMode, false);
            ParseLine("PicInv", "PicInvName", ref Prot.PicInv, CompatibilityMode, false);
            ParseLine("Corner", "", ref Prot.Corner, CompatibilityMode, false);
            ParseLine("Flags", "", ref Prot.Flags, CompatibilityMode, false);
            ParseLine("DisableEgg", "", ref Prot.DisableEgg, CompatibilityMode, false);
            ParseLine("Stackable", "Weapon.NoWear", ref Prot.Stackable, CompatibilityMode, false);
            ParseLine("Deteriorable", "", ref Prot.Deteriorable, CompatibilityMode, false);
            ParseLine("GroundLevel", "", ref Prot.GroundLevel, CompatibilityMode, false);

            ParseLine("Dir", "", ref Prot.Dir, CompatibilityMode, false);
            ParseLine("Slot", "", ref Prot.Slot, CompatibilityMode, false);
            ParseLine("Weight", "", ref Prot.Weight, CompatibilityMode, false);
            ParseLine("Volume", "", ref Prot.Volume, CompatibilityMode, false);
            ParseLine("SoundId", "", ref Prot.SoundId, CompatibilityMode, false);
            ParseLine("Cost", "", ref Prot.Cost, CompatibilityMode, false);
            ParseLine("StartCount", "Ammo.StartCount", ref Prot.StartCount, CompatibilityMode, false);
            ParseLine("Material", "", ref Prot.Material, CompatibilityMode, false);
            ParseLine("LightFlags", "", ref Prot.LightFlags, CompatibilityMode, false);
            ParseLine("LightDistance", "", ref Prot.LightDistance, CompatibilityMode, false);
            ParseLine("LightIntensity", "", ref Prot.LightIntensity, CompatibilityMode, false);
            ParseLine("LightColor", "", ref Prot.LightColor, CompatibilityMode, false);
            ParseLine("AnimWaitBase", "", ref Prot.AnimWaitBase, CompatibilityMode, false);
            ParseLine("AnimWaitRndMin", "", ref Prot.AnimWaitRndMin, CompatibilityMode, false);
            ParseLine("AnimWaitRndMax", "", ref Prot.AnimWaitRndMax, CompatibilityMode, false);
            ParseLine("AnimStay_0", "", ref Prot.AnimStay_0, CompatibilityMode, false);
            ParseLine("AnimStay_1", "", ref Prot.AnimStay_1, CompatibilityMode, false);
            ParseLine("AnimShow_0", "", ref Prot.AnimShow_0, CompatibilityMode, false);
            ParseLine("AnimShow_1", "", ref Prot.AnimShow_1, CompatibilityMode, false);
            ParseLine("AnimHide_0", "", ref Prot.AnimHide_0, CompatibilityMode, false);
            ParseLine("AnimHide_1", "", ref Prot.AnimHide_1, CompatibilityMode, false);
            ParseLine("OffsetX", "", ref Prot.OffsetX, CompatibilityMode, false);
            ParseLine("OffsetY", "", ref Prot.OffsetY, CompatibilityMode, false);
            ParseLine("SpriteCut", "", ref Prot.SpriteCut, CompatibilityMode, false);
            ParseLine("DrawOrderOffsetHexY", "", ref Prot.DrawOrderOffsetHexY, CompatibilityMode, false);
            ParseLine("RadioChannel", "", ref Prot.RadioChannel, CompatibilityMode, false);
            ParseLine("RadioFlags", "", ref Prot.RadioFlags, CompatibilityMode, false);
            ParseLine("RadioBroadcastSend", "", ref Prot.RadioBroadcastSend, CompatibilityMode, false);
            ParseLine("RadioBroadcastRecv", "", ref Prot.RadioBroadcastRecv, CompatibilityMode, false);
            ParseLine("IndicatorStart", "", ref Prot.IndicatorStart, CompatibilityMode, false);
            ParseLine("IndicatorMax", "", ref Prot.IndicatorMax, CompatibilityMode, false);
            ParseLine("HolodiskNum", "", ref Prot.HolodiskNum, CompatibilityMode, false);
            ParseLine("Weapon_IsUnarmed", "Weapon.IsUnarmed", ref Prot.Weapon_IsUnarmed, CompatibilityMode, false);
            ParseLine("Weapon_UnarmedTree", "Weapon.UnarmedTree", ref Prot.Weapon_UnarmedTree, CompatibilityMode, false);
            ParseLine("Weapon_UnarmedPriority", "Weapon.UnarmedPriority", ref Prot.Weapon_UnarmedPriority, CompatibilityMode, false);
            ParseLine("Weapon_UnarmedMinAgility", "Weapon.UnarmedMinAgility", ref Prot.Weapon_UnarmedMinAgility, CompatibilityMode, false);
            ParseLine("Weapon_UnarmedMinUnarmed", "Weapon.UnarmedMinUnarmed", ref Prot.Weapon_UnarmedMinUnarmed, CompatibilityMode, false);
            ParseLine("Weapon_UnarmedMinLevel", "Weapon.UnarmedMinLevel", ref Prot.Weapon_UnarmedMinLevel, CompatibilityMode, false);
            ParseLine("Weapon_Anim1", "Weapon.Anim1", ref Prot.Weapon_Anim1, CompatibilityMode, false);
            ParseLine("Weapon_MaxAmmoCount", "Weapon.VolHolder", ref Prot.Weapon_MaxAmmoCount, CompatibilityMode, false);
            ParseLine("Weapon_Caliber", "Weapon.Caliber", ref Prot.Weapon_Caliber, CompatibilityMode, false);
            ParseLine("Weapon_DefaultAmmoPid", "Weapon.DefAmmo", ref Prot.Weapon_DefaultAmmoPid, CompatibilityMode, false);
            ParseLine("Weapon_MinStrength", "Weapon.MinSt", ref Prot.Weapon_MinStrength, CompatibilityMode, false);
            ParseLine("Weapon_Perk", "Weapon.Perk", ref Prot.Weapon_Perk, CompatibilityMode, false);
            ParseLine("Weapon_ActiveUses", "Weapon.CountAttack", ref Prot.Weapon_ActiveUses, CompatibilityMode, false);
            ParseLine("Weapon_CriticalFailture", "Weapon.CrFailture", ref Prot.Weapon_CriticalFailture, CompatibilityMode, false);
            ParseLine("Weapon_Skill_0", "Weapon.Skill_0", ref Prot.Weapon_Skill_0, CompatibilityMode, false);
            ParseLine("Weapon_Skill_1", "Weapon.Skill_1", ref Prot.Weapon_Skill_1, CompatibilityMode, false);
            ParseLine("Weapon_Skill_2", "Weapon.Skill_2", ref Prot.Weapon_Skill_2, CompatibilityMode, false);
            ParseLine("Weapon_DmgType_0", "Weapon.DmgType_0", ref Prot.Weapon_DmgType_0, CompatibilityMode, false);
            ParseLine("Weapon_DmgType_1", "Weapon.DmgType_1", ref Prot.Weapon_DmgType_1, CompatibilityMode, false);
            ParseLine("Weapon_DmgType_2", "Weapon.DmgType_2", ref Prot.Weapon_DmgType_2, CompatibilityMode, false);
            ParseLine("Weapon_Anim2_0", "Weapon.Anim2_0", ref Prot.Weapon_Anim2_0, CompatibilityMode, false);
            ParseLine("Weapon_Anim2_1", "Weapon.Anim2_1", ref Prot.Weapon_Anim2_1, CompatibilityMode, false);
            ParseLine("Weapon_Anim2_2", "Weapon.Anim2_2", ref Prot.Weapon_Anim2_2, CompatibilityMode, false);
            ParseLine("Weapon_Remove_0", "Weapon.Remove_0", ref Prot.Weapon_Remove_0, CompatibilityMode, false);
            ParseLine("Weapon_Remove_1", "Weapon.Remove_1", ref Prot.Weapon_Remove_1, CompatibilityMode, false);
            ParseLine("Weapon_Remove_2", "Weapon.Remove_2", ref Prot.Weapon_Remove_2, CompatibilityMode, false);
            ParseLine("Weapon_PicUse_0", "Weapon.PicName_0", ref Prot.Weapon_PicUse_0, CompatibilityMode, false);
            ParseLine("Weapon_PicUse_1", "Weapon.PicName_1", ref Prot.Weapon_PicUse_1, CompatibilityMode, false);
            ParseLine("Weapon_PicUse_2", "Weapon.PicName_2", ref Prot.Weapon_PicUse_2, CompatibilityMode, false);
            ParseLine("Weapon_DmgMin_0", "Weapon.DmgMin_0", ref Prot.Weapon_DmgMin_0, CompatibilityMode, false);
            ParseLine("Weapon_DmgMin_1", "Weapon.DmgMin_1", ref Prot.Weapon_DmgMin_1, CompatibilityMode, false);
            ParseLine("Weapon_DmgMin_2", "Weapon.DmgMin_2", ref Prot.Weapon_DmgMin_2, CompatibilityMode, false);
            ParseLine("Weapon_DmgMax_0", "Weapon.DmgMax_0", ref Prot.Weapon_DmgMax_0, CompatibilityMode, false);
            ParseLine("Weapon_DmgMax_1", "Weapon.DmgMax_1", ref Prot.Weapon_DmgMax_1, CompatibilityMode, false);
            ParseLine("Weapon_DmgMax_2", "Weapon.DmgMax_2", ref Prot.Weapon_DmgMax_2, CompatibilityMode, false);
            ParseLine("Weapon_Effect_0", "Weapon.Effect_0", ref Prot.Weapon_FlyEffect_0, CompatibilityMode, false);
            ParseLine("Weapon_Effect_1", "Weapon.Effect_1", ref Prot.Weapon_FlyEffect_1, CompatibilityMode, false);
            ParseLine("Weapon_Effect_2", "Weapon.Effect_2", ref Prot.Weapon_FlyEffect_2, CompatibilityMode, false);
            ParseLine("Weapon_MaxDist_0", "Weapon.MaxDist_0", ref Prot.Weapon_MaxDist_0, CompatibilityMode, false);
            ParseLine("Weapon_MaxDist_1", "Weapon.MaxDist_1", ref Prot.Weapon_MaxDist_1, CompatibilityMode, false);
            ParseLine("Weapon_MaxDist_2", "Weapon.MaxDist_2", ref Prot.Weapon_MaxDist_2, CompatibilityMode, false);
            ParseLine("Weapon_Round_0", "Weapon.Round_0", ref Prot.Weapon_Round_0, CompatibilityMode, false);
            ParseLine("Weapon_Round_1", "Weapon.Round_1", ref Prot.Weapon_Round_1, CompatibilityMode, false);
            ParseLine("Weapon_Round_2", "Weapon.Round_2", ref Prot.Weapon_Round_2, CompatibilityMode, false);
            ParseLine("Weapon_ApCost_0", "Weapon.Time_0", ref Prot.Weapon_ApCost_0, CompatibilityMode, false);
            ParseLine("Weapon_ApCost_1", "Weapon.Time_1", ref Prot.Weapon_ApCost_1, CompatibilityMode, false);
            ParseLine("Weapon_ApCost_2", "Weapon.Time_2", ref Prot.Weapon_ApCost_2, CompatibilityMode, false);
            ParseLine("Weapon_Aim_0", "Weapon.Aim_0", ref Prot.Weapon_Aim_0, CompatibilityMode, false);
            ParseLine("Weapon_Aim_1", "Weapon.Aim_1", ref Prot.Weapon_Aim_1, CompatibilityMode, false);
            ParseLine("Weapon_Aim_2", "Weapon.Aim_2", ref Prot.Weapon_Aim_2, CompatibilityMode, false);
            ParseLine("Weapon_SoundId_0", "Weapon.SoundId_0", ref Prot.Weapon_SoundId_0, CompatibilityMode, false);
            ParseLine("Weapon_SoundId_1", "Weapon.SoundId_1", ref Prot.Weapon_SoundId_1, CompatibilityMode, false);
            ParseLine("Weapon_SoundId_2", "Weapon.SoundId_2", ref Prot.Weapon_SoundId_2, CompatibilityMode, false);
            ParseLine("Ammo_Caliber", "Ammo.Caliber", ref Prot.Ammo_Caliber, CompatibilityMode, false);
            ParseLine("Ammo_AcMod", "Ammo.ACMod", ref Prot.Ammo_AcMod, CompatibilityMode, false);
            ParseLine("Ammo_DrMod", "Ammo.DRMod", ref Prot.Ammo_DrMod, CompatibilityMode, false);
            ParseLine("Door_NoBlockMove", "Door.NoBlockMove", ref Prot.Door_NoBlockMove, CompatibilityMode, false);
            ParseLine("Door_NoBlockShoot", "Door.NoBlockShoot", ref Prot.Door_NoBlockShoot, CompatibilityMode, false);
            ParseLine("Door_NoBlockLight", "Door.NoBlockLight", ref Prot.Door_NoBlockLight, CompatibilityMode, false);
            ParseLine("Container_Volume", "Container.ContVolume", ref Prot.Container_Volume, CompatibilityMode, false);
            ParseLine("Container_Changeble", "Container.Changeble", ref Prot.Container_Changeble, CompatibilityMode, false);
            ParseLine("Container_CannotPickUp", "Container.CannotPickUp", ref Prot.Container_CannotPickUp, CompatibilityMode, false);
            ParseLine("Container_MagicHandsGrnd", "Container.MagicHandsGrnd", ref Prot.Container_MagicHandsGrnd, CompatibilityMode, false);
            ParseLine("Locker_Condition", "Locker.Condition", ref Prot.Locker_Condition, CompatibilityMode, false);
            ParseLine("Grid_Type", "Grid.Type", ref Prot.Grid_Type, CompatibilityMode, false);
            ParseLine("Armor_CrTypeMale", "Armor.Anim0Male", ref Prot.Armor_CrTypeMale, CompatibilityMode, false);
            ParseLine("Armor_CrTypeFemale", "Armor.Anim0Female", ref Prot.Armor_CrTypeFemale, CompatibilityMode, false);
            ParseLine("ChildLines_0", "MiscEx.Car.Bag_0", ref Prot.ChildLines[0], CompatibilityMode, false);
            ParseLine("ChildLines_1", "MiscEx.Car.Bag_1", ref Prot.ChildLines[1], CompatibilityMode, false);
            ParseLine("ChildLines_2", "", ref Prot.ChildLines[2], CompatibilityMode, false);
            ParseLine("ChildLines_3", "", ref Prot.ChildLines[3], CompatibilityMode, false);
            ParseLine("ChildLines_4", "", ref Prot.ChildLines[4], CompatibilityMode, false);
            ParseLine("BlockLines", "MiscEx.Car.Blocks", ref Prot.BlockLines, CompatibilityMode, false);

            for (uint i = 0; i < Prot.ChildPid.Length; i++)
                ParseLine("ChildPid_" + i, "" + i, ref Prot.ChildPid[i], CompatibilityMode, false);

            for (uint k = 1; k <= Prot.StartValue.Length; k++)
                ParseLine("StartValue_" + k, "MiscEx.StartVal" + k, ref Prot.StartValue[k-1], CompatibilityMode, false);

            ParseLine("Car_Speed", "MiscEx.Car.Speed", ref Prot.Car_Speed, CompatibilityMode, false);
            ParseLine("Car_Passability", "MiscEx.Car.Negotiability", ref Prot.Car_Passability, CompatibilityMode, false);
            ParseLine("Car_DeteriorationRate", "MiscEx.Car.WearConsumption", ref Prot.Car_DeteriorationRate, CompatibilityMode, false);
            ParseLine("Car_CrittersCapacity", "MiscEx.Car.CritCapacity", ref Prot.Car_CrittersCapacity, CompatibilityMode, false);
            ParseLine("Car_TankVolume", "MiscEx.Car.FuelTank", ref Prot.Car_TankVolume, CompatibilityMode, false);
            ParseLine("Car_MaxDeterioration", "MiscEx.Car.RunToBreak", ref Prot.Car_MaxDeterioration, CompatibilityMode, false);
            ParseLine("Car_Entrance", "MiscEx.Car.Entire", ref Prot.Car_Entrance, CompatibilityMode, false);
            ParseLine("Car_FuelConsumption", "MiscEx.Car.FuelConsumption", ref Prot.Car_FuelConsumption, CompatibilityMode, false);
            ParseLine("Car_MovementType", "MiscEx.Car.WalkType", ref Prot.Car_MovementType, CompatibilityMode, false);
            #endregion
        }

        // TODO: Refactor
        public bool Load(string FilePath, string Version, List<String> Lines, MSGParser FOObj, Dictionary<String, ItemProtoCustomField> CustomFields)
        {
            ItemProto Prot = null;
            bool CompatibilityMode = false;
            bool DuplicateFound = false;

            List<int> ProcessedPids = new List<int>();

            foreach (ItemProto LProt in _loadedProtos)
                ProcessedPids.Add(LProt.ProtoId);

            bool IsCar = false; // Compatibility
            bool IsNoOpen = false; // Compatibility

            int j = Lines.Count;
            for(int i=0;i<j;i++)// String Line in Lines)
            {
                string Line = Lines[i];
                if (Line.Replace(" ", "").StartsWith("[Proto]") || i==Lines.Count-1)
                {
                    if (Prot != null)
                    {
                        if (FOObj != null)
                        {
                            Prot.Name = FOObj.GetMSGValue(Prot.ProtoId * 100);
                            Prot.Description = FOObj.GetMSGValue(Prot.ProtoId * 100 + 1);
                        }
                        //Prot.FileName = Path.GetFileName(FilePath);
                        Prot.FileName = FilePath;
                        if (CompatibilityMode)
                        {
                            ConvertStuff(ref Prot, IsCar, IsNoOpen);
                            CompatibilityMode = false;
                        }
                        IsCar = false;
                        IsNoOpen = false;

                        if (ProcessedPids.Contains(Prot.ProtoId))
                        {
                            DuplicateFound = true;
                            Utils.Log("An object with the ProtoId " + Prot.ProtoId + " was already loaded. Overwriting proto.");
                            for (ushort u = 0; u < _loadedProtos.Count; u++)
                                if (_loadedProtos[u].ProtoId == Prot.ProtoId)
                                    _loadedProtos[u] = Prot;
                        }
                        else
                        {
                            if(Prot.ProtoId!=0)
                                _loadedProtos.Add(Prot);
                        }

                        ProcessedPids.Add(Prot.ProtoId);
                    }
                    Prot = new ItemProto();
                    continue;
                }

                if (string.IsNullOrEmpty(Lines[i]) || (Lines[i].Length>0 && Lines[i][0]=='#'))
                    continue;

                if (FOObj != null && Prot!=null)
                {
                    Prot.Name = FOObj.GetMSGValue(Prot.ProtoId * 100);
                    Prot.Description = FOObj.GetMSGValue(Prot.ProtoId * 100 + 1);
                }

                String[] Parts = Line.Split('=');
                if (Parts.Length != 2)
                    continue;
                _fieldName = Parts[0].Trim();
                _fieldValue = Parts[1].TrimStart(' ','\t');

                if (Prot == null) continue;

                bool shouldParseField = true;
                if (_fieldName == "Pid") 
                { 
                    Prot.ProtoId = ushort.Parse(_fieldValue); 
                    CompatibilityMode = true;
                    shouldParseField = false;
                    Utils.Log($"An object with the ProtoId {Prot.ProtoId} has deprecated 'Pid' field.");
                }
                if (_fieldName == "MiscEx.IsCar")
                {
                    IsCar = true;
                    shouldParseField = false;
                }
                if (_fieldName == "Container.IsNoOpen")
                {
                    IsNoOpen = true;
                    shouldParseField = false;
                }

                if(Prot!=null && shouldParseField)
                    shouldParseField = !ParseData(ref Prot, CompatibilityMode);

                if (!shouldParseField)
                    continue;

                bool notInCustFieldsList = true;
                if(CustomFields != null)
                {
                    foreach (KeyValuePair<String, ItemProtoCustomField> kvp in CustomFields)
                    {
                        if (_fieldName == kvp.Key || _fieldName.Replace('.', '_') == kvp.Key)
                        {
                            try
                            {
                                ItemProtoCustomField NewCustom = new ItemProtoCustomField(kvp.Key,
                                                                                          (Utils.DataType)
                                                                                          kvp.Value.Type);
                                if (NewCustom.Type == (Byte)Utils.DataType.BOOL)
                                    NewCustom.Value = (_fieldValue == "1");
                                else if (NewCustom.Type == (Byte)Utils.DataType.SBYTE)
                                    NewCustom.Value = SByte.Parse(_fieldValue);
                                else if (NewCustom.Type == (Byte)Utils.DataType.INT)
                                    NewCustom.Value = Int32.Parse(_fieldValue);
                                else if (NewCustom.Type == (Byte)Utils.DataType.INT16)
                                    NewCustom.Value = Int16.Parse(_fieldValue);
                                else if (NewCustom.Type == (Byte)Utils.DataType.UINT)
                                    NewCustom.Value = UInt32.Parse(_fieldValue);
                                else if (NewCustom.Type == (Byte)Utils.DataType.UINT16)
                                    NewCustom.Value = UInt16.Parse(_fieldValue);
                                else if (NewCustom.Type == (Byte)Utils.DataType.STRING)
                                    NewCustom.Value = _fieldValue;
                                Prot.CustomFields.Add(kvp.Key, NewCustom);
                                notInCustFieldsList = false;
                                break;
                            }
                            catch (OverflowException ex)
                            {
                                Utils.Log(ex.Message + " in ProtoId " + Prot.ProtoId + " ,field " + _fieldName +
                                          " value: " + _fieldValue);
                            }
                        }
                    }
                }

                if (notInCustFieldsList)
                {
                    if(Prot.CustomFields.ContainsKey(_fieldName))
                    {
                        Utils.Log($"An object with the ProtoId {Prot.ProtoId} has duplicate field with name '{_fieldName}'.");
                        continue;
                    }

                    ItemProtoCustomField NewCustom = new ItemProtoCustomField(_fieldName, Utils.DataType.NOTYPE) { Value = _fieldValue };
                    Prot.CustomFields.Add(_fieldName, NewCustom);
                }
            }
            return DuplicateFound;
        }

        public bool LoadProtosFromFile(String Path, String Version, MSGParser FOObj, List<ItemProto> LoadedProtos,
            Dictionary<string, ItemProtoCustomField> CustomFields)
        {
            List<String> Lines = new List<string>(File.ReadAllLines(Path, new UTF8Encoding(false)));
            Lines.Add(""); // Ugly crutch to make last line in file being parsed properly, or last field might be not parsed - APAMk2
            this._loadedProtos = LoadedProtos;
            return Load(Path, Version, Lines, FOObj, CustomFields);
        }

        public bool SaveProtosToFile(String Path, String Version, MSGParser FOObj, List<ItemProto> SaveProtos, bool FormatWithSpace, Dictionary<int, string> definesList)
        {
            this._saveProtos = SaveProtos;
            this._formatWithSpaces = FormatWithSpace;
            bool DuplicateFound = Save(Path, Version, null, FOObj, definesList);
            File.WriteAllLines(Path, _saveLines.ToArray(), new UTF8Encoding(false));
            return DuplicateFound;
        }
    }
}
