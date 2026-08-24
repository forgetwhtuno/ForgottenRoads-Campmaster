using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    // Pure living-camp data. These states are Campmaster-owned context only; they do not imply
    // native buffs, movement, animation, inventory consumption, equipment mutation, or skill use.
    internal enum CampLivingMode
    {
        None = 0,
        HuntCamp = 1,
        Relax = 2
    }

    internal enum CampLivingActivityType
    {
        Resting = 0,
        Eating = 1,
        EquipmentCare = 2,
        Training = 3,
        Watch = 4,
        Socializing = 5,
        PreparingTravel = 6,
        Quiet = 7
    }

    internal enum CampLivingActivityState
    {
        Active = 0,
        Completed = 1,
        Interrupted = 2
    }

    internal enum CampLivingEventType
    {
        SessionStarted = 0,
        SessionEnded = 1,
        ActivityStarted = 2,
        ActivityCompleted = 3,
        ActivityInterrupted = 4,
        CombatSuspended = 5,
        CombatResumed = 6,
        WatchEvent = 7,
        MinorDisagreement = 8,
        PreparationChanged = 9
    }

    internal enum CampPresentationCategory
    {
        Neutral = 0,
        Informational = 1,
        Warning = 2
    }

    internal sealed class CampLivingActivity
    {
        internal int StableId = -1;
        internal string ParticipantName;
        internal CampLivingActivityType Type;
        internal CampLivingActivityState State;
        internal DateTime StartedUtc;
        internal DateTime EndsUtc;
        internal string Outcome;
        internal int Ordinal;
    }

    internal sealed class CampLivingEvent
    {
        internal long Sequence;
        internal string EventId;
        internal DateTime Utc;
        internal string SessionId;
        internal CampLivingMode Mode;
        internal CampLivingEventType Type;
        internal int StableId = -1;
        internal string ParticipantName;
        internal string Detail;
        internal bool Meaningful;
        internal string Counterpart;
        internal string SubjectCategory;
        internal string SubjectSource;
        internal CampPresentationCategory PresentationCategory;
    }

    internal sealed class CampLivingSnapshot
    {
        internal string SessionId;
        internal CampLivingMode Mode;
        internal bool SuspendedForCombat;
        internal int Preparation;
        internal readonly List<CampLivingActivity> Activities = new List<CampLivingActivity>();
        internal readonly List<CampLivingEvent> RecentEvents = new List<CampLivingEvent>();
    }
}
