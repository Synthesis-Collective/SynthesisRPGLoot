﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;

namespace HalgarisRPGLoot
{
    public static class Extensions
    {
        public static TC AddNewLocking<TC>(this SkyrimGroup<TC> itms, FormKey val)
        where TC : class, ISkyrimMajorRecordInternal
        {
            lock (itms)
            {
                return itms.AddNew(val);
            }
        }

        public static Exception IncompatibleLoadOrderException
        {
            get
            {
                return IncompatibleLoadOrderException;
            }
        }

        public static bool CheckKeywords(IReadOnlyList<IFormLinkGetter<IKeywordGetter>> kws)
        {
            foreach (var untouchableEquipmentKeyword in Program.Settings.UntouchableEquipmentKeywords)
            {
                if (kws.Contains(untouchableEquipmentKeyword)) return true;
            }
            return false;
        }
    }
}
