# Editor Setup

Everything needed to turn this repository into a running vertical slice. Follow it top to bottom;
each step assumes the previous one.

Estimated time from a fresh project: **45–60 minutes**, most of it building UI panels.

---

## 1. Project creation

| Setting | Value |
|---|---|
| Unity version | **6000.x LTS** (Unity 6). 2022.3 LTS also works — no version-specific API is used. |
| Template | **3D (URP)**. Built-in RP is fine too; nothing here is pipeline-specific. |
| Platform | Windows / macOS / Linux — the Standalone target covers all three. |

Copy `Assets/Scripts/` from this repository into your project's `Assets/` folder.

### Active Input Handling

`Edit ▸ Project Settings ▸ Player ▸ Active Input Handling` — **any of the three values works.**
`PlayerController` and `MainMenuUI` compile against both backends behind `#if ENABLE_INPUT_SYSTEM`.

- Choose **Input System Package (New)** or **Both** → the new-input branch compiles.
- Choose **Input Manager (Old)** → the legacy branch compiles, and you may skip the Input System
  package below.

> If you use the new Input System, the `EventSystem` needs an **Input System UI Input Module**
> instead of a Standalone Input Module, or the menu will not respond to clicks. Unity offers to
> replace it automatically when you select the EventSystem object.

---

## 2. Package Manager dependencies

`Window ▸ Package Manager ▸ Unity Registry`:

| Package | ID | Why |
|---|---|---|
| **Netcode for GameObjects** | `com.unity.netcode.gameobjects` | Mandated. Brings in Unity Transport transitively. |
| **AI Navigation** | `com.unity.ai.navigation` | `NavMeshSurface` / multiple Agent Types. In Unity 2022+ NavMesh *baking* lives here (`NavMeshAgent` itself is still core). |
| **Input System** | `com.unity.inputsystem` | Only if Active Input Handling includes "New". |
| **TextMeshPro** | bundled with `com.unity.ugui` | The UI uses `TMP_Text` / `TMP_InputField`. Run `Window ▸ TextMeshPro ▸ Import TMP Essential Resources` once. |

`Packages/manifest.json` in this repo lists these with versions that were current at time of
writing. Prefer installing through the Package Manager UI and letting it resolve versions for your
editor, then commit the resulting manifest.

---

## 3. Scenes

Create two scenes and add them to `File ▸ Build Settings ▸ Scenes In Build` **in this order**:

| Index | Scene | Role |
|---|---|---|
| **0** | `Bootstrap` | Persistent. NetworkManager, GameManager, UI. Never unloaded. |
| **1** | `Loch` | Gameplay. Loaded **additively** by `NetworkSceneManager`, unloaded on teardown. |

> Index 0 matters: `GameManager.UnloadGameplaySceneRoutine` re-activates scene 0 before unloading
> the loch, so Unity always has a valid active scene.

### 3a. `Bootstrap` hierarchy

```
Bootstrap
├── NetworkManager            [NetworkManager] [UnityTransport]
├── GameManager               [NetworkObject]  [GameManager]
├── MenuCamera                [Camera] [AudioListener]        ← fallback view before a boat exists
├── EventSystem               [EventSystem] [*InputModule]
└── UICanvas                  [Canvas: Screen Space – Overlay] [CanvasScaler] [GraphicRaycaster] [MainMenuUI]
    ├── MenuRoot              [CanvasGroup]                   → menuRoot
    │   ├── TitlePanel
    │   │   ├── TitleText, VersionText            (TMP)
    │   │   └── SinglePlayerBtn, MultiplayerBtn, SettingsBtn, QuitBtn
    │   ├── MultiplayerPanel
    │   │   ├── NameInput, AddressInput, PortInput  (TMP_InputField)
    │   │   └── HostBtn, JoinBtn, BackBtn
    │   ├── SettingsPanel
    │   │   ├── SensitivitySlider, VolumeSlider     (Slider)
    │   │   ├── InvertYToggle, FullscreenToggle     (Toggle)
    │   │   └── BackBtn
    │   ├── ConnectingPanel
    │   │   ├── ConnectingText  (TMP)
    │   │   └── CancelBtn
    │   ├── ResultsPanel
    │   │   ├── HeadlineText, DetailText  (TMP)
    │   │   └── ReturnBtn
    │   └── StatusText        (TMP)                 ← errors and disconnect reasons
    └── HuntHud               [CanvasGroup]        → huntHud
        ├── TimerText, SightingsText, SonarReadoutText, RadioText   (TMP)
        └── SonarChargeFill   [Image, Image Type = Filled]
```

Notes:

- `HuntHud` is a **separate** CanvasGroup from `MenuRoot` on purpose — the menu fades to alpha 0
  during the hunt while the HUD stays visible.
- Set `CanvasScaler ▸ UI Scale Mode = Scale With Screen Size`, reference resolution `1920×1080`,
  Match `0.5`. This is what makes the menu behave on a Steam Deck and on a 4K monitor.
- `SonarChargeFill` must have **Image Type = Filled** or `fillAmount` does nothing.
- Only `TitlePanel` needs to be active in the saved scene; `MainMenuUI` activates the rest on demand.

### 3b. `Loch` hierarchy

```
Loch
├── Environment
│   ├── WaterSurface    (plane at y = 0, MeshCollider)   [NavMeshSurface: Agent Type "Boat"]
│   ├── Lakebed         (terrain or mesh, ~y = −25)      [NavMeshSurface: Agent Type "Nessie"]
│   └── Shoreline       (island / bank colliders)        [NavMeshModifier: Not Walkable]
└── Lighting            (Directional Light, fog, skybox)
```

`Loch` contains **no cameras and no NetworkObjects**. Every gameplay object is spawned at runtime by
`GameManager`, which is what lets late joiners synchronise correctly.

---

## 4. NavMesh — two agent types

Nessie swims the lakebed; boats drive the surface. Those are different navigable surfaces at
different heights, so they need different Agent Types.

`Window ▸ AI ▸ Navigation ▸ Agents`, create two beyond the built-in Humanoid:

| Agent Type | Radius | Height | Step Height | Max Slope | Used by |
|---|---|---|---|---|---|
| **Boat** | 2.0 | 2.0 | 0.4 | 5° | Player boats' spawn sampling, `AssistantAI` |
| **Nessie** | 3.0 | 4.0 | 1.0 | 45° | `NessieAI` |

Then:

1. `WaterSurface` → **Add Component ▸ NavMeshSurface**, Agent Type **Boat**, Collect Objects
   **Children**. Press **Bake**.
2. `Lakebed` → **Add Component ▸ NavMeshSurface**, Agent Type **Nessie**, Collect Objects
   **Children**. Press **Bake**.
3. Islands and banks → **Add Component ▸ NavMeshModifier**, tick **Override Area**, Area **Not
   Walkable**, then re-bake the Boat surface so boats cannot drive onto land.

**Verify before moving on:** with the Navigation window open, both surfaces should show as coloured
overlays at different heights. If the Boat surface is missing, the companion will log
`Could not place the companion on the water NavMesh` at spawn — that message means this step, not a
code problem.

---

## 5. Prefabs

### 5a. `PlayerBoat`

```
PlayerBoat            [NetworkObject] [ClientNetworkTransform] [CharacterController] [PlayerController] [AudioSource]
├── Visual            (hull mesh — a stretched cube is fine)         → boatVisual
└── CameraPivot       (empty, at helm height ≈ y 1.6)                → cameraPivot
    └── PlayerCamera  [Camera] [AudioListener]                       → playerCamera, playerAudioListener
```

| Component | Setting |
|---|---|
| `CharacterController` | Radius `1.5`, Height `2`, Center `(0, 1, 0)` |
| `ClientNetworkTransform` | Syncing Position **X Y Z**, Rotation **Y** only, **Interpolate ✔** |
| `AudioSource` | Play On Awake **off**, Spatial Blend `1` (3D) |

`PlayerController` field wiring: `cameraPivot`, `playerCamera`, `playerAudioListener`, `boatVisual`,
`sonarAudioSource` (the root AudioSource), `sonarPingClip` (any ping clip). Tuning fields have
working defaults.

> **Rotation Y only** matters. Syncing all three axes replicates the cosmetic roll, which is applied
> locally on every peer anyway — you would be paying bandwidth for it twice and fighting yourself.

### 5b. `Nessie`

```
Nessie                [NetworkObject] [NetworkTransform] [NavMeshAgent] [NessieAI]
└── Visual            (creature mesh)                                → visualRoot
```

| Component | Setting |
|---|---|
| `NetworkTransform` | **Stock, server-authoritative** — *not* `ClientNetworkTransform`. Position **X Y Z**, Rotation **Y**, Interpolate ✔ |
| `NavMeshAgent` | Agent Type **Nessie**. Speed/Angular/Acceleration/Base Offset are all driven by script |
| `NessieAI` | `visualRoot` → the Visual child. Set `roamCenter` to the middle of the loch at roughly lakebed height |

> `visualRoot` must contain **only meshes**. The client-side render gate toggles `Renderer.enabled`
> beneath it; putting the NavMeshAgent or collider under there would be harmless today but is asking
> for trouble later.

### 5c. `AssistantBoat`

```
AssistantBoat         [NetworkObject] [NetworkTransform] [NavMeshAgent] [AssistantAI] [AudioSource]
└── Visual                                                            → visualRoot
```

Stock server-authoritative `NetworkTransform` (the server owns it). `NavMeshAgent` Agent Type
**Boat**. Wire `visualRoot`, `sonarAudioSource`, `sonarPingClip`.

---

## 6. NetworkManager configuration

Select `NetworkManager` in `Bootstrap`:

| Field | Value | Why |
|---|---|---|
| **Player Prefab** | `PlayerBoat` | Harmless: approval sets `CreatePlayerObject = false`, so NGO never auto-spawns. Assigning it silences a startup warning. |
| **Network Prefabs** | `PlayerBoat`, `Nessie`, `AssistantBoat` | Anything spawned at runtime must be registered, or `Spawn()` fails. |
| **Enable Scene Management** | ✔ | Required for additive network scene loading. `GameManager` also sets it at runtime. |
| **Connection Approval** | ✔ | Enforces the player cap, version gate and deferred player spawn. Also set at runtime. |
| **Tick Rate** | `30` | `60` if you prefer; higher costs bandwidth. |
| **Network Transport** | `UnityTransport` | Address and port are overwritten by `GameManager` per session. |

---

## 7. Wiring `GameManager`

Select the `GameManager` object in `Bootstrap`:

| Field | Value |
|---|---|
| `gameplaySceneName` | `Loch` — must match the scene name in Build Settings exactly |
| `playerPrefab` / `nessiePrefab` / `assistantPrefab` | the three prefabs from step 5 |
| `spawnAssistantInMultiplayer` | off (companion is single-player only) |
| `defaultPort` | `7777` |
| `maxPlayers` | `4` |
| `buildVersion` | any string; **clients with a different value are rejected** |
| `huntDurationSeconds` / `targetSightings` | `480` / `3` |
| `playerSpawnCenter` / `playerSpawnRadius` | a point on the water at `y = 0`, radius `10` |
| `nessieSpawnCenter` / `nessieSpawnRadius` | a point over the lakebed, radius `40` |

These spawn volumes are world-space coordinates **inside `Loch`**, configured on an object that
lives in `Bootstrap` — cross-scene object references are impossible, so coordinates it is. Open
`Loch`, note the positions you want, type them in.

---

## 8. Wiring `MainMenuUI`

On `UICanvas`, assign every serialized field to the object of the same name from the step-3a
hierarchy. All 37 are plain drag-and-drop:

- **Root** — `menuRoot`, `fadeDuration` (`0.18`)
- **Fallback View** — `menuCamera`, `menuAudioListener` (both on `MenuCamera`)
- **Panels** — `titlePanel`, `multiplayerPanel`, `settingsPanel`, `connectingPanel`, `resultsPanel`
- **Title** — `singlePlayerButton`, `multiplayerButton`, `settingsButton`, `quitButton`, `versionText`
- **Multiplayer** — `nameInput`, `addressInput`, `portInput`, `hostButton`, `joinButton`, `multiplayerBackButton`
- **Settings** — `sensitivitySlider`, `volumeSlider`, `invertYToggle`, `fullscreenToggle`, `settingsBackButton`
- **Connecting** — `connectingText`, `cancelConnectButton`
- **Results** — `resultsHeadlineText`, `resultsDetailText`, `resultsReturnButton`
- **Status** — `statusText`, `statusHoldSeconds` (`6`)
- **In-Hunt HUD** — `huntHud`, `huntTimerText`, `huntSightingsText`, `sonarReadoutText`, `radioText`, `sonarChargeFill`

Do **not** add `onClick` handlers in the inspector. `MainMenuUI.WireButtons` calls
`RemoveAllListeners()` and binds them in code, so inspector handlers would be silently discarded.
Slider ranges are also set in code and will overwrite whatever you type.

Every field is null-guarded, so you can wire the menu incrementally and test as you go.

---

## 9. Controls

| Input | Action |
|---|---|
| `W` / `S` | Throttle ahead / astern |
| `A` / `D` | Rudder (weak at low speed — boats steer with water flow) |
| Mouse | Free-look, independent of the hull, ±120° from the bow |
| `Space` or `LMB` | Sonar ping |
| `Esc` | Back out of a panel, or leave the hunt |

---

## 10. Test matrix

Run all seven before calling the slice good. Rows 4–7 need a build plus the editor, or two builds.

| # | Scenario | Expected |
|---|---|---|
| 1 | Press Play, click **Single Player** | Menu fades, HUD appears, boat is drivable, companion falls in astern |
| 2 | Ping sonar repeatedly | Readout lists contacts; charge bar refills; extra presses inside the cooldown do nothing |
| 3 | Press `Esc` mid-hunt | Clean return to the title screen; `Loch` is unloaded; no console errors |
| 4 | Build. Run build as **Host**; editor **Joins** `127.0.0.1` | Two boats visible; both drive smoothly; each sonar readout is independent |
| 5 | Change `buildVersion` on one side and join | Rejected with *"Version mismatch. Host is X, you are Y."* |
| 6 | Join a host that is not running | *"Could not reach the host (timed out)"* after ~10 s, back at the title |
| 7 | Host quits mid-hunt | Client returns to the title with a disconnect reason, not a hang |

### Fast feedback without opening Unity

`tools/compilecheck` compiles `Assets/Scripts` against signature-only API stubs, in about two
seconds, with no Unity install:

```bash
./tools/compilecheck/run.sh
```

It builds **both** input backends, because the input layer is `#if`-switched and one pass only
covers one half. Good for CI and for catching typos before a domain reload.

It cannot verify NGO's IL post-processing (RPC codegen, NetworkVariable registration), scenes,
prefabs or serialized references — open the project for those.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| `Could not place Nessie on the lakebed NavMesh` | Lakebed `NavMeshSurface` not baked, wrong Agent Type, or `nessieSpawnCenter` is not over it |
| `Could not place the companion on the water NavMesh` | Same, for the Boat surface on `WaterSurface` |
| Monster stutters or teleports on a client | A client-side `NavMeshAgent` is enabled — `NessieAI` disables it in `OnNetworkSpawn`; check the prefab has `NessieAI` attached at all |
| Two overlapping views, duplicate-AudioListener warnings | `menuCamera` / `menuAudioListener` not wired on `MainMenuUI` |
| Buttons do nothing | `EventSystem` missing, or the wrong input module for your Active Input Handling |
| `Spawn()` fails at runtime | Prefab not in **Network Prefabs** on the NetworkManager |
| Client joins but sees an empty loch | `Loch` missing from Build Settings, or its name does not match `gameplaySceneName` |
| Boat spawns but will not move | Not the owner (check `ClientNetworkTransform`, not the stock one), or phase is not `Hunting` |
