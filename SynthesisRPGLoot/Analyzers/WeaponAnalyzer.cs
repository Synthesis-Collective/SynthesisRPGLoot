using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SynthesisRPGLoot.DataModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Synthesis;

namespace SynthesisRPGLoot.Analyzers
{
    public class WeaponAnalyzer : GearAnalyzer<IWeaponGetter>
    {

        public WeaponAnalyzer(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ISkyrimMod patchMod,
            ObjectEffectsAnalyzer objectEffectsAnalyzer, Settings.Settings settings)
            : base(settings.RarityAndVariationDistributionSettings.WeaponSettings, loadOrder, linkCache, patchMod, objectEffectsAnalyzer, settings)
        {
            ConfiguredNameGenerator = new(3,settings);

            EditorIdPrefix = "HAL_WEAPON_";
            ItemTypeDescriptor = " weapon";
        }

        protected override bool IsValidItem(IWeaponGetter item)
        {
            return base.IsValidItem(item) && !item.MajorFlags.HasFlag(Weapon.MajorFlag.NonPlayable);
        }

        protected override FormKey EnchantItem(ResolvedListItem<IWeaponGetter> item, int rarity)
        {
            if (!(item.Resolved?.Name?.TryLookup(Language.English, out var itemName) ?? false))
            {
                itemName = MakeName(item.Resolved!.EditorID);
            }
            
            if (RarityClasses[rarity].NumEnchantments != 0)
            {
                var generatedEnchantmentFormKey = GenerateEnchantment(rarity);
                var effects = ChosenRpgEnchantEffects[rarity].GetValueOrDefault(generatedEnchantmentFormKey);
                var newWeaponEditorId = EditorIdPrefix + RarityClasses[rarity].Label.ToUpper() + "_" +
                                        itemName +
                                        "_of_" + GetEnchantmentsStringForName(effects, true);
                if (GeneratedItemCache.TryGetValue(newWeaponEditorId, out var weaponGetter))
                {
                    return weaponGetter.FormKey;
                }

                var newWeapon = PatchMod.Weapons.AddNewLocking(PatchMod.GetNextFormKey());
                newWeapon.DeepCopyIn(item.Resolved);
                newWeapon.EditorID = newWeaponEditorId;
                newWeapon.ObjectEffect.SetTo(generatedEnchantmentFormKey);
                newWeapon.EnchantmentAmount = (ushort) effects.Where(e => e.Amount.HasValue).Sum(e => e.Amount.Value);
                
                newWeapon.Name = LabelMaker(rarity,itemName,effects);
                
                newWeapon.Template = item.Resolved.ToNullableLink();

                if (!RarityClasses[rarity].AllowDisenchanting)
                {
                    newWeapon.Keywords?.Add(Skyrim.Keyword.MagicDisallowEnchanting);
                }
                
                GeneratedItemCache.Add(newWeapon.EditorID, newWeapon);
                
                if (Settings.GeneralSettings.LogGeneratedItems)
                    Console.WriteLine($"Generated {newWeapon.Name}");

                return newWeapon.FormKey;
            }
            else
            {
                var newWeaponEditorId = EditorIdPrefix + item.Resolved.EditorID;
                if (GeneratedItemCache.TryGetValue(newWeaponEditorId, out var weaponGetter))
                {
                    return weaponGetter.FormKey;
                }
                
                var newWeapon = PatchMod.Weapons.AddNewLocking(PatchMod.GetNextFormKey());
                newWeapon.DeepCopyIn(item.Resolved);
                newWeapon.EditorID = newWeaponEditorId;

                newWeapon.Name = RarityClasses[rarity].Label.Equals("")
                    ? itemName
                    : RarityClasses[rarity].Label + " " + itemName;
                
                GeneratedItemCache.Add(newWeapon.EditorID, newWeapon);
                
                if (Settings.GeneralSettings.LogGeneratedItems)
                    Console.WriteLine($"Generated {newWeapon.Name}");

                return newWeapon.FormKey;
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static char[] _unusedNumbers = "123456890".ToCharArray();

        private static readonly Regex Splitter =
            new("(?<=[A-Z])(?=[A-Z][a-z])|(?<=[^A-Z])(?=[A-Z])|(?<=[A-Za-z])(?=[^A-Za-z])");

        private readonly Dictionary<string, string> _knownMapping = new();

        private string MakeName(string resolvedEditorId)
        {
            string returning;
            if (resolvedEditorId == null)
            {
                returning = "Weapon";
            }
            else
            {
                if (_knownMapping.TryGetValue(resolvedEditorId, out var cached))
                    return cached;

                var parts = Splitter.Split(resolvedEditorId)
                    .Where(e => e.Length > 1)
                    .Where(e => e != "DLC" && e != "Weapon" && e != "Variant")
                    .Where(e => !int.TryParse(e, out var _))
                    .ToArray();

                returning = string.Join(" ", parts);
                _knownMapping[resolvedEditorId] = returning;
            }

            Console.WriteLine(
                $"Missing {ItemTypeDescriptor} name for {resolvedEditorId ?? "<null>"} using {returning}");

            return returning;
        }
    }
}