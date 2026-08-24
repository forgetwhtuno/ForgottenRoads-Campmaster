using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    // Phase 4 explicit social downtime. This model is deliberately separate
    // from Hunt Camp: sitting/recovery during a hunt must never imply Relax.
    internal enum RelaxSessionState
    {
        Inactive = 0,
        Active = 1,
        SuspendedForCombat = 2
    }

    internal enum RelaxEventType
    {
        RelaxStarted,
        RelaxSuspended,
        RelaxResumed,
        RelaxEnded
    }

    internal enum RelaxRecognitionSource
    {
        Explicit = 0,
        Automatic = 1
    }

    internal sealed class RelaxConfig
    {
        // Classification thresholds owned by Campmaster, not native mechanics.
        internal float DepartureRadius = 35f;
        internal double DepartureGraceSeconds = 15.0;
        internal double PartyLossGraceSeconds = 12.0;
    }

    internal sealed class RelaxEvent
    {
        internal long Sequence;
        internal string EventId;
        internal DateTime Utc;
        internal string SessionId;
        internal RelaxEventType Type;
        internal string Zone;
        internal string Detail;
        internal RelaxRecognitionSource RecognitionSource;
        internal List<string> PartyNames = new List<string>();
    }

    internal sealed class RelaxSnapshot
    {
        internal const int SchemaVersion = 1;

        internal string SessionId;
        internal RelaxSessionState State = RelaxSessionState.Inactive;
        internal string Zone;
        internal DateTime? StartedUtc;
        internal double ElapsedSeconds;
        internal CampVector3? Anchor;
        internal List<string> Party = new List<string>();
        internal CampAuthority Authority = CampAuthority.Unknown;
        internal RelaxRecognitionSource RecognitionSource = RelaxRecognitionSource.Explicit;

        internal bool IsActive
        {
            get { return State == RelaxSessionState.Active || State == RelaxSessionState.SuspendedForCombat; }
        }
    }
}
