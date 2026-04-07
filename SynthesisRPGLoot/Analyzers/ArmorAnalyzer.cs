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
    public class ArmorAnalyzer : GearAnalyzer<IArmorGetter>
    {

        public ArmorAnalyzer(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ISkyrimMod patchMod,
            ObjectEffectsAnalyzer objectEffectsAnalyzer, Settings.Settings settings)
            : base(settings.RarityAndVariationDistributionSettings.ArmorSettings, loadOrder, linkCache, patchMod, objectEffectsAnalyzer, settings)
        {
            ConfiguredNameGenerator = new (2,settings);
            EditorIdPrefix = "HAL_ARMOR_";
            ItemTypeDescriptor = " armor";

        }

        protected override bool IsValidItem(IArmorGetter item)
        {
            return base.IsValidItem(item) && !item.MajorFlags.HasFlag(Armor.MajorFlag.NonPlayable);
        }

        protected override FormKey EnchantItem(ResolvedListItem<IArmorGetter> item, int rarity)
        {
            if (!(item.Resolved.Name?.TryLookup(Language.English, out var itemName) ?? false))
            {
                itemName = MakeName(item.Resolved.EditorID);
            }

            if (RarityClasses[rarity].NumEnchantments != 0)
            {
                var generatedEnchantmentFormKey = GenerateEnchantment(rarity);
                var effects = ChosenRpgEnchantEffects[rarity].GetValueOrDefault(generatedEnchantmentFormKey);
                var newArmorEditorId = EditorIdPrefix + RarityClasses[rarity].Label.ToUpper() + "_" +
                                       itemName +
                                       "_of_" + GetEnchantmentsStringForName(effects, true);
                if (GeneratedItemCache.TryGetValue(newArmorEditorId, out var armorGetter))
                {
                    return armorGetter.FormKey;
                }

                var newArmor = PatchMod.Armors.AddNewLocking(PatchMod.GetNextFormKey());
                newArmor.DeepCopyIn(item.Resolved);
                newArmor.EditorID = newArmorEditorId;
                newArmor.ObjectEffect.SetTo(generatedEnchantmentFormKey);
                newArmor.EnchantmentAmount = (ushort) effects.Where(e => e.Amount.HasValue).Sum(e => e.Amount.Value);
                
                newArmor.Name = LabelMaker(rarity,itemName,effects);
                
                newArmor.TemplateArmor = item.Resolved.ToNullableLink();

                if (!RarityClasses[rarity].AllowDisenchanting)
                {
                    newArmor.Keywords?.Add(Skyrim.Keyword.MagicDisallowEnchanting);
                }
                
                GeneratedItemCache.Add(newArmor.EditorID, newArmor);
                
                if (Settings.GeneralSettings.LogGeneratedItems)
                    Console.WriteLine($"Generated {newArmor.Name}");
                
                return newArmor.FormKey;
            }
            else
            {
                var newArmorEditorId = EditorIdPrefix + item.Resolved.EditorID;
                if (GeneratedItemCache.TryGetValue(newArmorEditorId, out var armorGetter))
                {
                    return PatchMod.Armors.GetOrAddAsOverride(armorGetter).FormKey;
                }

                var newArmor = PatchMod.Armors.AddNewLocking(PatchMod.GetNextFormKey());
                newArmor.DeepCopyIn(item.Resolved);
                newArmor.EditorID = newArmorEditorId;

                newArmor.Name = RarityClasses[rarity].Label.Equals("")
                    ? itemName
                    : RarityClasses[rarity].Label + " " + itemName;
                
                GeneratedItemCache.Add(newArmor.EditorID, newArmor);
                
                if (Settings.GeneralSettings.LogGeneratedItems)
                    Console.WriteLine($"Generated {newArmor.Name}");

                return newArmor.FormKey;
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static char[] _unusedNumbers = "123456890".ToCharArray();

        private readonly Regex _splitter =
            new("(?<=[A-Z])(?=[A-Z][a-z])|(?<=[^A-Z])(?=[A-Z])|(?<=[A-Za-z])(?=[^A-Za-z])");

        private readonly Dictionary<string, string> _knownMapping = new();

        private string MakeName(string resolvedEditorId)
        {
            string returning;
            if (resolvedEditorId == null)
            {
                returning = "Armor";
            }
            else
            {
                if (_knownMapping.TryGetValue(resolvedEditorId, out var cached))
                    return cached;

                var parts = _splitter.Split(resolvedEditorId)
                    .Where(e => e.Length > 1)
                    .Where(e => e != "DLC" && e != "Armor" && e != "Variant")
                    .Where(e => !int.TryParse(e, out var _))
                    .ToArray();
                if (parts.First() == "Clothes" && parts.Last() == "Clothes")
                    parts = parts.Skip(1).ToArray();
                if (parts.Length >= 2 && parts.First() == "Clothes")
                    parts = parts.Skip(1).ToArray();
                returning = string.Join(" ", parts);
                _knownMapping[resolvedEditorId] = returning;
            }

            Console.WriteLine(
                $"Missing {ItemTypeDescriptor} name for {resolvedEditorId ?? "<null>"} using {returning}");

            return returning;
        }
    }
}