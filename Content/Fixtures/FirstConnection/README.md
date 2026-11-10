# FirstConnection fixtures (stage 4-4)

## Current state (2026-11-06, refactoring stage 4)

- The game (`Assets/MagicCampus/Core`) reads only `runner-bundle.json`, `staging-recipes.json` and the `start-*.json` start states. It does not read schemas, `full-catalog.json` or the manifests, and it does not validate content at run time. `Tests/Core` checks every shipped bundle instead.
- `start-*.json` are written by hand in the new save format. `build-fixtures.cjs` does not own them.
- Task 6-4 (2026-11-10) added `spells`, `reactions` and `magicTargets` to `generated/base/runner-bundle.json` and the start state `start-spells.json` (lantern, the quick-cast unlock spell and the combo spells already learned). Both were edited by hand in `generated/`, because the generator above is stale; the build copies `generated/` to `Assets/StreamingAssets/FirstConnection`. The rules are checked by `Tests/Core` (`MagicProblems`) and described in `design/systems/spellcasting.md`.
- `build-fixtures.cjs` is stale. It still writes the old `save-*.json`, `base/schemas/`, `base/full-catalog.json` and `variants/injections/`, which were deleted, and `manifest.json` and `repro-manifest.json` still list them. Do not run it without first updating it to the new layout.

The rest of this file describes the stage 4-4 generator as it was.

Real JSON fixtures for the first connected play path: two playable maps (courtyard, lounge), one active NPC, one reserved-staging relationship event. Everything under `generated/` is produced by `build-fixtures.cjs`; nothing is hand edited and nothing outside this folder is written.

## Run

```
node Content/Fixtures/FirstConnection/build-fixtures.cjs
node Content/Fixtures/FirstConnection/build-fixtures.cjs --check
```

Defaults: repo root is three levels above this folder, design root is `design/systems/full-design` (examples in `examples/`, schemas in `schemas/`). Override with `--root`, `--design`, `--out`, `--functions`, `--runner-schema`, `--catalog-schema`, `--save-schema`. Schemas are located by `$id` (runner is `urn:magic-campus:runner:2`). `--check` regenerates in memory and fails on any difference.

## Layout of generated/

- `base/` : byte copy of the full catalog, byte copies of functions and schemas (`base/schemas/`), derived runner bundle, three saves (offered, accepted, both accepted), staging recipes.
- `variants/dest-moved/` : dest.snack moved to lounge 4.5, 0.5. Valid runner; the default variant can reach both.
- `variants/a10-mira/` : second active NPC plus a cloned event with its own id, variable, flag, dialogue and reward.
- `variants/injections/` : runtime hook manifests (closed portal, blocked destination, map load failure, wrong arrival map). They leave logical definitions intact so the catalog stays valid.
- `variants/invalid/<name>/` : compile-negative runner bundles. They never replace the base bundle.
- `variants/unregistered-effect/` : valid bundle whose effects use an operation outside the minimal set; expected result is UNREGISTERED at commit.
- `repro-manifest.json` : A01-A10, runtime variants, compile negatives, expected exact states.
- `manifest.json` : source hashes, inactive definitions, SHA-256 inventory of every generated file.

## Base decisions

- Catalog bytes are copied unchanged. Its SHA-256 is written into every save as catalogHash; the manifest records whether it equals the hash in the original example save.
- Saves keep all 45 original fields, the integer wallet and all 12 NPC positions. Flags are set to the runner variable initials.
- Runner keeps all six logical maps and all original destinations (the strict catalog refers to them). region_1..4 and the other 11 NPCs are inactive.
- Active NPC is serin only. Her home is moved to dest.serin_home and her start position to the saved courtyard position, so schedule 420 home / 720 dest.notice / 1080 home does not put her at the meeting spot before the reservation is accepted.
- Auto clock event (lunch) and every unsupported domain event are removed from the base bundle.
- event.snack: reserved staging, once, cost 10, no completionKey (the completion key defaults to the event id), intro guard, resources for the NPC, both maps, input.player and camera.main, effects set memory.snack_done true / currency.add 20.
- event.snack_later is a distinct invitation from the same NPC with lower priority, used for the priority failure test.
- Appointments start Offered with acceptedAt 0; accepted saves use acceptedAt 420. The original closed save schema is required, and rows contain exactly id/status/acceptedAt.

## Staging recipe

For each reserved event the generator takes the first move, finds its destination map, picks the portal that leads into that map and uses its arrival point. Leased NPCs and the player are joined there while the transition is hidden; the move then walks from the arrival to the real destination. Current staging never warps.

## Injection hook kinds

`portalOpen`, `blockedCell`, `mapLoadFailure`, `actualPositionOverride`. Consumed only by test doubles and harnesses.

## Verification and limits

Codex reconciled the generated A01–A10 IDs with `design/implementation-tasks/인수기준.md`; the delegated additional cases remain under supplementalScenarios. The generator creates 29 deterministic files and its --check passes. Actual C# standard schema/compiler validation accepts the valid bundles and saves; the five negative bundles produce their exact expected codes and source pointers. Production schedules, reservation acceptance, full-save seal/restore and representative v1 migration pass against the real schema/reference validator. Production Runner/movement/effects/time/reservations/recovery/save-service integration passes with physical files and test display/map ports. Actual rendered game acceptance A01–A10 and process-kill/relaunch remain stage5 work, and are not marked passed by these fixtures.
