# Forgotten Roads Campmaster 0.4.0 — living-camp live-test checklist

## Automated/source tests

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\RUN_DETERMINISTIC_TESTS.ps1
```

The runner covers existing Hunt Camp/Relax behavior plus living-camp selection, local participant admission, remote/dead/unresolved exclusion, combat interruption/resume, native Hunt pull priority, participant removal, cancel, scene/session replacement, repeated sessions, event bounds, schema compatibility, pure-core isolation, retained UI, and optional-dependency source contracts.

## Native build

- [ ] Deterministic runner reports all PASS.
- [ ] `BUILD_AND_INSTALL.ps1` compiles against current installed Erenshor/Lunaris assemblies.
- [ ] Only `ErenshorCampmaster.dll` is installed.
- [ ] Lunaris loads version `0.4.0` and `CampmasterApi.SchemaVersion` remains `3`.

## Relax living-camp loop

- [ ] Form a normal local Sim party and use `/relax here` or the retained **Relax Here** control.
- [ ] Each eligible local Sim receives one visible activity in the retained panel.
- [ ] Exactly one participant is normally on Watch when at least one participant is eligible.
- [ ] Relax can show Resting, Eating, Equipment Care, Training, Socializing, Travel Preparation, or Quiet activity as deterministic context.
- [ ] Activities rotate only on their bounded completion cadence; there is no per-frame reroll.
- [ ] Preparation changes are labeled Campmaster context and do not change native stats/buffs/items.
- [ ] Occasional Watch/Disagreement events appear deterministically and routine activity does not spam chat.

## Hunt Camp living loop

- [ ] Start/recognize a valid Hunt Camp and confirm participant living activities appear without changing the existing Hunt Camp state machine.
- [ ] When the native Sim Puller begins a verified pull, contextual living activities clear immediately.
- [ ] Once the pull is over and the party is again camp-idle, fresh contextual activities can resume.
- [ ] Existing pull/recovery/encounter/role events still work.

## Combat priority / interruption

- [ ] Enter verified combat during Relax: all activity ownership clears immediately and status reads suspended for combat.
- [ ] Enter verified combat during Hunt Camp: same behavior; native combat remains authoritative.
- [ ] After combat is positively clear, activities resume with fresh assignments; old stale ownership does not return.
- [ ] If native party state becomes unreadable during a camp, living ownership is released rather than guessed through unknown state.

## Participant/lifecycle cleanup

- [ ] Remote COOP humans/networked Sims are excluded.
- [ ] A positively dead local Sim is excluded.
- [ ] A missing/unresolved avatar is excluded rather than guessed alive.
- [ ] Removing a party member releases only that participant's activity ownership.
- [ ] `/camp clear`, **End Hunt Camp**, `/relax off`, and **End Relax** clear the appropriate session/context.
- [ ] Zone transition ends/replaces session ownership with no stuck activity.
- [ ] Repeated Camp/Relax sessions begin from clean runtime state.
- [ ] Lunaris plugin disable/hot unload leaves no activity ownership, UI duplication, Harmony patch, or COOP assembly-load subscription behind.

## No hidden gameplay automation

- [ ] No living-camp path moves or teleports a Sim.
- [ ] No Guard/Stay, attack, heal, pull, target, animation, food/item, equipment, skill, buff, loot, quest, or save API is called by the living tracker.
- [ ] Visually inspect party behavior: Erenshor's native AI continues normally; Campmaster's activity labels are context/presentation only.

## Optional integrations

With Deep Sims absent:
- [ ] Hunt Camp, Relax, living activities/events, retained panel, and chat notices all work.

With current Deep Sims installed:
- [ ] Existing schema-3 Hunt Camp events still reach its current bridge.
- [ ] Existing Relax state still enters/leaves Deep Sims downtime context.
- [ ] No load-order or hard-reference error occurs.
- [ ] The new living event stream exists for a future Deep Sims consumer but does not require that consumer today.

With Journal absent:
- [ ] No error/spam; living event UI/API continues.

With Journal installed:
- [ ] Only notable Watch/Minor Disagreement events are eligible for Chronicle.
- [ ] Routine activity starts/completions/preparation do not flood Chronicle.

## API inspection

- [ ] `CampmasterApi.GetCurrentSnapshot()` retains all existing schema-3 keys and adds optional `living*` fields only when available.
- [ ] `GetLivingEventsAfter(0)` returns bounded primitive dictionaries with sequence/event ID/mode/type/participant/detail.
- [ ] Unknown/missing optional data fails closed rather than fabricating actor/game state.
