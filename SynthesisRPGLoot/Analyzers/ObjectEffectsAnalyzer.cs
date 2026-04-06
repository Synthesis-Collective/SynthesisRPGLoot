using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using SynthesisRPGLoot.Settings;
using SynthesisRPGLoot.Settings.Enums;

namespace SynthesisRPGLoot.Analyzers
{
    public class ObjectEffectsAnalyzer
    {
        private readonly EnchantmentSettings _settings;

        public Dictionary<FormKey, IObjectEffectGetter> AllObjectEffects { get; set; }

        public ObjectEffectsAnalyzer(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, EnchantmentSettings settings)
        {
            _settings = settings;

            var pluginObjectEffectGroups = loadOrder.PriorityOrder.Select(listing => (IModListingGetter<ISkyrimModGetter>)listing).Select(listing => listing.Mod).NotNull()
                .Select(x => (x.ModKey, x.ObjectEffects)).AsParallel()
                .Where(x => x.ObjectEffects.Count > 0 && _settings.PluginList.Contains(x.ModKey))
                .Select(x => x.ObjectEffects).Distinct()
                .ToHashSet();

            var pluginObjectEffects = new HashSet<IObjectEffectGetter>();

            foreach (var objectEffectGetter in pluginObjectEffectGroups.SelectMany(objectEffectGroup => objectEffectGroup).ToHashSet())
            {
                pluginObjectEffects.Add(objectEffectGetter);
            }


            AllObjectEffects =
                loadOrder.PriorityOrder.ObjectEffect().WinningOverrides()
                    .Where(x => x.Name != null)
                    .Where(x =>
                    {
                        switch (_settings.EnchantmentListMode)
                        {
                            case ListMode.Blacklist:
                                switch (_settings.PluginListMode)
                                {
                                    case ListMode.Blacklist:
                                        return !_settings.EnchantmentList.Contains(x) &&
                                               !pluginObjectEffects.Contains(x);
                                    case ListMode.Whitelist:
                                        return !_settings.EnchantmentList.Contains(x) &&
                                               pluginObjectEffects.Contains(x);
                                    default:
                                        throw new();
                                }

                            case ListMode.Whitelist:
                                switch (_settings.PluginListMode)
                                {
                                    case ListMode.Blacklist:
                                        return _settings.EnchantmentList.Contains(x) ||
                                               !pluginObjectEffects.Contains(x);
                                    case ListMode.Whitelist:
                                        return _settings.EnchantmentList.Contains(x) ||
                                               pluginObjectEffects.Contains(x);
                                    default:
                                        throw new();
                                }
                            default:
                                throw new();
                        }
                    })
                    .ToDictionary(k => k.FormKey);
        }
    }
}