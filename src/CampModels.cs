using System;
using System.Collections.Generic;
using System.Globalization;

namespace ErenshorCampmaster
{
    // ---------------------------------------------------------------------
    // Pure data model. Nothing in this file may reference UnityEngine or the
    // game assembly: the deterministic test runner compiles it standalone.
    // ---------------------------------------------------------------------

    internal enum CampSessionState
    {
        Inactive = 0,
        Establishing = 1,
        Active = 2,
        Suspended = 3,
        Breaking = 4
    }

    // Phase 3: Fighting is the strongest signal. Pulling comes from the
    // verified SimPlayer pull lifecycle. Recovering is only used when the
    // exact native per-healer mana gate is currently blocking Auto Pull.
    internal enum CampActivity
    {
        Unknown = 0,
        Waiting = 1,
        Fighting = 2,
        Pulling = 3,
        Recovering = 4
    }

    internal enum CampRecognitionSource
    {
        None = 0,
        Explicit = 1,
        Auto = 2
    }

    internal enum CampAuthority
    {
        Unknown = 0,
        FullLocal = 1,
        PartialCoop = 2
    }

    // How a native role slot is filled. Unknown is a first-class value and is
    // never replaced by a class-derived guess.
    internal enum RoleHolderKind
    {
        Unknown = 0,
        Player = 1,
        Sim = 2,
        None = 3
    }

    internal struct RoleHolder
    {
        internal readonly RoleHolderKind Kind;
        internal readonly string Name;

        internal RoleHolder(RoleHolderKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        internal static RoleHolder Unknown { get { return new RoleHolder(RoleHolderKind.Unknown, null); } }
        internal static RoleHolder None { get { return new RoleHolder(RoleHolderKind.None, null); } }
        internal static RoleHolder Player(string name) { return new RoleHolder(RoleHolderKind.Player, name); }
        internal static RoleHolder Sim(string name) { return new RoleHolder(RoleHolderKind.Sim, name); }

        // IsKnown deliberately continues to mean "has an assigned holder" so
        // existing recognition logic remains backwards-compatible. IsResolved
        // also includes a verified native "none assigned" state.
        internal bool IsKnown { get { return Kind == RoleHolderKind.Player || Kind == RoleHolderKind.Sim; } }
        internal bool IsResolved { get { return Kind != RoleHolderKind.Unknown; } }

        internal string Describe()
        {
            switch (Kind)
            {
                case RoleHolderKind.Player: return string.IsNullOrEmpty(Name) ? "you" : (Name + " (you)");
                case RoleHolderKind.Sim: return string.IsNullOrEmpty(Name) ? "unknown" : Name;
                case RoleHolderKind.None: return "none";
                default: return "unknown";
            }
        }
    }

    // Unity-free position so the state machine stays testable.
    internal struct CampVector3
    {
        internal readonly float X;
        internal readonly float Y;
        internal readonly float Z;

        internal CampVector3(float x, float y, float z) { X = x; Y = y; Z = z; }

        internal static float Distance(CampVector3 a, CampVector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        internal string Describe()
        {
            return X.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                   Y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                   Z.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class CampParticipantObservation
    {
        internal int StableId = -1;
        internal string Name;
        internal bool LocalUsable;
        internal bool Remote;
        internal bool KnownDead;
    }

    // One deterministic read of native state. Every optional native fact is
    // nullable so "unknown" can never be confused with a real value.
    internal sealed class CampObservation
    {
        internal DateTime ObservedUtc;

        // True only when the reader completed without throwing. A failed read
        // must never be interpreted as "nothing is happening".
        internal bool ReadSucceeded;

        internal string Zone;                       // GameData.SceneName
        internal bool PartyPresent;                 // >=1 tracked party member
        internal List<string> PartyNames = new List<string>();
        internal List<CampParticipantObservation> Participants = new List<CampParticipantObservation>();
        internal int LocalResolvedMembers;          // trackings with a usable local SimPlayer
        internal int UnresolvedMembers;             // tracking present, avatar unusable
        internal int RemoteMembers;                 // COOP-owned entries
        internal CampAuthority Authority = CampAuthority.Unknown;

        // Native Role Manager (GroupTasks writes -> SimPlayerGrouping reads).
        internal RoleHolder MainTank = RoleHolder.Unknown;
        internal RoleHolder MainAssist = RoleHolder.Unknown;
        internal RoleHolder DesignatedMainAssist = RoleHolder.Unknown;
        internal RoleHolder Puller = RoleHolder.Unknown;
        internal List<string> CrowdControlNames = new List<string>();
        internal bool CrowdControlPlayer;
        internal bool CrowdControlKnown;
        internal List<string> HealerNames = new List<string>();
        internal bool HealersKnown;

        // Native pull state.
        internal bool? AutoPullEnabled;             // SimPlayerGrouping.PullConstant
        internal bool? GroupPullModeEngaged;        // SimPlayerGrouping.isPulling (gamepad latch, not Auto Pull)
        internal string ForcePullTargetName;        // SimPlayerGrouping.ForcePullTarget
        internal string CurrentPullTargetName;      // current Sim puller's SimPlayer.PullTarget, when readable
        internal int? MaxPullLevelAbove;            // PullerRangeHigh
        internal int? MaxPullLevelBelow;            // PullerRangeLow
        internal int? MaxPullDistance;              // MaxPullDist
        internal float? HoldManaFraction;           // ManaNeededForPull (0..1)

        // Derived from verified native reads, not a guessed game flag.
        // Null when the Puller is unknown/player-held or the Sim pull state is
        // unreadable. True means the assigned Sim Puller's CurrentPullPhase is
        // not NotPulling.
        internal bool? PullerActivelyPulling;

        // Recomputed exact per-healer gate from SimPlayer.CheckPullReadiness:
        // CurrentMana < GetCurrentMaxMana() * ManaNeededForPull. This is NOT a
        // group average and does not include a player healer. Null means the
        // gate could not be established from every relevant local healer.
        internal bool? HoldingForMana;

        // Native Guard/Stay anchor.
        internal bool? GuardActive;                 // every resolved local party Sim is guarding
        internal CampVector3? GuardAnchor;          // centroid of SimPlayer.GetGuardPos()
        internal float GuardAnchorSpread;           // worst member distance from the centroid
        internal float GuardAnchorTolerance;        // derived from native SpreadMagnitude
        internal List<string> GuardedNames = new List<string>();

        internal CampVector3? PlayerPosition;
        internal bool? InCombat;                    // GameData.InCombat
        internal bool? RaidActive;                  // GameData.RaidActive
        internal bool GameplayReady;                // SuiteUiPolicy readiness, sampled with this observation
        internal bool? PvpActive;                   // optional contract; null means installed state unreadable
        internal bool? DuelActive;                  // optional contract; null means installed state unreadable
        internal bool MeaningfulGameplayActivity;   // verified interaction signal when one is available
        internal string MeaningfulGameplayReason;   // privacy-safe current signal token; never target/chat text

        internal bool HasParty { get { return PartyPresent && PartyNames.Count > 0; } }
    }

    internal enum CampEventType
    {
        CampStarted,
        CampEnded,
        CampSuspended,
        CampResumed,
        CampPartyChanged,
        CampPullStarted,
        CampEncounterStarted,
        CampEncounterCompleted,
        CampRecoveryStarted,
        CampRecoveryEnded,
        CampRepeatedEnemy,
        CampRoughEncounter,
        CampRoleSnapshot
    }

    internal sealed class CampEvent
    {
        internal long Sequence;
        internal string EventId;
        internal DateTime Utc;
        internal string SessionId;
        internal CampEventType Type;
        internal string Zone;
        internal string Detail;
        internal List<string> PartyNames = new List<string>();
        // Additive Phase-3 payload for optional consumers such as Deep Sims.
        // Values are verified/derived primitives only. Keys are flattened by
        // CampmasterApi as verified.<key>.
        internal Dictionary<string, string> VerifiedFields = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // Immutable-by-convention snapshot handed to consumers (and to /camp status).
    internal sealed class CampSnapshot
    {
        internal const int SchemaVersion = 3;

        internal string SessionId;
        internal CampSessionState State = CampSessionState.Inactive;
        internal CampRecognitionSource Source = CampRecognitionSource.None;
        internal CampActivity Activity = CampActivity.Unknown;
        internal string Zone;
        internal DateTime? StartedUtc;
        internal double ElapsedSeconds;
        internal CampVector3? Anchor;
        internal string AnchorOrigin;               // "native guard" | "player position" | null
        internal List<string> Party = new List<string>();
        internal CampAuthority Authority = CampAuthority.Unknown;

        internal RoleHolder MainTank = RoleHolder.Unknown;
        internal RoleHolder MainAssist = RoleHolder.Unknown;
        internal RoleHolder DesignatedMainAssist = RoleHolder.Unknown;
        internal RoleHolder Puller = RoleHolder.Unknown;
        internal List<string> CrowdControl = new List<string>();
        internal bool CrowdControlKnown;
        internal List<string> Healers = new List<string>();
        internal bool HealersKnown;

        internal bool? AutoPullEnabled;
        internal bool? GroupPullModeEngaged;
        internal string ForcePullTargetName;
        internal string CurrentPullTargetName;
        internal int? MaxPullLevelAbove;
        internal int? MaxPullLevelBelow;
        internal int? MaxPullDistance;
        internal float? HoldManaFraction;
        internal bool? PullerActivelyPulling;
        internal bool? HoldingForMana;

        internal int CompletedEncounters;
        internal int VerifiedPulls;
        internal int RoughEncounters;
        internal int RepeatedEnemySignals;
        internal int PartyChanges;
        internal int Suspensions;

        internal bool IsActive
        {
            get { return State == CampSessionState.Active || State == CampSessionState.Suspended; }
        }
    }

    // Mod-side classification thresholds. These are Campmaster policy, NOT
    // Erenshor mechanics, and are labelled as such wherever they are shown.
    internal sealed class CampConfig
    {
        internal bool AutoRecognitionEnabled = true;
        internal double AutoStabilitySeconds = 8.0;
        internal float DepartureRadius = 45f;
        internal double DepartureGraceSeconds = 45.0;
        internal double PartyLossGraceSeconds = 20.0;
        internal double SignalLossGraceSeconds = 20.0;
        internal double EncounterQuietSeconds = 8.0;
        // Phase-3 deterministic social-seed thresholds. These classify
        // Campmaster context only; they are not claimed to be Erenshor rules.
        internal double RoughEncounterSeconds = 45.0;
        internal int RepeatedEnemyThreshold = 3;
        internal int MaxRetainedEvents = 256;
    }
}
