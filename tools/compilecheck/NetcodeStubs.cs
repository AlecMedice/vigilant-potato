// -----------------------------------------------------------------------------
// Signature-only stubs for Netcode for GameObjects and Unity.Collections.
// See UnityStubs.cs for what this harness does and does not prove.
//
// One caveat specific to these: NGO relies on IL post-processing at build time to
// generate the actual RPC plumbing and to register NetworkVariables. None of that
// runs here. A method marked [ServerRpc] compiles fine against these stubs and
// would still be rejected by Unity if, say, it were not named with the required
// `ServerRpc` suffix. Those rules are checked in the Editor, not here.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Collections
{
    public struct FixedString32Bytes
    {
        public FixedString32Bytes(string value) { }
        public int Length => 0;
        public override string ToString() => string.Empty;
        public static implicit operator FixedString32Bytes(string value) => default;
    }

    public struct FixedString64Bytes
    {
        public FixedString64Bytes(string value) { }
        public override string ToString() => string.Empty;
    }

    public struct FixedString128Bytes
    {
        public FixedString128Bytes(string value) { }
        public int Length => 0;
        public override string ToString() => string.Empty;
        public static implicit operator FixedString128Bytes(string value) => default;
    }
}

namespace Unity.Netcode
{
    public interface IReaderWriter { }
    public interface INetworkSerializable
    {
        void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter;
    }

    public struct BufferSerializer<T> where T : IReaderWriter
    {
        public bool IsReader => false;
        public bool IsWriter => false;
        public void SerializeValue(ref float value) { }
        public void SerializeValue(ref int value) { }
        public void SerializeValue(ref uint value) { }
        public void SerializeValue(ref byte value) { }
        public void SerializeValue(ref bool value) { }
        public void SerializeValue(ref ulong value) { }
        public void SerializeValue(ref double value) { }
        public void SerializeValue(ref Vector3 value) { }
        public void SerializeValue(ref Unity.Collections.FixedString32Bytes value) { }
        public void SerializeValue(ref Unity.Collections.FixedString128Bytes value) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute { public bool RequireOwnership; }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute { }

    public struct ServerRpcSendParams { }
    public struct ServerRpcReceiveParams { public ulong SenderClientId; }
    public struct ServerRpcParams { public ServerRpcSendParams Send; public ServerRpcReceiveParams Receive; }

    public struct ClientRpcSendParams { public ulong[] TargetClientIds; }
    public struct ClientRpcReceiveParams { public ulong SenderClientId; }
    public struct ClientRpcParams { public ClientRpcSendParams Send; public ClientRpcReceiveParams Receive; }

    public enum NetworkVariableReadPermission { Everyone, Owner }
    public enum NetworkVariableWritePermission { Server, Owner }

    public class NetworkVariableBase { }

    public class NetworkVariable<T> : NetworkVariableBase
    {
        public delegate void OnValueChangedDelegate(T previousValue, T newValue);

        public NetworkVariable() { }
        public NetworkVariable(T value) { }
        public NetworkVariable(T value, NetworkVariableReadPermission read, NetworkVariableWritePermission write) { }

        public T Value { get; set; }
        public event OnValueChangedDelegate OnValueChanged;
    }

    public struct NetworkTime
    {
        public double Time => 0d;
        public double FixedTime => 0d;
        public int Tick => 0;
    }

    public class NetworkObject : Component
    {
        public delegate bool VisibilityDelegate(ulong clientId);

        public ulong NetworkObjectId => 0;
        public ulong OwnerClientId => 0;
        public bool IsSpawned => false;
        public VisibilityDelegate CheckObjectVisibility;

        public void Spawn(bool destroyWithScene = true) { }
        public void SpawnAsPlayerObject(ulong clientId, bool destroyWithScene = true) { }
        public void SpawnWithOwnership(ulong clientId, bool destroyWithScene = true) { }
        public void Despawn(bool destroy = true) { }
        public bool TrySetParent(NetworkObject parent, bool worldPositionStays = true) => false;
        public bool TrySetParent(Transform parent, bool worldPositionStays = true) => false;
        public void NetworkShow(ulong clientId) { }
        public void NetworkHide(ulong clientId) { }
        public bool IsNetworkVisibleTo(ulong clientId) => false;
    }

    public abstract class NetworkBehaviour : MonoBehaviour
    {
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsHost => false;
        public bool IsOwner => false;
        public bool IsOwnedByServer => false;
        public bool IsSpawned => false;
        public ulong OwnerClientId => 0;
        public ulong NetworkObjectId => 0;
        public NetworkObject NetworkObject => default;
        public NetworkManager NetworkManager => default;

        public virtual void OnNetworkSpawn() { }
        public virtual void OnNetworkDespawn() { }
        public virtual void OnGainedOwnership() { }
        public virtual void OnLostOwnership() { }
    }

    public class NetworkClient
    {
        public ulong ClientId;
        public NetworkObject PlayerObject;
        public List<NetworkObject> OwnedObjects;
    }

    public interface INetworkPrefabInstanceHandler { }

    public class NetworkPrefabs { public void Add(GameObject prefab) { } }

    public class NetworkConfig
    {
        public NetworkTransport NetworkTransport;
        public GameObject PlayerPrefab;
        public NetworkPrefabs Prefabs = new NetworkPrefabs();
        public bool ConnectionApproval;
        public bool EnableSceneManagement;
        public bool ForceSamePrefabs;
        public uint TickRate;
        public int ClientConnectionBufferTimeout;
        public byte[] ConnectionData;
    }

    public abstract class NetworkTransport : MonoBehaviour { }

    public class NetworkManager : MonoBehaviour
    {
        public struct ConnectionApprovalRequest
        {
            public byte[] Payload;
            public ulong ClientNetworkId;
        }

        public class ConnectionApprovalResponse
        {
            public bool Approved;
            public bool CreatePlayerObject;
            public uint? PlayerPrefabHash;
            public Vector3? Position;
            public Quaternion? Rotation;
            public string Reason;
            public bool Pending;
        }

        public static NetworkManager Singleton => default;

        public NetworkConfig NetworkConfig = new NetworkConfig();
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsHost => false;
        public bool IsListening => false;
        public bool IsConnectedClient => false;
        public ulong LocalClientId => 0;
        public static ulong ServerClientId => 0;
        public string DisconnectReason => string.Empty;
        public NetworkTime ServerTime => default;
        public NetworkTime LocalTime => default;
        public IReadOnlyDictionary<ulong, NetworkClient> ConnectedClients => default;
        public IReadOnlyList<ulong> ConnectedClientsIds => default;

        public Action<ConnectionApprovalRequest, ConnectionApprovalResponse> ConnectionApprovalCallback;
        public event Action OnServerStarted;
        public event Action OnServerStopped;
        public event Action<ulong> OnClientConnectedCallback;
        public event Action<ulong> OnClientDisconnectCallback;

        public bool StartHost() => false;
        public bool StartServer() => false;
        public bool StartClient() => false;
        public void Shutdown(bool discardMessageQueue = false) { }
        public void DisconnectClient(ulong clientId) { }
        public void DisconnectClient(ulong clientId, string reason) { }
        public void AddNetworkPrefab(GameObject prefab) { }
        public void RemoveNetworkPrefab(GameObject prefab) { }
    }
}

namespace Unity.Netcode.Components
{
    public class NetworkTransform : NetworkBehaviour
    {
        public bool InLocalSpace;
        public bool Interpolate;
        public bool SyncPositionX, SyncPositionY, SyncPositionZ;
        public bool SyncRotAngleX, SyncRotAngleY, SyncRotAngleZ;
        public bool SyncScaleX, SyncScaleY, SyncScaleZ;
        public float PositionThreshold;
        public float RotAngleThreshold;
        protected virtual bool OnIsServerAuthoritative() => true;
    }
}

namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : NetworkTransport
    {
        public ushort ConnectionPort;
        public void SetConnectionData(string address, ushort port) { }
        public void SetConnectionData(string address, ushort port, string listenAddress) { }
    }
}
