// -----------------------------------------------------------------------------
// SonarSet — the boat's sonar, and the only way a contact becomes a sighting.
//
// EVERY PING IS RESOLVED ON THE SERVER.
// A client asks for a ping and receives a list of blips. It is never told where
// the monster is, only what came back — so the scope cannot be turned into a
// wallhack by reading memory or intercepting packets. Combined with NessieAI's
// distance-based network visibility, a client that has not earned a contact
// genuinely does not have the information.
//
// THE SCOPE IS SHARED HARDWARE.
// Contacts go to every client, not just the operator, because the scope is a
// physical display in the wheelhouse and anyone standing there can read it. The
// crew pressure comes from the stations instead: one person pings, one person
// steers, one person watches, and only the watch can confirm with their own eyes.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using LochNess.AI;
using LochNess.Boot;
using LochNess.Core;
using LochNess.Sim;
using Unity.Netcode;
using UnityEngine;

namespace LochNess.Vessel
{
    /// <summary>Wire form of Sim.Contact. The sim layer must not know about NGO.</summary>
    public struct NetContact : INetworkSerializable
    {
        public float Bearing;
        public float Range;
        public float Depth;
        public float Clarity;
        public byte Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Bearing);
            serializer.SerializeValue(ref Range);
            serializer.SerializeValue(ref Depth);
            serializer.SerializeValue(ref Clarity);
            serializer.SerializeValue(ref Kind);
        }

        public static NetContact From(Contact c) => new NetContact
        {
            Bearing = c.Bearing,
            Range = c.Range,
            Depth = c.Depth,
            Clarity = c.Clarity,
            Kind = (byte)c.Kind
        };

        public ContactKind KindEnum => (ContactKind)Kind;
    }

    public sealed class SonarSet : NetworkBehaviour
    {
        public static SonarSet Instance { get; private set; }

        /// <summary>Fired on every peer when a ping returns. The HUD scope draws these.</summary>
        public static event Action<NetContact[]> OnContacts;
        /// <summary>Fired on every peer as the set recharges, 0..1.</summary>
        public static event Action<float> OnCharge;

        [Header("Set")]
        [SerializeField] private float range = 130f;
        [SerializeField] private float cooldownSeconds = 3f;
        [Tooltip("Clarity at or above which a return may be logged as a sighting.")]
        [SerializeField] private float confirmClarity = 0.65f;
        [Tooltip("Range at or below which a return may be logged as a sighting.")]
        [SerializeField] private float confirmRange = 60f;
        [Tooltip("How much threat one ping adds to the monster at point-blank range.")]
        [SerializeField] private float pingAggression = 0.55f;

        [Header("Visual sighting (bow watch)")]
        [SerializeField] private float visualRange = 85f;
        [SerializeField] private float visualConeDegrees = 38f;

        /// <summary>Server time at which the set is ready again. Replicated so every
        /// peer's charge meter agrees, including players who are not the operator.</summary>
        private readonly NetworkVariable<double> _readyAtServerTime = new NetworkVariable<double>(0d);

        private readonly List<Contact> _scratch = new List<Contact>(12);
        private System.Random _rng;

        public float Range => range;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                // Server-only RNG. Seeded from the clock so two sessions do not
                // produce an identical clutter pattern.
                _rng = new System.Random(Environment.TickCount);
                _readyAtServerTime.Value = 0d;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>0..1 — how far through the recharge the set is. 1 means ready.</summary>
        public float Charge
        {
            get
            {
                var net = NetworkManager.Singleton;
                if (net == null || !net.IsListening) return 1f;
                double remaining = _readyAtServerTime.Value - net.ServerTime.Time;
                if (remaining <= 0d) return 1f;
                return Mathf.Clamp01(1f - (float)(remaining / Mathf.Max(0.01f, cooldownSeconds)));
            }
        }

        private void Update()
        {
            OnCharge?.Invoke(Charge);
        }

        // ---------------------------------------------------------------------
        // Pinging
        // ---------------------------------------------------------------------

        [ServerRpc(RequireOwnership = false)]
        public void PingServerRpc(ServerRpcParams parameters = default)
        {
            ulong sender = parameters.Receive.SenderClientId;

            // Authority check: only whoever holds the sonar station may ping. The UI
            // already hides the prompt, but the UI is not a security boundary.
            var boat = BoatController.Instance;
            if (boat == null || boat.OccupantOf(StationRole.Sonar) != sender) return;

            ServerPing();
        }

        /// <summary>
        /// Resolve a ping from the boat's transducer. Server-side entry point, also
        /// used by the AI crewmate in single player, which has no RPC path.
        /// </summary>
        /// <returns>True if the ping was fired (i.e. the set was charged).</returns>
        public bool ServerPing()
        {
            if (!IsServer) return false;

            var net = NetworkManager.Singleton;
            double now = net != null ? net.ServerTime.Time : 0d;
            if (now < _readyAtServerTime.Value) return false;
            _readyAtServerTime.Value = now + cooldownSeconds;

            Vector3 origin = transform.position;

            var nessie = NessieAI.ServerInstance;
            var quarry = new QuarrySnapshot
            {
                Exists = nessie != null,
                Position = nessie != null ? nessie.transform.position.ToSim() : Vec3.Zero,
                Signature = nessie != null ? nessie.SonarSignature : 0f
            };

            // Clutter count rises with range: a longer sweep touches more of the loch
            // and therefore more weed, fish and sunken timber.
            int clutter = 2 + Mathf.RoundToInt(range / 90f);

            bool confirmed = SonarModel.Resolve(
                origin.ToSim(), range, quarry, confirmClarity, confirmRange, clutter, _rng, _scratch);

            var wire = new NetContact[_scratch.Count];
            for (int i = 0; i < _scratch.Count; i++) wire[i] = NetContact.From(_scratch[i]);
            DeliverContactsClientRpc(wire);

            // A ping is not free. This is the whole tension of the loop: the only tool
            // that finds her is also the thing that tells her where you are.
            if (nessie != null)
            {
                nessie.HearPing(Vector3.Distance(origin, nessie.transform.position), pingAggression);
            }

            if (confirmed)
            {
                MatchState.Server?.RegisterSighting("SONAR CONTACT CONFIRMED — logged");
            }

            return true;
        }

        [ClientRpc]
        private void DeliverContactsClientRpc(NetContact[] contacts)
        {
            OnContacts?.Invoke(contacts ?? Array.Empty<NetContact>());
        }

        // ---------------------------------------------------------------------
        // Visual sighting — the bow watch's job
        // ---------------------------------------------------------------------

        [ServerRpc(RequireOwnership = false)]
        public void ReportVisualServerRpc(ServerRpcParams parameters = default)
        {
            ulong sender = parameters.Receive.SenderClientId;

            var boat = BoatController.Instance;
            if (boat == null || boat.OccupantOf(StationRole.Watch) != sender) return;

            var nessie = NessieAI.ServerInstance;
            if (nessie == null || !nessie.IsSurfaced)
            {
                MatchState.Server?.RadioToClient(sender, "Nothing but chop out there.");
                return;
            }

            var net = NetworkManager.Singleton;
            if (net == null || !net.ConnectedClients.TryGetValue(sender, out NetworkClient client)) return;
            var playerObject = client.PlayerObject;
            if (playerObject == null) return;

            // Validated against the REPLICATED player transform rather than anything
            // the client asserts in the call. A modified client can still lie about
            // where it is looking — but only within the deck it is standing on, which
            // is a few degrees of advantage, not a free win.
            Vector3 eye = playerObject.transform.position + Vector3.up * CrewSightHeight;
            Vector3 toTarget = nessie.transform.position - eye;
            float distance = toTarget.magnitude;

            if (distance > visualRange)
            {
                MatchState.Server?.RadioToClient(sender, "Too far to be sure. Get us closer.");
                return;
            }

            float angle = Vector3.Angle(playerObject.transform.forward, toTarget);
            if (angle > visualConeDegrees)
            {
                MatchState.Server?.RadioToClient(sender, "You're not even looking at it.");
                return;
            }

            // Being seen is provoking. A confirmed visual costs you the element of
            // surprise, which is a fair price for a guaranteed point.
            nessie.Provoke(0.45f);
            MatchState.Server?.RegisterSighting("VISUAL CONFIRMED — she broke the surface");
        }

        /// <summary>Eye height above the crew member's feet. Matches CrewBuilder.EyeHeight.</summary>
        private const float CrewSightHeight = 1.62f;
    }
}
