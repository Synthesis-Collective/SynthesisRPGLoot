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

        private readonly ObjectEffectsAnalyzer _objectEffectsAnalyzer;

        private readonly Settings.Settings _settings;

        public ArmorAnalyzer(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ISkyrimMod patchMod,
            ObjectEffectsAnalyzer objectEffectsAnalyzer, Settings.Settings settings)
        {
            _settings = settings;
            RarityAndVariationDistributionSettings = settings.RarityAndVariationDistributionSettings;
            GearSettings = RarityAndVariationDistributionSettings.ArmorSettings;
            ConfiguredNameGenerator = new (2,settings);

            EditorIdPrefix = "HAL_ARMOR_";
            ItemTypeDescriptor = " armor";

            LoadOrder = loadOrder;
            LinkCache = linkCache;
            PatchMod = patchMod;
            _objectEffectsAnalyzer = objectEffectsAnalyzer;

            VarietyCountPerRarity = GearSettings.VarietyCountPerItem;
            RarityClasses = GearSettings.RarityClasses;


            AllRpgEnchants = new SortedList<string, ResolvedEnchantment[]>[RarityClasses.Count];
            for (var i = 0; i < AllRpgEnchants.Length; i++)
            {
                AllRpgEnchants[i] = new();
            }

            ChosenRpgEnchants = new Dictionary<string, FormKey>[RarityClasses.Count];
            for (var i = 0; i < ChosenRpgEnchants.Length; i++)
            {
                ChosenRpgEnchants[i] = new();
            }

            ChosenRpgEnchantEffects = new Dictionary<FormKey, ResolvedEnchantment[]>[RarityClasses.Count];
            for (var i = 0; i < ChosenRpgEnchantEffects.Length; i++)
            {
                ChosenRpgEnchantEffects[i] = new();
            }
            
            GeneratedItemCache = new();
            GeneratedLeveledItemsCache = new();

            Random = new(settings.GeneralSettings.RandomGenerationSeed);
            LeveledListFlagSettings = settings.GeneralSettings.LeveledListFlagSettings;
            EnchantmentSeparatorString = settings.NameGeneratorSettings.EnchantmentSeparator;
            LastEnchantmentSeparatorString = settings.NameGeneratorSettings.LastEnchantmentSeparator;
        }

        protected override void AnalyzeGear()
        {
            AllLeveledLists = LoadOrder.PriorityOrder.WinningOverrides<ILeveledItemGetter>().ToHashSet();

            AllListItems = AllLeveledLists.SelectMany(lst => lst.Entries?.Select(entry =>
                                                             {
                                                                 if (entry.Data?.Reference.FormKey == default)
                                                                     return default;
                                                                 if (entry.Data == null) return default;
                                                                 if (!LinkCache.TryResolve<IArmorGetter>(
                                                                         entry.Data.Reference.FormKey,
                                                                         out var resolved))
                                                                     return default;
                                                                 if (resolved.MajorFlags.HasFlag(Armor.MajorFlag
                                                                         .NonPlayable)) return default;
                                                                 return new ResolvedListItem<IArmorGetter>
                                                                 {
                                                                     List = lst,
                                                                     Entry = entry,
                                                                     Resolved = resolved
                                                                 };
                                                             }).Where(resolvedListItem => resolvedListItem != default)
                                                             ?? Array.Empty<ResolvedListItem<IArmorGetter>>())
                .Where(e =>
                {
                    var kws = (e.Resolved.Keywords ?? Array.Empty<IFormLink<IKeywordGetter>>());
                    return !Extensions.CheckKeywords(kws);
                })
                .ToHashSet();

            AllUnenchantedItems = AllListItems.Where(e => e.Resolved.ObjectEffect.IsNull).ToHashSet();

            AllEnchantedItems = AllListItems.Where(e => !e.Resolved.ObjectEffect.IsNull).ToHashSet();

            AllObjectEffects = _objectEffectsAnalyzer.AllObjectEffects;

            AllEnchantments = AllEnchantedItems
                .Select(e => (e.Entry.Data!.Level, e.Resolved.EnchantmentAmount, e.Resolved.ObjectEffect.FormKey))
                .Distinct()
                .Select(e =>
                {
                    var (level, enchantmentAmount, formKey) = e;
                    if (!AllObjectEffects.TryGetValue(formKey, out var ench))
                        return default;
                    return new ResolvedEnchantment
                    {
                        Level = level,
                        Amount = enchantmentAmount,
                        Enchantment = ench
                    };
                })
                .Where(e => e != default)
                .ToArray();

            AllLevels = AllEnchantments.Select(e => e.Level).Distinct().ToHashSet();

            var maxLvl = AllListItems.Select(i => i.Entry.Data!.Level).Distinct().ToHashSet().Max();

            ByLevel = AllEnchantments.GroupBy(e => e.Level)
                .OrderBy(e => e.Key)
                .Select(e => (e.Key, e.ToHashSet()))
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

                    var newEnchantmentsForName = GetEnchantmentsStringForName(resolvedEnchantments);
                    var enchants = AllRpgEnchants[i];

                    if (!enchants.ContainsKey(RarityClasses[i].Label + " " + newEnchantmentsForName))
                    {
                        enchants.Add(RarityClasses[i].Label + " " + newEnchantmentsForName, resolvedEnchantments);
                    }
                }
            }
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
                
                if (_settings.GeneralSettings.LogGeneratedItems)
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
                
                if (_settings.GeneralSettings.LogGeneratedItems)
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