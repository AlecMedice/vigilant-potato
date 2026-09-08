// ---------------------------------------------------------------------------------------------
//  MainMenuUI.cs
//  Role : Title screen (Single Player / Multiplayer / Settings), connection flow, results screen,
//         and a minimal in-hunt HUD so the vertical slice is playable end to end.
//
//  UI SYSTEM CHOICE
//  --------------------------------------------------------------------------------------------
//  uGUI + TextMeshPro, not UI Toolkit. UI Toolkit is the more modern authoring stack, but it needs
//  a binary PanelSettings asset and UXML/USS files that cannot be reconstructed from a code listing
//  alone. uGUI can be rebuilt exactly from the written setup steps, which matters more for a slice
//  someone else has to stand up. "Modern" here is delivered through behaviour - panel navigation
//  with no scene reloads, cross-fades, full keyboard/gamepad focus handling, persisted settings.
//
//  THE TRANSITION RULE THAT MAKES SINGLE AND MULTIPLAYER FEEL IDENTICAL
//  --------------------------------------------------------------------------------------------
//  This class never asks "are we in single-player?" to decide what to show. It listens to exactly
//  two signals from GameManager - the replicated GamePhase and the local SessionMode - and both
//  behave identically in solo and multiplayer because solo is a loopback host. Single Player and
//  Host differ by one method call and nothing else.
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LochNess
{
    /// <summary>
    /// PlayerPrefs-backed user settings, shared by the menu (which edits them) and the
    /// PlayerController (which reads them). Kept in this file because the menu owns their
    /// lifetime; a larger project would promote it to its own service.
    /// </summary>
    public static class GameSettings
    {
        private const string KeyName = "lochness.playerName";
        private const string KeySensitivity = "lochness.mouseSensitivity";
        private const string KeyInvertY = "lochness.invertLookY";
        private const string KeyVolume = "lochness.masterVolume";
        private const string KeyFullscreen = "lochness.fullscreen";
        private const string KeyAddress = "lochness.lastAddress";
        private const string KeyPort = "lochness.lastPort";

        public static string PlayerName { get; set; } = "Skipper";
        public static float MouseSensitivity { get; set; } = 2f;
        public static bool InvertLookY { get; set; }
        public static float MasterVolume { get; set; } = 0.8f;
        public static bool Fullscreen { get; set; } = true;
        public static string LastAddress { get; set; } = "127.0.0.1";
        public static ushort LastPort { get; set; } = 7777;

        private static bool _loaded;

        /// <summary>Loads once per process. Safe to call from anywhere, any number of times.</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            PlayerName = PlayerPrefs.GetString(KeyName, PlayerName);
            MouseSensitivity = PlayerPrefs.GetFloat(KeySensitivity, MouseSensitivity);
            InvertLookY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            MasterVolume = PlayerPrefs.GetFloat(KeyVolume, MasterVolume);
            Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            LastAddress = PlayerPrefs.GetString(KeyAddress, LastAddress);
            LastPort = (ushort)Mathf.Clamp(PlayerPrefs.GetInt(KeyPort, LastPort), 1, 65535);

            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetString(KeyName, PlayerName);
            PlayerPrefs.SetFloat(KeySensitivity, MouseSensitivity);
            PlayerPrefs.SetInt(KeyInvertY, InvertLookY ? 1 : 0);
            PlayerPrefs.SetFloat(KeyVolume, MasterVolume);
            PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
            PlayerPrefs.SetString(KeyAddress, LastAddress);
            PlayerPrefs.SetInt(KeyPort, LastPort);
            PlayerPrefs.Save();

            Apply();
        }

        /// <summary>Pushes settings that affect the engine rather than gameplay scripts.</summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);

            // Guard against thrashing the display mode every frame a slider moves.
            if (Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
        }
    }

    /// <summary>Front-end for the whole session lifecycle.</summary>
    [DisallowMultipleComponent]
    public class MainMenuUI : MonoBehaviour
    {
        // -----------------------------------------------------------------------------------------
        //  Inspector - panels
        // -----------------------------------------------------------------------------------------

        [Header("Root")]
        [Tooltip("CanvasGroup wrapping the entire menu. Faded out during the hunt.")]
        [SerializeField] private CanvasGroup menuRoot;
        [SerializeField] private float fadeDuration = 0.18f;

        [Header("Fallback View")]
        [Tooltip("Bootstrap-scene camera that renders the title screen. Switched off once this " +
                 "client has a boat, so it never fights the boat's own camera.")]
        [SerializeField] private Camera menuCamera;
        [Tooltip("AudioListener on the menu camera. Unity allows exactly one active listener.")]
        [SerializeField] private AudioListener menuAudioListener;

        [Header("Panels")]
        [SerializeField] private GameObject titlePanel;
        [SerializeField] private GameObject multiplayerPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject connectingPanel;
        [SerializeField] private GameObject resultsPanel;

        [Header("Title Panel")]
        [SerializeField] private Button singlePlayerButton;
        [SerializeField] private Button multiplayerButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private TMP_Text versionText;

        [Header("Multiplayer Panel")]
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private TMP_InputField portInput;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button multiplayerBackButton;

        [Header("Settings Panel")]
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private Toggle invertYToggle;
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private Button settingsBackButton;

        [Header("Connecting Panel")]
        [SerializeField] private TMP_Text connectingText;
        [SerializeField] private Button cancelConnectButton;

        [Header("Results Panel")]
        [SerializeField] private TMP_Text resultsHeadlineText;
        [SerializeField] private TMP_Text resultsDetailText;
        [SerializeField] private Button resultsReturnButton;

        [Header("Status")]
        [Tooltip("Transient message line on the title screen (errors, disconnect reasons).")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private float statusHoldSeconds = 6f;

        [Header("In-Hunt HUD")]
        [Tooltip("Separate CanvasGroup so the HUD can be visible while the menu is faded out.")]
        [SerializeField] private CanvasGroup huntHud;
        [SerializeField] private TMP_Text huntTimerText;
        [SerializeField] private TMP_Text huntSightingsText;
        [SerializeField] private TMP_Text sonarReadoutText;
        [SerializeField] private TMP_Text radioText;
        [SerializeField] private Image sonarChargeFill;

        // -----------------------------------------------------------------------------------------
        //  Runtime
        // -----------------------------------------------------------------------------------------

        private GameObject _activePanel;
        private float _statusClearTime;
        private float _radioClearTime;
        private float _menuTargetAlpha = 1f;
        private bool _busy;                    // True while a connection attempt is in flight.
        private readonly StringBuilder _readoutBuilder = new StringBuilder(256);

        // Last outcome actually rendered onto the results panel. See PopulateResults for why the
        // panel is refreshed on change rather than snapshotted once.
        private bool _resultsRendered;
        private bool _renderedWin;
        private int _renderedSightings;

        // -----------------------------------------------------------------------------------------
        //  Lifecycle
        // -----------------------------------------------------------------------------------------

        private void Start()
        {
            // Start(), not Awake(): every Awake in Bootstrap has run by now, so GameManager.Instance
            // is guaranteed to be assigned regardless of script execution order.
            GameSettings.EnsureLoaded();

            WireButtons();
            WireSettingsWidgets();
            PopulateFromSettings();

            if (versionText != null) versionText.text = Application.version;

            var gm = GameManager.Instance;
            if (gm == null)
            {
                SetStatus("GameManager missing from the Bootstrap scene.");
                Debug.LogError("[MainMenuUI] No GameManager found. Check the Bootstrap scene setup.", this);
            }
            else
            {
                gm.OnPhaseChanged += HandlePhaseChanged;
                gm.OnSessionModeChanged += HandleSessionModeChanged;
                gm.OnStatusMessage += SetStatus;
                gm.OnSessionEnded += HandleSessionEnded;
                gm.OnSightingsChanged += HandleSightingsChanged;
            }

            // Local-player HUD feeds. These are static events, so they must be unsubscribed.
            PlayerController.OnLocalSonarContacts += HandleSonarContacts;
            PlayerController.OnLocalSonarCharge += HandleSonarCharge;
            PlayerController.OnLocalRadioMessage += HandleRadioMessage;

            ShowPanel(titlePanel);
            ApplyPhaseVisibility(gm != null ? gm.CurrentPhase : GamePhase.Menu);
        }

        private void OnDestroy()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnPhaseChanged -= HandlePhaseChanged;
                gm.OnSessionModeChanged -= HandleSessionModeChanged;
                gm.OnStatusMessage -= SetStatus;
                gm.OnSessionEnded -= HandleSessionEnded;
                gm.OnSightingsChanged -= HandleSightingsChanged;
            }

            PlayerController.OnLocalSonarContacts -= HandleSonarContacts;
            PlayerController.OnLocalSonarCharge -= HandleSonarCharge;
            PlayerController.OnLocalRadioMessage -= HandleRadioMessage;
        }

        private void WireButtons()
        {
            Bind(singlePlayerButton, OnSinglePlayerClicked);
            Bind(multiplayerButton, () => ShowPanel(multiplayerPanel, addressInput != null ? addressInput.gameObject : null));
            Bind(settingsButton, () => ShowPanel(settingsPanel, sensitivitySlider != null ? sensitivitySlider.gameObject : null));
            Bind(quitButton, () => GameManager.Instance?.QuitApplication());

            Bind(hostButton, OnHostClicked);
            Bind(joinButton, OnJoinClicked);
            Bind(multiplayerBackButton, () => ShowPanel(titlePanel));

            Bind(settingsBackButton, OnSettingsBack);
            Bind(cancelConnectButton, OnCancelConnect);
            Bind(resultsReturnButton, OnReturnToMenu);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void WireSettingsWidgets()
        {
            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = 0.25f;
                sensitivitySlider.maxValue = 8f;
                sensitivitySlider.onValueChanged.AddListener(v => GameSettings.MouseSensitivity = v);
            }

            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
                // Applied live so the player hears the change while dragging.
                volumeSlider.onValueChanged.AddListener(v =>
                {
                    GameSettings.MasterVolume = v;
                    AudioListener.volume = Mathf.Clamp01(v);
                });
            }

            if (invertYToggle != null) invertYToggle.onValueChanged.AddListener(v => GameSettings.InvertLookY = v);

            if (fullscreenToggle != null)
            {
                fullscreenToggle.onValueChanged.AddListener(v =>
                {
                    GameSettings.Fullscreen = v;
                    GameSettings.Apply();
                });
            }

            if (nameInput != null)
            {
                nameInput.characterLimit = 24;
                nameInput.onEndEdit.AddListener(v => GameSettings.PlayerName = v);
            }

            if (addressInput != null) addressInput.onEndEdit.AddListener(v => GameSettings.LastAddress = v);

            if (portInput != null)
            {
                portInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                portInput.characterLimit = 5;
            }
        }

        private void PopulateFromSettings()
        {
            if (nameInput != null) nameInput.SetTextWithoutNotify(GameSettings.PlayerName);
            if (addressInput != null) addressInput.SetTextWithoutNotify(GameSettings.LastAddress);
            if (portInput != null) portInput.SetTextWithoutNotify(GameSettings.LastPort.ToString(CultureInfo.InvariantCulture));

            if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
            if (invertYToggle != null) invertYToggle.SetIsOnWithoutNotify(GameSettings.InvertLookY);
            if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        }

        // -----------------------------------------------------------------------------------------
        //  Button handlers
        // -----------------------------------------------------------------------------------------

        private void OnSinglePlayerClicked()
        {
            if (_busy || GameManager.Instance == null) return;

            CommitNameField();
            _busy = true;

            // The ONLY difference between solo and hosting. Everything downstream is identical.
            if (!GameManager.Instance.StartSinglePlayer()) _busy = false;
            else ShowPanel(connectingPanel, cancelConnectButton != null ? cancelConnectButton.gameObject : null);
        }

        private void OnHostClicked()
        {
            if (_busy || GameManager.Instance == null) return;

            CommitNameField();
            if (!TryReadPort(out ushort port)) return;

            GameSettings.LastPort = port;
            GameSettings.Save();

            _busy = true;
            if (!GameManager.Instance.StartHost(port)) _busy = false;
            else ShowPanel(connectingPanel, cancelConnectButton != null ? cancelConnectButton.gameObject : null);
        }

        private void OnJoinClicked()
        {
            if (_busy || GameManager.Instance == null) return;

            CommitNameField();
            if (!TryReadPort(out ushort port)) return;

            string address = addressInput != null ? addressInput.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(address))
            {
                SetStatus("Enter the host's address.");
                return;
            }

            GameSettings.LastAddress = address;
            GameSettings.LastPort = port;
            GameSettings.Save();

            _busy = true;
            if (!GameManager.Instance.StartClient(address, port)) _busy = false;
            else ShowPanel(connectingPanel, cancelConnectButton != null ? cancelConnectButton.gameObject : null);
        }

        private void OnCancelConnect()
        {
            // LeaveSession is safe even mid-handshake; it drives the same teardown as a real
            // disconnect, so there is no separate "cancel" code path to keep correct.
            GameManager.Instance?.LeaveSession("Cancelled.");
            _busy = false;
            ShowPanel(titlePanel);
        }

        private void OnSettingsBack()
        {
            GameSettings.Save();
            ShowPanel(titlePanel);
        }

        private void OnReturnToMenu()
        {
            GameManager.Instance?.LeaveSession();
            ShowPanel(titlePanel);
        }

        private void CommitNameField()
        {
            if (nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text))
            {
                GameSettings.PlayerName = nameInput.text.Trim();
            }

            GameSettings.Save();
        }

        private bool TryReadPort(out ushort port)
        {
            port = GameSettings.LastPort;
            if (portInput == null) return true;

            if (!ushort.TryParse(portInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsed)
                || parsed == 0)
            {
                SetStatus("Port must be a number between 1 and 65535.");
                return false;
            }

            port = parsed;
            return true;
        }

        // -----------------------------------------------------------------------------------------
        //  GameManager event handlers
        // -----------------------------------------------------------------------------------------

        private void HandlePhaseChanged(GamePhase phase) => ApplyPhaseVisibility(phase);

        private void HandleSessionModeChanged(SessionMode mode)
        {
            // The connect attempt has resolved one way or the other.
            if (mode == SessionMode.None) _busy = false;
        }

        private void HandleSessionEnded(string reason)
        {
            _busy = false;
            ShowPanel(titlePanel);
            if (!string.IsNullOrEmpty(reason)) SetStatus(reason);
        }

        private void HandleSightingsChanged(int current, int target)
        {
            if (huntSightingsText != null) huntSightingsText.text = $"Confirmed sightings  {current} / {target}";
        }

        /// <summary>
        /// The single place the menu is shown or hidden. Driven purely by phase, which is why solo
        /// and multiplayer transition identically.
        /// </summary>
        private void ApplyPhaseVisibility(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Menu:
                    _menuTargetAlpha = 1f;
                    SetHudVisible(false);
                    if (_activePanel == connectingPanel || _activePanel == resultsPanel) ShowPanel(titlePanel);
                    break;

                case GamePhase.Loading:
                    _menuTargetAlpha = 1f;
                    SetHudVisible(false);
                    ShowPanel(connectingPanel, cancelConnectButton != null ? cancelConnectButton.gameObject : null);
                    if (connectingText != null) connectingText.text = "Making way to the loch...";
                    break;

                case GamePhase.Hunting:
                    _busy = false;
                    _menuTargetAlpha = 0f;   // Fade the menu out; the HUD takes over.
                    SetHudVisible(true);
                    break;

                case GamePhase.Results:
                    _menuTargetAlpha = 1f;
                    SetHudVisible(false);
                    _resultsRendered = false;   // Force a fresh render for this result.
                    PopulateResults();
                    ShowPanel(resultsPanel, resultsReturnButton != null ? resultsReturnButton.gameObject : null);
                    break;
            }
        }

        /// <summary>
        /// Renders the outcome, re-rendering whenever the underlying replicated values change.
        /// <para>
        /// SUBTLE NGO BEHAVIOUR, and the reason this is not a one-shot snapshot: when several
        /// NetworkVariables on one NetworkBehaviour go dirty in the same tick, a client applies them
        /// one at a time and fires each OnValueChanged DURING that read loop. So the phase callback
        /// that brings us here can run before the sibling outcome variables have been deserialised,
        /// and a snapshot taken here would show the PREVIOUS hunt's result. Re-rendering on change
        /// converges within the same frame and does not depend on field declaration order, which the
        /// CLR does not actually guarantee to be the reflection order NGO enumerates.
        /// </para>
        /// </summary>
        private void PopulateResults()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            bool won = gm.HuntSucceeded;
            int sightings = gm.ConfirmedSightings;

            // Nothing new to say - skip the string building entirely.
            if (_resultsRendered && won == _renderedWin && sightings == _renderedSightings) return;

            _resultsRendered = true;
            _renderedWin = won;
            _renderedSightings = sightings;

            if (resultsHeadlineText != null)
            {
                resultsHeadlineText.text = won ? "SHE'S REAL" : "THE LOCH KEEPS ITS SECRET";
            }

            if (resultsDetailText != null)
            {
                resultsDetailText.text = won
                    ? $"{sightings} of {gm.TargetSightings} confirmed contacts logged. Evidence secured."
                    : $"Only {sightings} of {gm.TargetSightings} contacts confirmed before the light went.";
            }
        }

        // -----------------------------------------------------------------------------------------
        //  HUD feeds
        // -----------------------------------------------------------------------------------------

        private void HandleSonarContacts(SonarContact[] contacts)
        {
            if (sonarReadoutText == null) return;

            _readoutBuilder.Clear();
            _readoutBuilder.AppendLine("SONAR RETURN");

            if (contacts == null || contacts.Length == 0)
            {
                _readoutBuilder.AppendLine("  no returns");
            }
            else
            {
                for (int i = 0; i < contacts.Length; i++)
                {
                    SonarContact c = contacts[i];

                    // Weak returns are reported as unidentified: the player must ping again to
                    // resolve them. This is the "detect an anomaly" beat, not "read a label".
                    string label = c.Clarity >= 0.6f ? c.KindEnum.ToString().ToUpperInvariant() : "UNIDENTIFIED";

                    _readoutBuilder.AppendLine(
                        $"  {label,-12} brg {Mathf.RoundToInt(c.Bearing):000}  " +
                        $"rng {Mathf.RoundToInt(c.Range):000}m  conf {Mathf.RoundToInt(c.Clarity * 100f):00}%");
                }
            }

            sonarReadoutText.text = _readoutBuilder.ToString();
        }

        private void HandleSonarCharge(float normalized)
        {
            if (sonarChargeFill != null) sonarChargeFill.fillAmount = Mathf.Clamp01(normalized);
        }

        private void HandleRadioMessage(string message)
        {
            if (radioText == null) return;
            radioText.text = message;
            _radioClearTime = Time.time + 7f;
        }

        // -----------------------------------------------------------------------------------------
        //  Panels + focus
        // -----------------------------------------------------------------------------------------

        private void ShowPanel(GameObject panel, GameObject focus = null)
        {
            SetActiveSafe(titlePanel, panel == titlePanel);
            SetActiveSafe(multiplayerPanel, panel == multiplayerPanel);
            SetActiveSafe(settingsPanel, panel == settingsPanel);
            SetActiveSafe(connectingPanel, panel == connectingPanel);
            SetActiveSafe(resultsPanel, panel == resultsPanel);

            _activePanel = panel;

            // Explicit focus keeps the menu fully navigable on a gamepad or by keyboard - without
            // this, EventSystem loses its selection every time a panel is disabled.
            GameObject target = focus;
            if (target == null && panel == titlePanel && singlePlayerButton != null) target = singlePlayerButton.gameObject;

            if (target != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(target);
            }
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private void SetHudVisible(bool visible)
        {
            if (huntHud == null) return;
            huntHud.alpha = visible ? 1f : 0f;
            huntHud.interactable = false;      // The HUD is display-only; never steal clicks.
            huntHud.blocksRaycasts = false;
        }

        private void SetStatus(string message)
        {
            if (statusText == null) return;
            statusText.text = message;
            _statusClearTime = Time.time + statusHoldSeconds;
        }

        // -----------------------------------------------------------------------------------------
        //  Update
        // -----------------------------------------------------------------------------------------

        private void Update()
        {
            UpdateMenuFade();
            UpdateFallbackView();
            UpdateTransientText();
            UpdateHuntHud();
            UpdateBackNavigation();
        }

        /// <summary>
        /// Owns the "who is rendering and listening" question for this client.
        /// <para>
        /// The Bootstrap scene needs a camera and an AudioListener to show a title screen at all,
        /// but the player's boat brings its own pair. Two enabled cameras render on top of each
        /// other and two enabled AudioListeners make Unity log a warning every frame and pick one
        /// arbitrarily - so the menu pair is live exactly while this client has no boat. Driving it
        /// off LocalPlayer rather than off GamePhase is what makes it correct during the results
        /// screen and during a late join, where phase and boat lifetime do not line up.
        /// </para>
        /// </summary>
        private void UpdateFallbackView()
        {
            bool needed = PlayerController.LocalPlayer == null;

            if (menuCamera != null && menuCamera.enabled != needed) menuCamera.enabled = needed;
            if (menuAudioListener != null && menuAudioListener.enabled != needed) menuAudioListener.enabled = needed;
        }

        private void UpdateMenuFade()
        {
            if (menuRoot == null) return;

            float step = fadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeDuration;
            menuRoot.alpha = Mathf.MoveTowards(menuRoot.alpha, _menuTargetAlpha, step);

            // A fully transparent menu must not eat clicks from the game underneath it.
            bool live = menuRoot.alpha > 0.99f;
            menuRoot.interactable = live;
            menuRoot.blocksRaycasts = live;
        }

        private void UpdateTransientText()
        {
            if (statusText != null && statusText.text.Length > 0 && Time.time > _statusClearTime)
            {
                statusText.text = string.Empty;
            }

            if (radioText != null && radioText.text.Length > 0 && Time.time > _radioClearTime)
            {
                radioText.text = string.Empty;
            }
        }

        private void UpdateHuntHud()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // Catches outcome values that land a frame after the phase change (see PopulateResults).
            if (gm.CurrentPhase == GamePhase.Results)
            {
                PopulateResults();
                return;
            }

            if (huntTimerText == null || gm.CurrentPhase != GamePhase.Hunting) return;

            // Derived from the synchronised server clock, so every crew member sees the same
            // number without the server replicating a per-frame countdown.
            float remaining = gm.RemainingSeconds;
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            huntTimerText.text = $"{minutes:0}:{seconds:00}";
        }

        /// <summary>Escape backs out one level, or opens the pause path during a hunt.</summary>
        private void UpdateBackNavigation()
        {
            if (!WasCancelPressed()) return;

            var gm = GameManager.Instance;

            if (gm != null && gm.CurrentPhase == GamePhase.Hunting)
            {
                // Deliberately a hard leave rather than a pause menu: a networked session cannot be
                // paused, so offering "Resume" would be a lie in multiplayer. Solo behaves the same
                // way for consistency, and a real pause would be single-player-only.
                gm.LeaveSession();
                return;
            }

            if (_activePanel == multiplayerPanel || _activePanel == settingsPanel)
            {
                if (_activePanel == settingsPanel) GameSettings.Save();
                ShowPanel(titlePanel);
            }
            else if (_activePanel == connectingPanel)
            {
                OnCancelConnect();
            }
        }

        private static bool WasCancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
