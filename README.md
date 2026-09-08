# Loch Ness — Multiplayer Vertical Slice

A cross-platform (Windows / macOS / Linux) 3D multiplayer game about hunting the Loch Ness Monster.
Players crew research launches and sweep the water with active sonar; Nessie is a server-owned AI
that wanders, hides and flees in response to being detected.

This repository is the **code and architecture layer** of the vertical slice: the five requested
systems plus the two small enablers they need, written to be dropped into a Unity project that is
assembled from [`docs/EDITOR_SETUP.md`](docs/EDITOR_SETUP.md).

> **What is here and what is not.** Every C# script is complete, reviewed and compiles cleanly
> (see [Verification](#verification)). Scenes, prefabs, meshes and audio are *not* here — they are
> binary Unity assets that cannot be authored as text. The setup guide reconstructs them step by
> step, and every serialized field named there matches a real field in the scripts.

---

## Table of contents

1. [The core loop](#the-core-loop)
2. [The plan](#the-plan)
3. [Assumptions and open questions](#assumptions-and-open-questions)
4. [Architecture: the five load-bearing decisions](#architecture-the-five-load-bearing-decisions)
5. [Networking edge cases](#networking-edge-cases)
6. [Script map](#script-map)
7. [Editor setup](#editor-setup)
8. [Verification](#verification)
9. [Known limitations and next steps](#known-limitations-and-next-steps)

---

## The core loop

```
        ┌─────────────────────────────────────────────────────┐
        │                                                     │
        ▼                                                     │
   Player pings sonar  ──►  Server resolves the sweep  ──►  Contacts returned
   (loud, on a cooldown)    (clarity from range +          to that client only
                             Nessie's current state)       (bearing / range / confidence)
        │                            │                             │
        │                            ▼                             ▼
        │                   Nessie's threat rises          Player interprets the
        │                            │                     anomaly and closes in
        │                            ▼                             │
        │              Wandering → Hiding → Fleeing                │
        │              (quieter)     (louder)                      │
        └──────────────────────────────────────────────────────────┘

   Win: 3 confirmed contacts (high clarity, close range) before the 8-minute timer.
```

The tension is that **detection and evasion share one channel**. Active sonar is the only way to
find her and the only thing that tells her you are there. Pinging from far away is safe but useless;
pinging close enough to confirm a sighting is what sends her running.

Fleeing is deliberately her *loudest* state. Spooking her is not a failure — it is how you get a
clean track, if you can keep up.

---

## The plan

This is the plan as executed, in order.

| # | Step | Outcome |
|---|------|---------|
| 1 | **Fix the authority model before writing anything.** Decide who owns the monster, the boats and the sonar maths. | Server owns Nessie and all sonar resolution; owners own their own boat's transform. Everything else follows from this. |
| 2 | **Collapse single-player into the multiplayer path.** | Solo runs as a loopback host, so there is exactly one code path to write, debug and keep correct. |
| 3 | **Choose the scene topology.** | A persistent `Bootstrap` scene (NetworkManager, GameManager, UI) with the gameplay scene `Loch` loaded *additively* through `NetworkSceneManager`. Managers survive the whole session; late joiners synchronise for free. |
| 4 | **Close the information leak that a naive build would ship.** | Replicating Nessie's transform to everyone is a wallhack. Solved with distance-based network visibility plus server-resolved, polar, lossy sonar contacts. |
| 5 | **Build the session lifecycle** (`GameManager`) — start, approve, load, spawn, score, tear down — with every failure mode returning to the menu. | Timeouts, transport failure, version mismatch, host quit and cancel all land in the same teardown. |
| 6 | **Build the front end** (`MainMenuUI`) driven only by replicated phase + local session mode. | Solo and multiplayer transition identically because nothing in the UI branches on which one is running. |
| 7 | **Build the player** (`PlayerController`) — boat handling, free-look, and the server-authoritative sonar tool. | Responsive local movement, uncheatable sonar. |
| 8 | **Build the monster** (`NessieAI`) — threat model, three-state machine with hysteresis, NavMesh swimming, visibility management. | Readable behaviour that never flickers between states. |
| 9 | **Build the companion** (`AssistantAI`) — NavMesh station-keeping, autonomous sweeps, radio reports. | Restores the multi-bearing triangulation loop in solo play without playing the game for you. |
| 10 | **Verify.** Compile both input backends headlessly, audit every NetworkVariable write for authority, and trace both session flows end to end. | 14 defects found and fixed — [see below](#verification). |

---

## Assumptions and open questions

These were not specified. I picked a defensible default for each and flagged it here rather than
blocking; each is a one-line change if you want it the other way.

**Decisions I made**

| Question | Assumed | Change it by |
|---|---|---|
| Unity version / render pipeline | Unity 6 LTS (6000.x), URP | Scripts use no pipeline-specific API; 2022.3 LTS also works |
| First-person or boat? | **Both** — a boat hull with a free-look first-person camera at the helm | `PlayerController` look/hull sections |
| Transport | **Direct IP** (`UnityTransport`), LAN and port-forwarded play | Unity Relay + Lobby for internet play — needs a UGS project ID |
| Crew size | 4, enforced in connection approval | `GameManager.maxPlayers` |
| Win condition | 3 confirmed contacts inside 8 minutes | `targetSightings`, `huntDurationSeconds` |
| Can Nessie harm players? | **No** — she is purely evasive; there is no fail state but the clock | Add a `Lunging` state and a damage model |
| Dedicated server build? | No — listen-server (host is a player) | Affects the host-trust limitation below |
| Art / audio | Placeholder primitives; every hook is a serialized field | Assign real meshes and clips |

**Genuinely open — I need input**

- **Is internet (non-LAN) play in scope for the slice?** If yes, Relay integration should happen
  now rather than later: it changes the join UI from "type an IP" to "type a join code", and Relay
  allocation is asynchronous in a way that touches `GameManager`'s connect flow.
- **Target session length and crew size for tuning.** Sonar range, cooldown and Nessie's roam radius
  are all balanced against "4 boats, 8 minutes, ~300 m of water". A 16-player or 30-minute target
  needs different numbers, not different code.
- **Does the assistant exist in multiplayer?** It is written to work there (`spawnAssistantInMultiplayer`),
  but whether a crew of 4 should also get a bot is a design call.
- **Anti-cheat bar.** The slice validates everything that affects *scoring*, and trusts owners for
  their own position. Raising that to full server-authoritative movement is a real piece of work
  (prediction + reconciliation) and should be scheduled deliberately.

---

## Architecture: the five load-bearing decisions

### 1. Single-player is a loopback host, not an offline mode

Solo play calls `StartHost()` bound to `127.0.0.1`, with connection approval refusing every remote
client. There is no `if (offline)` anywhere in the codebase.

- Server-authority code runs constantly during solo playtests, so authority bugs surface on day one
  instead of ambushing the first multiplayer build.
- The Single Player and Host buttons differ by one method call.
- The socket is bound to loopback, so it is never reachable from the LAN.

The cost is a loopback socket in solo play. That is invisible to the player and is the cheapest
insurance available against the class of bug that only appears when a second machine connects.

### 2. The server owns the monster, absolutely

`NessieAI`'s state machine, threat model and `NavMeshAgent` execute only when `IsServer` is true.
Clients receive a replicated transform and a replicated `NessieState` for VFX and audio, and
simulate nothing.

The `NavMeshAgent` is **disabled on every client**. Leaving it enabled is the classic NGO bug: the
agent and `NetworkTransform` both write the transform and fight, producing a monster that stutters
and teleports everywhere except on the host.

### 3. Sonar is server-resolved, and its output is deliberately lossy

The client sends *intent* ("I pinged") and nothing else. The server decides what came back.

A contact is polar and imprecise — bearing, range, confidence, and a guess at what it is — never a
world position. Range error scales with uncertainty, so a weak contact is genuinely ambiguous rather
than "weak but pixel-perfect". Decoys (fish, wreckage, thermoclines) cap out below the confirmation
threshold: clutter can mislead you, but it can never hand the crew a false win.

Consequences: a modified client cannot forge a contact, cannot bypass the cooldown, cannot score a
sighting it did not earn, and a packet sniffer learns no more than the player does.

### 4. Nessie is network-*invisible* to distant clients

Server-resolved sonar is worthless if the monster's `NetworkTransform` is replicated to everyone —
a modified client would just read her position directly.

So `NessieAI` drives NGO's visibility system: she is only spawned on clients whose boat is inside
`visibilityRadius`, which is set comfortably wider than sonar range. Distant clients receive no
transform data at all. Because `CheckObjectVisibility` is only evaluated at spawn time, a periodic
server tick calls `NetworkShow`/`NetworkHide` as boats move, with asymmetric show/hide radii so a
boat idling on the boundary does not spawn and despawn her twice a second.

**The residual hole, stated plainly:** NGO cannot hide an object from the host's own client, because
that client *is* the server. A cheating host could therefore still see her. A client-side render
gate hides her meshes at the same distance, which handles an honest host; the real fix is a
dedicated server build, and host trust is unavoidable in any listen-server topology.

### 5. Owner-authoritative movement, with the server watching

Boats use `ClientNetworkTransform` (owner writes, everyone reads) so steering has zero latency. That
trusts the owner with its own position — an accepted trade for a slice, and *scoped*: it buys a
cheater a boat that moves oddly, not a sighting, because sonar is resolved independently on the
server. `PlayerController` runs a server-side plausibility check that flags impossible speeds, which
is also how you catch a broken build.

---

## Networking edge cases

Every row is handled in code, not just noted.

| Edge case | How it is handled | Where |
|---|---|---|
| **Host vs client authority over the monster** | Server-only simulation; agent disabled on clients | `NessieAI.OnNetworkSpawn` |
| Client reads Nessie's transform (wallhack) | Distance-based network visibility + server-resolved sonar | `NessieAI.UpdateNetworkVisibility` |
| Host can't be hidden from (host advantage) | Client-side render gate; documented residual | `NessieAI.UpdateClientRenderGate` |
| Visibility churn at the boundary | Asymmetric show/hide radii (hysteresis) | `NessieAI.UpdateNetworkVisibility` |
| Spawning into a scene the client hasn't loaded | `CreatePlayerObject = false`; spawn on `OnLoadEventCompleted` | `GameManager.EnsurePlayerSpawned` |
| Late joiners | Player spawned on `OnSynchronizeComplete`; NetworkVariables carry match state | `GameManager.HandleClientSynchronizeComplete` |
| Scene-placed NetworkVariables surviving a session | Explicitly reset on the server in `OnNetworkSpawn` | `GameManager`, `NessieAI`, `AssistantAI` |
| Several NetworkVariables dirty in one tick | Results UI re-renders on change, never snapshots in a sibling's callback | `MainMenuUI.PopulateResults` |
| Client binds scene callbacks too late | Bound immediately after `StartClient`, not from the connect callback | `GameManager.StartClient` |
| Host quits mid-hunt | No host migration; clients tear down with a readable reason | `GameManager.HandleClientDisconnected` |
| Connection never completes | Explicit client-side timeout | `GameManager.ClientConnectTimeout` |
| Transport dies (cable, adapter, socket) | `OnTransportFailure` → same teardown path | `GameManager.HandleTransportFailure` |
| Mismatched builds | Version string in the approval payload; rejected with a reason | `GameManager.ApproveConnection` |
| Session full | Rejected in approval with a reason | `GameManager.ApproveConnection` |
| Re-entrant / double shutdown | `_isTearingDown` guard; async shutdown awaited before scene unload | `GameManager.ShutdownRoutine` |
| Sonar RPC spam | Authoritative per-player cooldown on server time | `PlayerController.RequestSonarPingServerRpc` |
| Two AudioListeners / cameras | Disabled on non-owners; menu pair live only while this client has no boat | `PlayerController`, `MainMenuUI.UpdateFallbackView` |
| `FixedString32Bytes` overflow | Truncated by UTF-8 **byte** count, not character count | `PlayerController.TruncateForFixedString` |
| Missing `base.OnDestroy()` | Called in every `NetworkBehaviour` that overrides it | all four |
| NetworkVariable callback leaks | Unsubscribed in `OnNetworkDespawn` | all |
| Bandwidth waste from cosmetics | Hull bob/roll applied to a visual child on every peer, never networked | `PlayerController`, `AssistantAI` |
| Match timer replication | A single deadline on the synced server clock, not a per-tick float | `GameManager._phaseEndServerTime` |

---

## Script map

| File | Type | Runs on | Responsibility |
|---|---|---|---|
| `Assets/Scripts/Core/GameManager.cs` | `NetworkBehaviour` | all | Session lifecycle, approval, scene flow, spawning, match state, teardown |
| `Assets/Scripts/UI/MainMenuUI.cs` | `MonoBehaviour` | local | Title / multiplayer / settings / results panels, connect flow, hunt HUD |
| `Assets/Scripts/Player/PlayerController.cs` | `NetworkBehaviour` | all | Boat handling, free-look, sonar tool. Also hosts `SonarContact` + the shared `Sonar` resolver |
| `Assets/Scripts/AI/NessieAI.cs` | `NetworkBehaviour` | server sim | Threat model, Wandering/Hiding/Fleeing, NavMesh swimming, network visibility |
| `Assets/Scripts/AI/AssistantAI.cs` | `NetworkBehaviour` | server sim | Solo companion: station keeping, autonomous sweeps, radio reports |
| `Assets/Scripts/Networking/ClientNetworkTransform.cs` | `NetworkTransform` | all | Owner-authoritative transform replication (enabler, 1 line of logic) |
| `tools/compilecheck/` | — | CI / local | Headless compile of `Assets/Scripts` against API stubs, no Unity needed |

`GameSettings` (PlayerPrefs-backed user settings) lives inside `MainMenuUI.cs`, since the menu owns
its lifetime. `SonarContact` and the `Sonar` resolver live inside `PlayerController.cs` so the
player's tool and the companion's sensor are provably the same model — the companion can never
out-sense a human.

---

## Editor setup

Full step-by-step instructions — packages, scene hierarchies, prefab composition, NavMesh agent
types, NetworkManager configuration, UI wiring and a manual test matrix — are in
**[`docs/EDITOR_SETUP.md`](docs/EDITOR_SETUP.md)**.

`Packages/manifest.json` lists the dependencies; pin whatever the Package Manager resolves for your
editor version.

---

## Verification

### What was actually checked

1. **Headless compile, both input backends.** `Assets/Scripts` is compiled by Roslyn against
   signature-only stubs of UnityEngine / TextMeshPro / NGO. The input layer is `#if`-switched, so
   each pass only covers one half — both are built.

   ```
   ./tools/compilecheck/run.sh
   == pass 1/2: legacy Input Manager ==
   == pass 2/2: Input System package ==
   OK: both input backends compile.
   ```

   Zero errors, zero warnings from game code, with `CS0169`/`CS0414` (dead fields) deliberately
   left enabled.

2. **Authority audit.** Every `NetworkVariable` write was traced to its enclosing guard. All 22
   writes are inside an `IsServer` check, except `_displayName` — which is declared
   `NetworkVariableWritePermission.Owner` and written only under `IsOwner`. Correct.

3. **Flow trace.** Both session lifecycles walked callback by callback: boot → menu → start →
   approve → scene load → spawn → hunt → results → teardown → menu, for a loopback host, a listen
   host, a client joining at load, and a client joining late.

### Defects found and fixed

Found during this review, all fixed in the committed code:

| # | Defect | Impact |
|---|---|---|
| 1 | `FixedString32Bytes` was truncated by **character** count, but it is bounded in **bytes** | Any accented or CJK player name would throw at spawn and break the boat. Crash-class. |
| 2 | Clients bound scene callbacks from `OnClientConnectedCallback`, which NGO fires *after* synchronisation | Clients missed the initial `OnLoadComplete` and never made `Loch` the active scene — wrong lighting, runtime spawns in the wrong scene |
| 3 | Results panel snapshotted the outcome inside the phase callback | On clients, NGO applies same-tick NetworkVariables one at a time and fires each callback during that loop, so the panel could show the **previous** hunt's result |
| 4 | Nessie was spawned *before* player boats | Her spawn-time visibility check measures distance to each client's boat; with no boats yet she spawned hidden from everyone |
| 5 | Teardown skipped despawn cleanup when NGO had already stopped | Stale references after a host quit or transport failure |
| 6 | Nothing disabled the Bootstrap camera and AudioListener once a boat spawned | Two cameras rendering over each other and a per-frame duplicate-listener warning |
| 7 | A fresh sonar contact while already investigating did not repath the companion | It kept driving to a bearing several sweeps out of date |
| 8 | Companion flipped Following↔Sweeping at `stoppingDistance` | A NetworkVariable write several times a second, forever |
| 9 | `NavMeshAgent.destination` was read before checking `hasPath` | Meaningless value on the first regroup frame |
| 10 | `new NavMeshPath()` allocated on every repath | Steady GC pressure in both AI scripts |
| 11 | Dead fields (`_shutdownRoutine`, `_stateEnteredAt`) | Warnings; `_stateEnteredAt` became the fix for #8 |
| 12 | Player boat decelerated twice per frame when input was locked | Wrong deceleration rate on the menu |
| 13 | `ResolvePingOnServer` had no `IsServer` guard despite writing a server-authority variable | Latent throw if ever called from client code |
| 14 | `BindSceneCallbacks` had no idempotency guard and is reached from three call sites | Both a host and a client double-subscribed, so every scene event ran its handler twice |

### What this does *not* prove

Honest limits of the above:

- **NGO's IL post-processing is not exercised.** RPC codegen and NetworkVariable registration happen
  when Unity compiles the assembly. The stubs check signatures and types; they cannot confirm that
  `SonarContact[]` serialises the way the codegen expects. Open the project once to confirm.
- **No scene or prefab wiring is verified.** Serialized references, NavMesh bakes and Network Prefab
  registration are all editor state. The test matrix in the setup guide covers them manually.
- **No runtime behaviour is verified.** Nothing here has been played. Nessie's thresholds, sonar
  falloff and boat handling are considered starting values, not tuned ones.

---

## Known limitations and next steps

Ordered by what I would do next.

1. **Play it and tune it.** Every number in the AI and sonar headers is a starting value. The
   hide/flee thresholds and sonar falloff curve are what make or break the feel.
2. **Unity Relay + Lobby** if internet play is in scope. Direct IP only covers LAN and
   port-forwarded hosts.
3. **A real sonar HUD.** `PlayerController` already raises `OnLocalSonarContacts` /
   `OnLocalSonarCharge` / `OnLocalRadioMessage`; the current readout is a text dump so the slice is
   playable. A polar scope display is a pure presentation task against the existing events.
4. **Dedicated server build target.** Closes the host-advantage hole in decision 4 and removes host
   trust entirely.
5. **Server-authoritative movement with prediction** if the anti-cheat bar rises above "cannot forge
   score".
6. **Reconnect grace window.** A dropped player currently loses their boat immediately; a 30-second
   window to rejoin their own slot is a small addition to `HandleClientDisconnected`.
7. **Assembly definition** for `Assets/Scripts`. Left out on purpose: an `.asmdef` that references
   the Input System hard-fails compilation if that package is absent, and the input layer is
   deliberately optional.
