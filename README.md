# Forgotten Roads: Campmaster 0.4.0

Part of the **Forgotten Roads for Erenshor** mod collection.

Campmaster observes native party/camp state and now adds a deterministic **living-camp** activity loop. Erenshor remains authoritative for party AI, roles, movement, combat, food/items, equipment, skills, buffs, and saves. Campmaster owns only bounded camp context, visible participant activities, and deterministic camp events.

## Commands

```text
/camp status                 show Hunt Camp/native observations
/camp setup                  explain missing/required Hunt Camp signals
/camp here                   explicitly declare a Hunt Camp
/camp clear                  clear Hunt Camp context
/camp auto on|off            enable/disable automatic Hunt Camp recognition
/camp selftest               run deterministic tests
/relax here                  begin explicit downtime at the current anchor
/relax off                   end Relax
/relax status                show Relax lifecycle state
```

The retained Campmaster panel also exposes **Hunt Camp Here**, **Relax Here**, **End Hunt Camp**, and **End Relax** controls plus current living-camp status.

## What visibly happens at camp

When either a Hunt Camp or explicit Relax session is active, Campmaster assigns each **verified local party Sim identity** one Campmaster-owned contextual activity. Examples include:

- standing watch;
- resting;
- eating (Relax only);
- tending equipment;
- short training (Relax only);
- socializing (Relax only);
- preparing for travel;
- keeping quiet.

The retained panel shows participant -> activity lines, a small `Preparation 0/12` context meter, and recent meaningful camp events. Activities complete and rotate on a deterministic 35–65 second cadence.

This is context, not hidden automation. Campmaster does **not** force sitting, animations, movement, Guard/Stay, food consumption, inventory changes, equipment repair, skill use, native buffs, or AI commands. `Preparation` is explicitly Campmaster context, not an Erenshor stat/buff.

## Deterministic camp events

Camp activity completion can produce bounded deterministic outcomes such as:

- a quiet watch;
- a watch member noticing signs of movement nearby;
- finishing equipment care/training/travel preparation;
- social downtime;
- an occasional minor disagreement with the group; when current Campmaster preparation state supports it, the bounded subject may be whether camp preparations are sufficient;
- increased Campmaster preparation context.

The system decides and records the event. An LLM is never asked whether it happened.

Only a small subset of meaningful events is echoed to chat; routine activity rotations stay in the retained panel/event stream rather than spamming social logs.

## Combat, pulling, and cleanup priority

Native gameplay always wins:

- verified combat immediately interrupts every living-camp activity and suspends assignment;
- after verified combat clears, fresh activities can be assigned again;
- in Hunt Camp, a verified native pull already in progress interrupts contextual camp routines even before combat starts;
- unreadable native party state releases activity ownership rather than simulating through unknown state;
- remote COOP actors, unresolved actors, and positively dead actors are never admitted;
- participant loss releases that participant's ownership;
- camp cancellation, session replacement/zoning, and plugin disable clear all runtime activity ownership.

No living-camp ownership is persisted across sessions.

## Hunt Camp

Automatic Hunt Camp recognition remains read-only. It requires the current proven local-party/Guard/role/Auto Pull/anchor signals and respects the existing stability, departure, party-loss, signal-loss, and encounter-quiet rules. `/camp here` remains an explicit declaration path.

Existing Hunt Camp context/events—pull lifecycle, encounter/recovery events, repeated verified pull-target seed, rough-combat seed, and actual native role snapshot—remain intact. The new living-camp layer is additive and does not change roles, Guard, Auto Pull, target selection, attacks, heals, movement, loot, equipment, or saves.

## Relax

Manual Relax remains available through `/relax here`. It is anchored, mutually exclusive with Hunt Camp, suspends for verified combat, resumes after combat, and ends on departure, party loss, zoning, or `/relax off`.

Campmaster can also recognize **automatic Relax** after about 60 seconds of verified safe stationary local-party downtime. This is social context only: it does not assign living activities, move actors, force sitting, change native AI, heal, buff, reward, or create camp objects. Automatic Relax exits immediately when combat, meaningful movement/travel, optional PvP/Duel activity, scene/readiness loss, or verified native pull activity resumes. Selecting `/relax here` while Auto Relax is active promotes the session to explicit/manual ownership.

Explicit Relax drives the deterministic living-camp participant/activity/event presentation described above. Automatic Relax deliberately does not; it remains bounded social context for optional consumers such as Deep Sims.

## Stable participant identity

Campmaster reads the current verified `SimPlayerTracking.simIndex` field once through cached reflection and pairs it with the current authoritative `GameData.GroupMembers` entries. Runtime activity ownership is keyed by that stable integer, not by display name.

A Sim must also be a live, local, in-group Erenshor Sim with `MyStats.Myself.Alive == true`. Remote COOP humans/networked Sims are excluded through the existing compatibility classifier.

## API compatibility

The existing public `CampmasterApi.SchemaVersion` remains **3** so current consumers, including the existing Deep Sims Campmaster bridge, do not break.

Existing APIs remain unchanged:

```text
CampmasterApi.IsHuntCampActive
CampmasterApi.IsRelaxActive
CampmasterApi.GetCurrentSnapshot()
CampmasterApi.GetEventsAfter(sequence)
CampmasterApi.GetRelaxEventsAfter(sequence)
```

The current snapshot now adds optional primitive fields:

```text
livingContractVersion = 1
livingMode
livingPreparation
livingSuspendedForCombat
livingActivities
```

And the additive event stream is:

```text
CampmasterApi.LivingLatestEventSequence
CampmasterApi.LivingOldestRetainedEventSequence
CampmasterApi.GetLivingEventsAfter(sequence)
```

Living event wire types include `camp_activity_started`, `camp_activity_completed`, `camp_activity_interrupted`, `camp_watch_event`, `camp_minor_disagreement`, combat suspend/resume, preparation changes, and living-session start/end.

## Deep Sims and Journal

Deep Sims is optional. The existing Deep Sims integration can already observe Campmaster's established Hunt Camp and Relax contracts; because schema 3 and those methods remain intact, it keeps working when the new living layer is present.

The `GetLivingEventsAfter(...)` stream is deliberately reflection-friendly. Deep Sims may use the exact deterministic watch/disagreement fact as a short conversation seed, but Campmaster still decides what happened and Deep Sims may not invent a subject or consequence.

Living-event chat colors are semantic and Campmaster-owned: watch/observation uses informational light blue, disagreement/tension uses warning yellow, and preparation/status uses neutral grey. Campmaster does not alter native or global chat styling.

Journal is optional. Through reflection, Campmaster offers only notable `WatchEvent` and `MinorDisagreement` events to the current Chronicle `AddChronicleEvent(...)` surface. Missing/unknown Journal versions are ignored. Routine camp activity is not written to Chronicle.

## Performance and persistence

Native state remains polled on the existing fixed 1-second cadence. The living tracker works only on that small party observation, uses no per-frame world scan/reflection lookup, keeps at most one activity per admitted party Sim, and retains at most 96 living events.

Living activity ownership, `Preparation`, and the event ring are session/runtime state and are not persisted. Existing Campmaster 0.4.0 also owns no save sidecar. No Erenshor save is edited.

## Build / test

Native Lunaris is required for this source line.

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\RUN_DETERMINISTIC_TESTS.ps1
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1
```

The deterministic runner covers Hunt Camp, Relax, and living-camp selection, admission, combat/native-pull interruption, cancellation, scene/session replacement, repeated sessions, and event bounds. Source contracts also guard schema 3, the additive living API, pure deterministic core, retained UI, and optional-integration boundaries.

`BUILD_AND_INSTALL.ps1` compiles against the currently installed Erenshor/Lunaris assemblies and installs only `ErenshorCampmaster.dll`.

## Optional Forgotten Roads Hub integration

Forgotten Roads Hub remains optional. Campmaster keeps the retained fallback panel and exposes the existing versioned control API. Hub/fallback controls can declare/clear Campmaster context and change Campmaster's own auto-recognition setting; they do not drive native gameplay.

---

This is an unofficial, community-made mod for Erenshor and is not affiliated with or endorsed by the game's developer.
