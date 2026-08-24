using System;
using System.Reflection;

namespace ErenshorCampmaster
{
    // Optional, read-only lifecycle checks. Absence means false; a present but unreadable contract
    // means unknown so Auto Relax fails closed. No sibling mod is referenced at compile time.
    internal static class OptionalCompetitiveStateReader
    {
        private static readonly string[] DuelTypes = { "ErenshorDuel.DuelController" };
        private static readonly string[] DuelMembers = { "Active", "IsActive" };
        private static readonly string[] PvpTypes = { "ErenshorPvP.ErenshorPvpApi", "ErenshorPvP.PvpController" };
        private static readonly string[] PvpMembers = { "IsMatchActive", "MatchActive", "Active", "IsActive" };
        private static MemberInfo _duelMember;
        private static MemberInfo _pvpMember;
        private static MethodInfo _pvpStateMethod;
        private static MemberInfo _pvpStateActiveMember;
        private static bool _duelTypePresent;
        private static bool _pvpTypePresent;
        private static DateTime _nextDuelResolveUtc;
        private static DateTime _nextPvpResolveUtc;

        internal static bool? ReadDuelActive()
        {
            if (_duelMember == null && DateTime.UtcNow >= _nextDuelResolveUtc)
            {
                _nextDuelResolveUtc = DateTime.UtcNow.AddSeconds(5);
                _duelMember = Resolve(DuelTypes, DuelMembers, out _duelTypePresent);
            }
            return Read(_duelMember, _duelTypePresent);
        }

        internal static bool? ReadPvpActive()
        {
            if (_pvpStateMethod == null && _pvpMember == null && DateTime.UtcNow >= _nextPvpResolveUtc)
            {
                _nextPvpResolveUtc = DateTime.UtcNow.AddSeconds(5);
                if (!ResolvePvpControlState(out _pvpTypePresent))
                {
                    bool legacyPresent;
                    _pvpMember = Resolve(PvpTypes, PvpMembers, out legacyPresent);
                    _pvpTypePresent = _pvpTypePresent || legacyPresent;
                }
            }
            if (_pvpStateMethod != null) return ReadPvpControlState();
            return Read(_pvpMember, _pvpTypePresent);
        }

        // Current PvP 0.5.x deliberately exposes encounter ownership through the public
        // PvpControlApi.GetBasicState().EncounterActive contract rather than a static Active flag.
        // Bind that public surface first; no compile-time PvP dependency is introduced.
        private static bool ResolvePvpControlState(out bool typePresent)
        {
            typePresent = false;
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type type = assemblies[i].GetType("ErenshorPvP.PvpControlApi", false);
                    if (type == null) continue;
                    typePresent = true;
                    MethodInfo method = type.GetMethod("GetBasicState", BindingFlags.Public | BindingFlags.Static,
                        null, Type.EmptyTypes, null);
                    if (method == null || method.ReturnType == typeof(void)) return false;
                    FieldInfo field = method.ReturnType.GetField("EncounterActive", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo property = method.ReturnType.GetProperty("EncounterActive", BindingFlags.Public | BindingFlags.Instance);
                    MemberInfo active = (MemberInfo)field ?? property;
                    if (active == null) return false;
                    _pvpStateMethod = method;
                    _pvpStateActiveMember = active;
                    return true;
                }
            }
            catch { typePresent = true; }
            return false;
        }

        private static bool? ReadPvpControlState()
        {
            try
            {
                object state = _pvpStateMethod.Invoke(null, null);
                if (state == null) return null;
                FieldInfo field = _pvpStateActiveMember as FieldInfo;
                if (field != null) return (bool)field.GetValue(state);
                PropertyInfo property = _pvpStateActiveMember as PropertyInfo;
                return property == null ? (bool?)null : (bool)property.GetValue(state, null);
            }
            catch { return null; }
        }

        private static MemberInfo Resolve(string[] typeNames, string[] members, out bool typePresent)
        {
            typePresent = false;
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    for (int t = 0; t < typeNames.Length; t++)
                    {
                        Type type = assemblies[i].GetType(typeNames[t], false);
                        if (type == null) continue;
                        typePresent = true;
                        for (int j = 0; j < members.Length; j++)
                        {
                            PropertyInfo property = type.GetProperty(members[j], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (property != null && property.PropertyType == typeof(bool)) return property;
                            FieldInfo field = type.GetField(members[j], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (field != null && field.FieldType == typeof(bool)) return field;
                        }
                        // A compatibility type may intentionally omit activity state while a later
                        // current/legacy type supplies it. Keep searching instead of converting the
                        // first partial API match into permanent unknown competitive activity.
                        continue;
                    }
                }
            }
            catch { typePresent = true; }
            return null;
        }

        private static bool? Read(MemberInfo member, bool typePresent)
        {
            if (member == null) return typePresent ? (bool?)null : false;
            try
            {
                PropertyInfo property = member as PropertyInfo;
                if (property != null) return (bool)property.GetValue(null, null);
                FieldInfo field = member as FieldInfo;
                return field == null ? (bool?)null : (bool)field.GetValue(null);
            }
            catch { return null; }
        }
    }
}
