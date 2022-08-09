﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using HalgarisRPGLoot.DataModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Synthesis;

namespace HalgarisRPGLoot.Analyzers
{
    public class WeaponAnalyzer : GearAnalyzer<IWeaponGetter>
    {
        private readonly Dictionary<IFormLinkGetter<IWeaponGetter>, IConstructibleObjectGetter> _weaponDictionary;

        private readonly ObjectEffectsAnalyzer _objectEffectsAnalyzer;

        public WeaponAnalyzer(IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            Dictionary<IFormLinkGetter<IWeaponGetter>, IConstructibleObjectGetter> weaponDictionary,
            ObjectEffectsAnalyzer objectEffectsAnalyzer)
        {
            RarityVariationDistributionSettings = Program.Settings.RarityVariationDistributionSettings;
            GearSettings = RarityVariationDistributionSettings.ArmorSettings;

            EditorIdPrefix = "HAL_ARMOR_";
            ItemTypeDescriptor = " armor";

            State = state;
            _weaponDictionary = weaponDictionary;
            _objectEffectsAnalyzer = objectEffectsAnalyzer;

            switch (RarityVariationDistributionSettings.GenerationMode)
            {
                case GenerationMode.GenerateRarities:
                    VarietyCountPerItem = GearSettings.VarietyCountPerItem;
                    RarityClasses = GearSettings.RarityClasses;
                    break;
                case GenerationMode.JustDistributeEnchantments:
                    RarityClasses = new()
                    {
                        new() {Label = "", NumEnchantments = 1, RarityWeight = 100, AllowDisenchanting = true},
                    };
                    break;
            }

            AllRpgEnchants = new SortedList<String, ResolvedEnchantment[]>[RarityClasses.Count];
            for (var i = 0; i < AllRpgEnchants.Length; i++)
            {
                AllRpgEnchants[i] = new();
            }

            ChosenRpgEnchants = new Dictionary<String, FormKey>[RarityClasses.Count];
            for (var i = 0; i < ChosenRpgEnchants.Length; i++)
            {
                ChosenRpgEnchants[i] = new();
            }

            ChosenRpgEnchantEffects = new Dictionary<FormKey, ResolvedEnchantment[]>[RarityClasses.Count];
            for (var i = 0; i < ChosenRpgEnchantEffects.Length; i++)
            {
                ChosenRpgEnchantEffects[i] = new();
            }
        }

        protected override void AnalyzeGear()
        {
            AllLeveledLists = State.LoadOrder.PriorityOrder.WinningOverrides<ILeveledItemGetter>().ToArray();

            AllListItems = AllLeveledLists.SelectMany(lst => lst.Entries?.Select(entry =>
                                                             {
                                                                 if (entry.Data?.Reference.FormKey == default)
                                                                     return default;
                                                                 if (entry.Data == null) return default;
                                                                 if (!State.LinkCache.TryResolve<IWeaponGetter>(
                                                                         entry.Data.Reference.FormKey,
                                                                         out var resolved))
                                                                     return default;
                                                                 return new ResolvedListItem<IWeaponGetter>
                                                                 {
                                                                     List = lst,
                                                                     Entry = entry,
                                                                     Resolved = resolved
                                                                 };
                                                             }).Where(resolvedListItem => resolvedListItem != default)
                                                             ?? Array.Empty<ResolvedListItem<IWeaponGetter>>())
                .Where(e =>
                {
                    if (Program.Settings.GeneralSettings.OnlyProcessConstructableEquipment)
                    {
                        var kws = (e.Resolved.Keywords ?? Array.Empty<IFormLink<IKeywordGetter>>());
                        return !Extensions.CheckKeywords(kws) && _weaponDictionary.ContainsKey(e.Resolved.Template);
                    }
                    else
                    {
                        var kws = (e.Resolved.Keywords ?? Array.Empty<IFormLink<IKeywordGetter>>());
                        return !Extensions.CheckKeywords(kws);
                    }
                })
                .ToArray();

            AllUnenchantedItems = AllListItems.Where(e => e.Resolved.ObjectEffect.IsNull).ToArray();

            AllEnchantedItems = AllListItems.Where(e => !e.Resolved.ObjectEffect.IsNull).ToArray();

            AllObjectEffects = _objectEffectsAnalyzer.AllObjectEffects;

            AllEnchantments = AllEnchantedItems
                .Select(e => (e.Entry.Data?.Level, e.Resolved.EnchantmentAmount, e.Resolved.ObjectEffect.FormKey))
                .Distinct()
                .Select(e =>
                {
                    var (level, enchantmentAmount, formKey) = e;
                    if (!AllObjectEffects.TryGetValue(formKey, out var ench))
                        return default;
                    return new ResolvedEnchantment
                    {
                        Level = level ?? 1,
                        Amount = enchantmentAmount,
                        Enchantment = ench
                    };
                })
                .Where(e => e != default)
                .ToArray();

            AllLevels = AllEnchantments.Select(e => e.Level).Distinct().ToHashSet();

            var maxLvl = AllListItems.Select(i =>
            {
                Debug.Assert(i.Entry.Data != null, "Incompatible LeveledListItem.");
                return i.Entry.Data.Level;
            }).Distinct().ToHashSet().Max();

            ByLevel = AllEnchantments.GroupBy(e => e.Level)
                .OrderBy(e => e.Key)
                .Select(e => (e.Key, e.ToArray()))
                .ToArray();

            ByLevelIndexed = Enumerable.Range(0, maxLvl + 1)
                .Select(lvl => (lvl, ByLevel.Where(bl => bl.Key <= lvl).SelectMany(e => e.Item2).ToArray()))
                .ToDictionary(kv => kv.lvl, kv => kv.Item2);


            for (var coreEnchant = 0; coreEnchant < AllEnchantments.Length; coreEnchant++)
            {
                for (var i = 0; i < AllRpgEnchants.Length; i++)
                {
                    var forLevel = AllEnchantments;
                    var takeMin = Math.Min(RarityClasses[i].NumEnchantments, forLevel.Length);
                    if (takeMin <= 0) continue;
                    var resolvedEnchantments = new ResolvedEnchantment[takeMin];
                    resolvedEnchantments[0] = AllEnchantments[coreEnchant];

                    var result = new int[takeMin];
                    for (var j = 0; j < takeMin; ++j)
                        result[j] = j;

                    for (var t = takeMin; t < AllEnchantments.Length; ++t)
                    {
                        var m = Random.Next(0, t + 1);
                        if (m >= takeMin) continue;
                        result[m] = t;
                        if (t != coreEnchant) continue;
                        result[m] = result[0];
                        result[0] = t;
                    }

                    result[0] = coreEnchant;

                    for (var len = 0; len < takeMin; len++)
                    {
                        resolvedEnchantments[len] = AllEnchantments[result[len]];
                    }

                    var oldEnchantment = resolvedEnchantments.First().Enchantment;
                    var enchants = AllRpgEnchants[i];
                    Console.WriteLine(
                        $"$Generated raw {RarityClasses[i].Label} {ItemTypeDescriptor} enchantment of {oldEnchantment.Name}");
                    if (!enchants.ContainsKey(RarityClasses[i].Label + " " + oldEnchantment.Name))
                    {
                        enchants.Add(RarityClasses[i].Label + " " + oldEnchantment.Name, resolvedEnchantments);
                    }
                }
            }
        }

        protected override FormKey EnchantItem(ResolvedListItem<IWeaponGetter> item, int rarity, int currentVariation = 0)
        {
            if (!(item.Resolved?.Name?.TryLookup(Language.English, out var itemName) ?? false))
            {
                itemName = MakeName(item.Resolved?.EditorID);
            }

            Console.WriteLine("Generating Enchanted version of " + itemName);
            if (RarityClasses[rarity].NumEnchantments != 0)
            {
                var newWeapon = State.PatchMod.Weapons.AddNewLocking(State.PatchMod.GetNextFormKey());
                var generatedEnchantmentFormKey = GenerateEnchantment(rarity, currentVariation);
                var effects = ChosenRpgEnchantEffects[rarity].GetValueOrDefault(generatedEnchantmentFormKey);
                if (item.Resolved != null) newWeapon.DeepCopyIn(item.Resolved);
                newWeapon.EditorID = EditorIdPrefix + RarityClasses[rarity].Label.ToUpper() + "_" + newWeapon.EditorID +
                                "_of_" + effects?.First().Enchantment.Name;
                newWeapon.ObjectEffect.SetTo(generatedEnchantmentFormKey);
                newWeapon.EnchantmentAmount = (ushort) (effects ?? Array.Empty<ResolvedEnchantment>())
                    .Where(e => e.Amount.HasValue).Sum(e => e.Amount.Value);
                newWeapon.Name = RarityClasses[rarity].Label + " " + itemName + " of " +
                            effects?.First().Enchantment.Name;
                if (item.Resolved != null)
                    newWeapon.Template = (IFormLinkNullable<IWeaponGetter>) item.Resolved.ToNullableLinkGetter();
                
                if (RarityClasses[rarity].NumEnchantments>1)
                    newWeapon.Keywords?.Add(Skyrim.Keyword.MagicDisallowEnchanting);

                Console.WriteLine("Generated " + RarityClasses[rarity].Label + " " + itemName + " of " +
                                  effects?.First().Enchantment.Name);
                return newWeapon.FormKey;
            }
            else
            {
                var newWeapon = State.PatchMod.Weapons.AddNewLocking(State.PatchMod.GetNextFormKey());
                if (item.Resolved != null) newWeapon.DeepCopyIn(item.Resolved);
                newWeapon.EditorID = EditorIdPrefix + newWeapon.EditorID;

                newWeapon.Name = RarityClasses[rarity].Label.Equals("")
                    ? itemName
                    : RarityClasses[rarity].Label + " " + itemName;

                Console.WriteLine("Generated " + newWeapon.Name);


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

            Console.WriteLine($"Missing {ItemTypeDescriptor} name for {resolvedEditorId ?? "<null>"} using {returning}");

            return returning;
        }
    }
}