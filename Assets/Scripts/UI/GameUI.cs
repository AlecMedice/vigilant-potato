// -----------------------------------------------------------------------------
// GameUI — title screen and hunt HUD, built and driven from one place.
//
// THE RULE THIS FILE OBEYS
// Nothing here branches on single-player versus multiplayer. Which page is shown
// is a function of two things only: the replicated GamePhase, and whether a
// session is running at all. Because solo play is a host session (see
// GameManager), that single rule covers both modes — and it is why the UI cannot
// drift out of step between them, which is the classic failure of a menu that
// tracks its own idea of what the game is doing.
//
// The panels are assembled with UIKit at boot and then only shown and hidden.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using LochNess.Core;
using LochNess.Player;
using LochNess.Vessel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LochNess.UI
{
    public sealed class GameUI : MonoBehaviour
    {
        private enum Page { Title, Join, Settings, Hud, Results }

        // Menu pages
        private RectTransform _titlePage;
        private RectTransform _joinPage;
        private RectTransform _settingsPage;
        private RectTransform _hudPage;
        private RectTransform _resultsPage;

        // Widgets we keep hold of
        private Text _statusLine;
        private Text _clock;
        private Text _sightingsLabel;
        private Text _promptLabel;
        private Text _stationLabel;
        private Text _radioLog;
        private Text _helmReadout;
        private Text _resultsHeadline;
        private Text _resultsDetail;
        private Image _chargeFill;
        private InputField _addressField;

        private readonly Queue<string> _radioLines = new Queue<string>();
        private Camera _menuCamera;
        private AudioListener _menuListener;
        private SonarScope _scope;

        private void Awake()
        {
            GameSettings.Apply();

            BuildEventSystem();
            BuildMenuCamera();

            Canvas canvas = BuildCanvas();
            _titlePage = BuildTitlePage(canvas.transform);
            _joinPage = BuildJoinPage(canvas.transform);
            _settingsPage = BuildSettingsPage(canvas.transform);
            _hudPage = BuildHud(canvas.transform);
            _resultsPage = BuildResultsPage(canvas.transform);

            Show(Page.Title);
        }

        private void OnEnable()
        {
            GameManager.OnStatus += HandleStatus;
            GameManager.OnSessionModeChanged += HandleSessionMode;
            MatchState.OnPhaseChanged += HandlePhase;
            MatchState.OnSightingsChanged += HandleSightings;
            MatchState.OnRadio += HandleRadio;
            CrewController.OnPrompt += HandlePrompt;
            CrewController.OnStationChanged += HandleStation;
            SonarSet.OnCharge += HandleCharge;
        }

        private void OnDisable()
        {
            GameManager.OnStatus -= HandleStatus;
            GameManager.OnSessionModeChanged -= HandleSessionMode;
            MatchState.OnPhaseChanged -= HandlePhase;
            MatchState.OnSightingsChanged -= HandleSightings;
            MatchState.OnRadio -= HandleRadio;
            CrewController.OnPrompt -= HandlePrompt;
            CrewController.OnStationChanged -= HandleStation;
            SonarSet.OnCharge -= HandleCharge;
        }

        // ---------------------------------------------------------------------
        // Construction
        // ---------------------------------------------------------------------

        private void BuildEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("Event System");
            go.transform.SetParent(transform, false);
            go.AddComponent<EventSystem>();

            // InputSystemUIInputModule, not StandaloneInputModule: the latter reads the
            // legacy Input class and does nothing when that backend is switched off.
            // A module added in code has no actions assigned — the Editor's menu item
            // does that part — so ask it for the defaults, or no button ever clicks.
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private void BuildMenuCamera()
        {
            // Something has to render before a crew member exists, and something has to
            // hear. Both are switched off the moment the local crew spawns — two active
            // AudioListeners produce a warning every single frame.
            var go = new GameObject("Menu Camera");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 14f, -150f);
            go.transform.rotation = Quaternion.Euler(6f, 12f, 0f);

            _menuCamera = go.AddComponent<Camera>();
            _menuCamera.farClipPlane = 900f;
            _menuCamera.fieldOfView = 55f;
            _menuCamera.tag = "MainCamera";

            _menuListener = go.AddComponent<AudioListener>();
        }

        private Canvas BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private RectTransform BuildTitlePage(Transform parent)
        {
            RectTransform page = UIKit.FullScreen(parent, "Title", new Color(0.02f, 0.05f, 0.06f, 0.55f));

            UIKit.Label(page, "Title", "LOCH NESS", 96, UIKit.Paper, TextAnchor.LowerLeft,
                new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(120f, -300f), new Vector2(0f, -180f));
            UIKit.Label(page, "Subtitle", "A SURVEY OF THE DEEP WATER", 24, UIKit.Brass, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(124f, -350f), new Vector2(0f, -304f));

            float y = -430f;
            AddMenuButton(page, "Single", "SAIL ALONE", ref y).onClick.AddListener(() =>
            {
                GameManager.Instance?.StartSinglePlayer();
            });
            AddMenuButton(page, "Host", "TAKE ON CREW  (HOST)", ref y).onClick.AddListener(() =>
            {
                GameManager.Instance?.StartHost();
            });
            AddMenuButton(page, "Join", "JOIN A BOAT", ref y).onClick.AddListener(() => Show(Page.Join));
            AddMenuButton(page, "Settings", "SETTINGS", ref y).onClick.AddListener(() => Show(Page.Settings));
            AddMenuButton(page, "Quit", "QUIT", ref y).onClick.AddListener(Quit);

            _statusLine = UIKit.Label(page, "Status", "", 20, UIKit.Mist, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(124f, 48f), new Vector2(-120f, 84f));

            return page;
        }

        private Button AddMenuButton(Transform parent, string name, string caption, ref float y)
        {
            Button button = UIKit.Button(parent, name, caption,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(120f, y - 54f), new Vector2(460f, y));
            y -= 66f;
            return button;
        }

        private RectTransform BuildJoinPage(Transform parent)
        {
            RectTransform page = UIKit.FullScreen(parent, "Join", new Color(0.02f, 0.05f, 0.06f, 0.82f));

            UIKit.Label(page, "Heading", "JOIN A BOAT", 44, UIKit.Paper, TextAnchor.LowerLeft,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(120f, -240f), new Vector2(0f, -180f));
            UIKit.Label(page, "Hint", "Host's address on your network. Port 7777.", 19, UIKit.Mist,
                TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(124f, -282f), new Vector2(0f, -246f));

            _addressField = UIKit.Field(page, "Address", "127.0.0.1", "127.0.0.1",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -360f), new Vector2(560f, -306f));

            UIKit.Button(page, "Go", "CAST OFF",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -430f), new Vector2(340f, -376f))
                .onClick.AddListener(() => GameManager.Instance?.StartClient(_addressField.text));

            UIKit.Button(page, "Back", "BACK",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(360f, -430f), new Vector2(560f, -376f))
                .onClick.AddListener(() => Show(Page.Title));

            return page;
        }

        private RectTransform BuildSettingsPage(Transform parent)
        {
            RectTransform page = UIKit.FullScreen(parent, "Settings", new Color(0.02f, 0.05f, 0.06f, 0.88f));

            UIKit.Label(page, "Heading", "SETTINGS", 44, UIKit.Paper, TextAnchor.LowerLeft,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(120f, -240f), new Vector2(0f, -180f));

            UIKit.Label(page, "NameLabel", "NAME", 18, UIKit.Brass, TextAnchor.LowerLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -296f), new Vector2(400f, -272f));
            InputField name = UIKit.Field(page, "Name", "Skipper", GameSettings.DisplayName,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -350f), new Vector2(560f, -300f));
            name.characterLimit = GameSettings.MaxNameLength;
            name.onEndEdit.AddListener(value => GameSettings.DisplayName = value);

            UIKit.Label(page, "SensLabel", "MOUSE SENSITIVITY", 18, UIKit.Brass, TextAnchor.LowerLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -400f), new Vector2(400f, -376f));
            UIKit.Slider(page, "Sensitivity", 0.3f, 8f, GameSettings.MouseSensitivity,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -444f), new Vector2(560f, -410f))
                .onValueChanged.AddListener(value => GameSettings.MouseSensitivity = value);

            UIKit.Label(page, "VolLabel", "VOLUME", 18, UIKit.Brass, TextAnchor.LowerLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -494f), new Vector2(400f, -470f));
            UIKit.Slider(page, "Volume", 0f, 1f, GameSettings.MasterVolume,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -538f), new Vector2(560f, -504f))
                .onValueChanged.AddListener(value => GameSettings.MasterVolume = value);

            UIKit.Toggle(page, "Invert", "INVERT VERTICAL LOOK", GameSettings.InvertY,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -600f), new Vector2(560f, -560f))
                .onValueChanged.AddListener(value => GameSettings.InvertY = value);

            UIKit.Button(page, "Back", "BACK",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -690f), new Vector2(340f, -636f))
                .onClick.AddListener(() => Show(Page.Title));

            return page;
        }

        private RectTransform BuildHud(Transform parent)
        {
            RectTransform page = UIKit.FullScreen(parent, "HUD", new Color(0f, 0f, 0f, 0f));

            // ---- Top bar: the only two numbers that matter --------------------
            RectTransform bar = UIKit.Panel(page, "Top Bar", UIKit.Hull,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-260f, -64f), new Vector2(260f, -12f));

            _clock = UIKit.Label(bar, "Clock", "08:00", 30, UIKit.Paper, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, new Vector2(22f, 0f), new Vector2(-200f, 0f));
            _sightingsLabel = UIKit.Label(bar, "Sightings", "SIGHTINGS 0 / 3", 21, UIKit.Brass,
                TextAnchor.MiddleRight, Vector2.zero, Vector2.one, new Vector2(120f, 0f), new Vector2(-22f, 0f));

            // ---- Scope, bottom left ------------------------------------------
            RectTransform scopeFrame = UIKit.Panel(page, "Scope", UIKit.Hull,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(268f, 268f));

            var scopeGo = new GameObject("Scope Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var scopeRect = (RectTransform)scopeGo.transform;
            scopeRect.SetParent(scopeFrame, false);
            scopeRect.anchorMin = Vector2.zero;
            scopeRect.anchorMax = Vector2.one;
            scopeRect.offsetMin = new Vector2(10f, 30f);
            scopeRect.offsetMax = new Vector2(-10f, -10f);

            _scope = gameObject.AddComponent<SonarScope>();
            _scope.Attach(scopeGo.GetComponent<RawImage>());

            // Charge meter along the bottom of the scope housing.
            UIKit.Panel(scopeFrame, "Charge Track", UIKit.Abyss,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 10f), new Vector2(-10f, 18f));
            _chargeFill = UIKit.Panel(scopeFrame, "Charge Fill", UIKit.Phosphor,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 10f), new Vector2(-10f, 18f))
                .GetComponent<Image>();
            _chargeFill.type = Image.Type.Filled;
            _chargeFill.fillMethod = Image.FillMethod.Horizontal;
            _chargeFill.fillAmount = 1f;

            // ---- Helm readout, bottom right ----------------------------------
            RectTransform helm = UIKit.Panel(page, "Helm", UIKit.Hull,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-300f, 24f), new Vector2(-24f, 116f));
            _helmReadout = UIKit.Label(helm, "Readout", "", 20, UIKit.Mist, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-18f, 0f));

            // ---- Radio log, above the scope ----------------------------------
            _radioLog = UIKit.Label(page, "Radio", "", 18, UIKit.Mist, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0.55f, 0f), new Vector2(28f, 286f), new Vector2(0f, 430f));

            // ---- Centre: crosshair, prompt, station --------------------------
            UIKit.Label(page, "Reticle", "+", 26, new Color(0.85f, 0.88f, 0.86f, 0.5f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-20f, -20f), new Vector2(20f, 20f));

            _promptLabel = UIKit.Label(page, "Prompt", "", 22, UIKit.Paper, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420f, -120f), new Vector2(420f, -80f));

            _stationLabel = UIKit.Label(page, "Station", "", 20, UIKit.Brass, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-260f, -100f), new Vector2(260f, -70f));

            return page;
        }

        private RectTransform BuildResultsPage(Transform parent)
        {
            RectTransform page = UIKit.FullScreen(parent, "Results", new Color(0.02f, 0.05f, 0.06f, 0.9f));

            _resultsHeadline = UIKit.Label(page, "Headline", "", 64, UIKit.Paper, TextAnchor.LowerCenter,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 40f), new Vector2(0f, 130f));
            _resultsDetail = UIKit.Label(page, "Detail", "", 22, UIKit.Mist, TextAnchor.UpperCenter,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -40f), new Vector2(0f, 24f));

            UIKit.Button(page, "Return", "BACK TO THE SLIPWAY",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-180f, -160f), new Vector2(180f, -106f))
                .onClick.AddListener(() =>
                {
                    GameManager.Instance?.Leave();
                    Show(Page.Title);
                });

            return page;
        }

        // ---------------------------------------------------------------------
        // Page switching
        // ---------------------------------------------------------------------

        private void Show(Page page)
        {
            _titlePage.gameObject.SetActive(page == Page.Title);
            _joinPage.gameObject.SetActive(page == Page.Join);
            _settingsPage.gameObject.SetActive(page == Page.Settings);
            _hudPage.gameObject.SetActive(page == Page.Hud);
            _resultsPage.gameObject.SetActive(page == Page.Results);

            // The scope only ticks while it is on screen; it is the one part of the UI
            // that costs anything to run.
            if (_scope != null) _scope.enabled = page == Page.Hud;
        }

        private void HandleSessionMode(SessionMode mode)
        {
            if (mode == SessionMode.None)
            {
                _radioLines.Clear();
                if (_radioLog != null) _radioLog.text = string.Empty;
                Show(Page.Title);
            }
        }

        private void HandlePhase(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Hunting:
                    Show(Page.Hud);
                    break;

                case GamePhase.Results:
                    PopulateResults();
                    Show(Page.Results);
                    break;

                default:
                    Show(Page.Title);
                    break;
            }
        }

        private void PopulateResults()
        {
            MatchState match = MatchState.Instance;
            if (match == null) return;

            // Read from the replicated values at the moment of drawing rather than
            // caching them when the phase changed. On a client, NGO applies each dirty
            // NetworkVariable one at a time and fires OnValueChanged during that loop —
            // so the phase can arrive before the final sighting count does.
            bool won = match.Succeeded;
            _resultsHeadline.text = won ? "SHE'S REAL" : "NOTHING CONCLUSIVE";
            _resultsHeadline.color = won ? UIKit.Brass : UIKit.Mist;
            _resultsDetail.text = won
                ? $"{match.Sightings} confirmed sightings logged. The Society will want to see this."
                : $"{match.Sightings} of {match.TargetSightings} confirmed. Not enough to convince anybody.";
        }

        // ---------------------------------------------------------------------
        // Live readouts
        // ---------------------------------------------------------------------

        private void HandleStatus(string message)
        {
            if (_statusLine != null) _statusLine.text = message;
        }

        private void HandleSightings(int count, int target)
        {
            if (_sightingsLabel != null) _sightingsLabel.text = $"SIGHTINGS {count} / {target}";
        }

        private void HandleRadio(string line)
        {
            if (_radioLog == null || string.IsNullOrEmpty(line)) return;

            _radioLines.Enqueue(line);
            while (_radioLines.Count > 5) _radioLines.Dequeue();

            _radioLog.text = string.Join("\n", _radioLines.ToArray());
        }

        private void HandlePrompt(string prompt)
        {
            if (_promptLabel != null) _promptLabel.text = prompt ?? string.Empty;
        }

        private void HandleStation(StationRole? role)
        {
            if (_stationLabel == null) return;
            _stationLabel.text = role.HasValue ? $"— {role.Value.ToString().ToUpperInvariant()} —" : string.Empty;
        }

        private void HandleCharge(float charge)
        {
            if (_chargeFill == null) return;
            _chargeFill.fillAmount = charge;
            _chargeFill.color = charge >= 1f ? UIKit.Phosphor : UIKit.Rule;
        }

        private void Update()
        {
            UpdateFallbackView();

            MatchState match = MatchState.Instance;
            if (match == null || match.Phase != GamePhase.Hunting) return;

            if (_clock != null)
            {
                float remaining = match.RemainingSeconds;
                int minutes = Mathf.FloorToInt(remaining / 60f);
                int seconds = Mathf.FloorToInt(remaining % 60f);
                _clock.text = $"{minutes:00}:{seconds:00}";
                _clock.color = remaining < 60f ? UIKit.Alarm : UIKit.Paper;
            }

            if (_helmReadout != null)
            {
                BoatController boat = BoatController.Instance;
                if (boat != null)
                {
                    // Knots rather than metres per second: it is a boat.
                    float knots = boat.Speed * 1.94384f;
                    _helmReadout.text = $"HDG {Mathf.RoundToInt(boat.HeadingDegrees):000}°\nSPD {knots:0.0} kn";
                }
            }
        }

        /// <summary>
        /// Hand the camera and the audio listener over to the crew member once one
        /// exists, and take them back when it is gone. Two enabled AudioListeners log
        /// a warning every frame, and two enabled cameras render the world twice.
        /// </summary>
        private void UpdateFallbackView()
        {
            bool needed = CrewController.LocalCrew == null;
            if (_menuCamera != null && _menuCamera.enabled != needed) _menuCamera.enabled = needed;
            if (_menuListener != null && _menuListener.enabled != needed) _menuListener.enabled = needed;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
