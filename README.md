# Loch Ness

A cross-platform 3D multiplayer game about hunting the Loch Ness Monster. Up to four
players crew a single survey launch: somebody drives, somebody works the sonar,
somebody watches the water. A server-authoritative monster listens for your pings and
tries not to be found.

Unity 6 · URP · C# · Netcode for GameObjects · Windows, macOS, Linux.

---

## Running it

```bash
git clone <this repo>
```

1. Open the folder in Unity Hub with **Unity 6 (6000.x)**. A different version gives a
   one-time upgrade prompt, which is safe to accept.
2. Let it import — it fetches URP and Netcode for GameObjects from the package manager.
3. **Assign a URP asset.** Right-click in the Project window → Create → Rendering →
   *URP Asset (with Universal Renderer)*, then Project Settings → Graphics → set it as
   the Default Render Pipeline. This is the one manual step, and it is unavoidable: a
   pipeline asset cannot be authored outside the Editor, and without one URP renders
   everything magenta.
4. Press **Play**.

Active Input Handling can stay on **Input System Package (New)**, which is the Unity 6
default. The game reads input through the Input System's device API and never touches
the legacy `Input` class.

### Dropping the scripts into an existing project

If you already have a Unity 6 URP project, copy **`Assets/Scripts/` only** — not
`ProjectSettings/`, not `Packages/manifest.json`, not the scene. Then install
`com.unity.netcode.gameobjects` from the Package Manager and press Play. The URP
template already provides the pipeline asset and the Input System.

Apart from the pipeline asset there is nothing to assemble: no scene to build, no
prefab to wire, no NavMesh to bake, no inspector reference to assign. **The game boots
from any scene**, including an empty one — see *Why everything is built in code* below.

### Playing together

- **Sail alone** — a solo hunt with an AI crewmate on the sonar.
- **Take on crew (host)** — listens on port 7777. Others join with your LAN address.
- **Join a boat** — enter the host's address.

Same-machine testing: run a build and press Play in the Editor, and point one at
`127.0.0.1`.

### Controls

| | |
|---|---|
| `W` `A` `S` `D` | Walk the deck |
| `Shift` | Move quickly |
| Mouse | Look |
| `E` | Take or leave the station you are standing at |
| `Space` | Ping (at the sonar) · log a sighting (at the bow watch) |
| `W`/`S`, `A`/`D` | Throttle and rudder (at the helm) |
| `Esc` | Free the cursor |

---

## The loop

You have eight minutes to log **three confirmed sightings**.

Sonar is the only tool that reaches into the water, and it is loud. Every ping tells
her roughly where you are, and how much it tells her depends on how close you are.
She has three states:

- **Wandering** — cruising the loch, moderately loud on sonar, and the only state in
  which she will surface.
- **Hiding** — down on the bed, slow, and returning very little. Alarmed but not
  panicked.
- **Fleeing** — a fast run away from whatever spooked her. Loud, and easy to track,
  which is the trade: pushing her hard makes her findable but puts distance between
  you.

Sightings come two ways. A **sonar confirm** needs a strong return inside 60 m — you
have to close the distance, which means more pings, which means more threat. A
**visual confirm** needs her actually surfaced, in front of you, within 85 m, and
somebody at the bow watch to call it. She only surfaces when she believes she is
alone. So the strongest play is also the least obvious one: stop pinging, cut the
engine, and wait.

That tension — the tool that finds her is the tool that scares her off — is the thing
this prototype exists to test.

---

## The plan this was built from

Written before the code, revised once when the design changed from "a boat each" to
"one boat, many crew".

1. **Project skeleton** — `ProjectSettings/`, a package manifest, one scene.
2. **Boot from code** so pressing Play in any scene starts the game.
3. **`NetworkPrefabForge`** — build and register NGO prefabs at runtime.
4. **The loch** — seeded terrain, animated water with real buoyancy sampling, fog.
5. **The vessel** — server-authoritative motion, crew parented in local space.
6. **Crew** — first-person movement on a moving deck, stations claimed with `E`.
7. **The monster** — the same state machine, swimming in 3D, network-hidden at range.
8. **AI crewmate**, then compile-check, documentation, commit.

### Decisions taken without being asked

Flagged here because they are the ones most worth arguing with.

| Decision | Reason |
|---|---|
| URP, not HDRP | Unity is retiring the built-in pipeline, so URP is the right target. HDRP is heavier than this needs and narrows platform support. Materials are set up by property name with `HasProperty` guards, so the code still runs under the built-in pipeline if it has to — see `MeshKit`. |
| World-space uGUI name tags, not `TextMesh` | `TextMesh` draws through the built-in `GUI/Text Shader`, which URP does not ship, so every name tag would render magenta. |
| No NavMesh | A NavMesh is baked in world space and **the deck moves**, so agents cannot path on it. The monster also swims in open 3D, which a NavMesh does not describe. Both use steering. |
| Input System device API, not an `.inputactions` asset | `Keyboard.current` / `Mouse.current` need no authored asset, so the project stays clone-and-play while staying on the supported input path. The cost is no rebinding and no gamepad; both call for an actions asset, and every call site already goes through `Boot/Controls`. |
| Nobody can fall off the boat | Removes a whole family of moving-platform bugs for no real loss — the launch is railed all round. |
| Contacts are shown to the whole crew | The scope is physical hardware in the wheelhouse. Crew pressure comes from the stations, not from hoarding the display. |

### Still open

Four questions whose answers would change the architecture. Defaults are in place;
say the word and they move.

1. **Internet play, or LAN only?** Currently direct IP. Relay would change the join
   flow to a join code and make connection asynchronous, which touches `GameManager`.
2. **Crew size and session length?** Tuned for 2–4 players over eight minutes.
3. **Should the AI crewmate exist in multiplayer?** Currently solo only.
4. **Where is the anti-cheat bar?** Crew movement is owner-authoritative. Everything
   that matters — the boat, the monster, the sonar, the score — is not.

---

## How it is put together

```
Assets/Scripts/
  Sim/          Engine-free. No UnityEngine reference anywhere in this folder.
    Vec3, SimMath      vectors, deterministic noise, stable hashing
    SonarModel         ping resolution, falloff, clutter, uncertainty
    QuarryBrain        threat model and the three-state machine
    HullModel          displacement-boat handling
  Boot/
    Bootstrapper       entry point; builds the world, netcode, prefabs and UI
    NetworkPrefabForge registers runtime-built prefabs with NGO
    MeshKit            procedural geometry and materials
  World/               terrain, animated water, atmosphere
  Vessel/              hull motion, stations, sonar set, deck bounds
  Player/              first-person crew controller and body
  AI/                  the monster, and the crewmate who sails with you
  Core/                session lifecycle, replicated match state, settings
  UI/                  title screen, HUD, PPI scope
  Net/                 owner-authoritative transform
```

### Why everything is built in code

There are no `.prefab` files, no authored materials, no Canvas laid out in the Editor,
and the one scene file is empty.

That is a response to a specific constraint: this was written without access to a
Unity Editor. Prefabs and scenes are YAML full of GUID references into package
internals; authored blind they fail to import, and they fail *silently* — a missing
script, an empty slot, a magenta mesh. C# can be compiled and type-checked without an
Editor (`tools/compilecheck`), so the game is C#.

The trade is real and worth stating: **nothing here is artist-editable yet.** Changing
the boat's proportions means editing `BoatBuilder`, not dragging a handle. The path
out is to open the project once, drag the runtime-built hierarchies into the Project
window as real prefabs, and delete the builders — the layout survives, and after that
an artist can work normally.

### The portable layer

`Assets/Scripts/Sim/` contains no `UnityEngine` reference at all. It holds the sonar
model, the threat model, the state machine and the hull dynamics — the parts you will
spend the most time tuning, and the parts hardest to get right.

Two benefits. It is unit-testable without an engine. And if this is ever rebuilt in
Unreal, that folder is a mechanical translation rather than a redesign — which is the
honest version of "can we port this later?" The rest of the code cannot be ported;
C#-to-C++ and NGO-to-Unreal-replication are rewrites. What survives an engine change
is design knowledge, art, and this folder.

### Networking

| Concern | Approach |
|---|---|
| Monster authority | Server only. Clients receive a transform and nothing else. |
| Monster position leaking | `NetworkHide` beyond 165 m, re-shown inside it, with a 40 m hysteresis band so a boat loitering at the boundary does not cause spawn churn. |
| Sonar | Resolved on the server. The client asks for a ping and is told what came back — never where she is. |
| Vessel authority | Server-simulated. The helmsman sends input, not position: everyone is standing on this object. |
| Crew on a moving deck | Parented to the boat's `NetworkObject`, replicating in **local space**. Standing still costs nothing to send and never jitters. |
| Crew authority | Owner-authoritative, bounded by `DeckBounds` on every peer — the worst a modified client achieves is standing somewhere silly on its own screen. |
| Match clock | A replicated *deadline* in server time, not a ticking float. Late joiners get the right number with no correction. |
| Station contention | Two players pressing `E` on the helm in one frame is a race the server resolves. |
| Disconnects | A helmsman who drops out vacates the helm, or the boat is locked for the rest of the hunt. |
| Solo play | A host bound to loopback. There is no offline code path, so solo and multiplayer are the same code. |
| Build mismatch | Rejected at connection approval with a readable reason. |

**The known hole.** NGO cannot hide an object from the host's own client, because that
client *is* the server. A cheating host can see the monster. A client-side render gate
hides her visually, which stops a casual host and not a determined one. The real fix
is a dedicated server build.

---

## Verification

```bash
bash tools/compilecheck/run.sh
```

Compiles every script against signature-only Unity and NGO stubs, with no Unity
install. It catches typos, wrong overloads, broken overrides and namespace mistakes.

**It does not prove the game works.** It cannot see NGO's IL post-processing, the
scene, rendering, or any runtime behaviour. Treat it as a spellchecker.

Defects found and fixed during self-review, recorded because they are the interesting
part:

1. `GameManager.Fail()` left NetworkManager callbacks subscribed. A failed host start
   followed by a retry would have spawned two boats, two monsters and two MatchStates.
2. The AI crewmate claimed the sonar and never released it, so a solo player could
   never work the set — contradicting the design the file itself documented.
3. The interaction prompt read "occupied" for the AI-held sonar even though the server
   would have accepted the claim.
4. Head pitch and hull speed/heading were written to NetworkVariables every frame;
   assignment dirties them, so all three were resending at full tick rate.
5. Five BoxColliders on the boat that nothing ever queried — crew movement is
   analytic — implying a physics model the code does not use.
6. The cursor stayed locked when the hunt ended, so the results screen's button
   could not be clicked. Freeing it from the UI alone would not have been enough:
   the crew controller keeps its own lock flag and would have gone on swinging the
   view while the player tried to click.

Earlier passes, before the rewrite to a shared boat, also caught a byte-versus-
character truncation bug in `FixedString32Bytes` that crashed on any accented name,
and a results panel that could show the previous hunt's outcome because NGO fires
`OnValueChanged` during its read loop. Both fixes survive in `CrewController.Truncate`
and `GameUI.PopulateResults`.

---

## What this is not

- **Not tuned.** Every threshold is a starting value. `pingAggression` at 0.55 means
  roughly two close pings tip her into fleeing; `threatDecayPerSecond` at 0.16 means
  about six seconds to shed a full threat bar. Both are worth arguing with.
- **Not audio.** There is no sound. A sonar game without a ping you can hear is
  missing a limb; it is the first thing to add.
- **Not artist-ready.** See *Why everything is built in code*.
- **Not run.** Written without a Unity Editor. It compiles and the architecture is
  sound, but first-open fixes are likely, not unexpected.

`prototype/index.html` is a 2D browser sandbox for tuning the sonar and threat models
in isolation — same constants, same maths, no engine. It is a tuning tool, not the
game.
