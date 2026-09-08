// -----------------------------------------------------------------------------
// MatchState — the replicated state of one hunt.
//
// Split out from GameManager on purpose. GameManager has to exist on the title
// screen, before any networking is running, so it cannot be a NetworkBehaviour;
// MatchState is spawned by the host when a hunt begins and despawned when it
// ends, which is exactly the lifetime of the data it holds.
//
// THE CLOCK IS A DEADLINE, NOT A COUNTDOWN.
// Replicating a ticking float would burn bandwidth every tick and still drift.
// Instead the server publishes the server-time at which the hunt ends, once, and
// every peer subtracts its own synchronised NetworkTime from it. A client that
// joins late, or stalls for a second, gets the right number with no correction.
// -----------------------------------------------------------------------------

using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LochNess.Core
{
    public enum GamePhase : byte
    {
        Menu = 0,
        Hunting = 1,
        Results = 2
    }

    public sealed class MatchState : NetworkBehaviour
    {
        /// <summary>Present on every peer once a hunt is running.</summary>
        public static MatchState Instance { get; private set; }
        /// <summary>Present on the host only. Null-conditional calls on this are the
        /// idiom used throughout for "do this if I am the server".</summary>
        public static MatchState Server { get; private set; }

        public static event Action<GamePhase> OnPhaseChanged;
        public static event Action<int, int> OnSightingsChanged;
        /// <summary>Radio traffic for the HUD log.</summary>
        public static event Action<string> OnRadio;

        [SerializeField] private float huntDurationSeconds = 480f;
        [SerializeField] private int targetSightings = 3;

        private readonly NetworkVariable<GamePhase> _phase = new NetworkVariable<GamePhase>(GamePhase.Menu);
        private readonly NetworkVariable<double> _endsAtServerTime = new NetworkVariable<double>(0d);
        private readonly NetworkVariable<int> _sightings = new NetworkVariable<int>(0);
        private readonly NetworkVariable<bool> _succeeded = new NetworkVariable<bool>(false);

        public GamePhase Phase => _phase.Value;
        public int Sightings => _sightings.Value;
        public int TargetSightings => targetSightings;
        public bool Succeeded => _succeeded.Value;

        public float RemainingSeconds
        {
            get
            {
                var net = NetworkManager.Singleton;
                if (net == null || _phase.Value != GamePhase.Hunting) return 0f;
                return Mathf.Max(0f, (float)(_endsAtServerTime.Value - net.ServerTime.Time));
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) Server = this;

            _phase.OnValueChanged += HandlePhaseChanged;
            _sightings.OnValueChanged += HandleSightingsChanged;

            // Fire once for the current values. A client that spawns this object mid-hunt
            // has missed every OnValueChanged that came before it existed.
            OnPhaseChanged?.Invoke(_phase.Value);
            OnSightingsChanged?.Invoke(_sightings.Value, targetSightings);
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _sightings.OnValueChanged -= HandleSightingsChanged;

            if (Instance == this) Instance = null;
            if (Server == this) Server = null;
        }

        private void HandlePhaseChanged(GamePhase previous, GamePhase next) => OnPhaseChanged?.Invoke(next);
        private void HandleSightingsChanged(int previous, int next) => OnSightingsChanged?.Invoke(next, targetSightings);

        // ---------------------------------------------------------------------
        // Server control
        // ---------------------------------------------------------------------

        public void BeginHunt()
        {
            if (!IsServer) return;

            var net = NetworkManager.Singleton;
            _sightings.Value = 0;
            _succeeded.Value = false;
            _endsAtServerTime.Value = (net != null ? net.ServerTime.Time : 0d) + huntDurationSeconds;
            _phase.Value = GamePhase.Hunting;

            RadioAll($"Sonar warmed up. {targetSightings} confirmed sightings to prove she's here.");
        }

        /// <summary>Log a confirmed sighting. Called by SonarSet for both sonar and visual confirms.</summary>
        public void RegisterSighting(string message)
        {
            if (!IsServer || _phase.Value != GamePhase.Hunting) return;

            _sightings.Value = Mathf.Min(_sightings.Value + 1, targetSightings);
            RadioAll(message);

            if (_sightings.Value >= targetSightings)
            {
                _succeeded.Value = true;
                _phase.Value = GamePhase.Results;
                RadioAll("That's the lot. Bring her about — we've got what we came for.");
            }
        }

        private void Update()
        {
            if (!IsServer || _phase.Value != GamePhase.Hunting) return;

            var net = NetworkManager.Singleton;
            if (net == null) return;

            if (net.ServerTime.Time >= _endsAtServerTime.Value)
            {
                _succeeded.Value = false;
                _phase.Value = GamePhase.Results;
                RadioAll("Light's going. That's us done for the day.");
            }
        }

        // ---------------------------------------------------------------------
        // Radio
        // ---------------------------------------------------------------------

        /// <summary>Broadcast a line to the whole crew's HUD log.</summary>
        public void RadioAll(string message)
        {
            if (!IsServer) return;
            RadioClientRpc(Clip(message));
        }

        /// <summary>Send a line to one crew member — used for "you're not even looking at it".</summary>
        public void RadioToClient(ulong clientId, string message)
        {
            if (!IsServer) return;

            var parameters = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            RadioClientRpc(Clip(message), parameters);
        }

        [ClientRpc]
        private void RadioClientRpc(FixedString128Bytes message, ClientRpcParams parameters = default)
        {
            OnRadio?.Invoke(message.ToString());
        }

        /// <summary>
        /// FixedString128Bytes holds 125 bytes. Clipping by character would throw on any
        /// long line containing a multi-byte glyph, so clip by byte — same class of bug
        /// as the name truncation in CrewController.
        /// </summary>
        private static FixedString128Bytes Clip(string message)
        {
            if (string.IsNullOrEmpty(message)) return default;

            const int capacity = 125;
            while (message.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(message) > capacity)
            {
                int drop = message.Length >= 2
                           && char.IsLowSurrogate(message[message.Length - 1])
                           && char.IsHighSurrogate(message[message.Length - 2]) ? 2 : 1;
                message = message.Substring(0, message.Length - drop);
            }
            return new FixedString128Bytes(message);
        }
    }
}
