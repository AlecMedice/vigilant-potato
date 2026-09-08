// Minimal API-shape stubs of Unity Netcode for GameObjects (NGO).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Netcode
{
    public interface IReaderWriter { }
    public struct BufferSerializer<TReaderWriter> where TReaderWriter : IReaderWriter {
        public void SerializeValue(ref float v) { }
        public void SerializeValue(ref byte v) { }
        public void SerializeValue(ref int v) { }
        public void SerializeValue(ref bool v) { }
        public void SerializeValue(ref Vector3 v) { }
    }
    public interface INetworkSerializable {
        void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter;
    }

    public enum NetworkVariableReadPermission { Everyone, Owner }
    public enum NetworkVariableWritePermission { Server, Owner }

    public class NetworkVariable<T> where T : unmanaged
    {
        public delegate void OnValueChangedDelegate(T previousValue, T newValue);
        public event OnValueChangedDelegate OnValueChanged;
        public NetworkVariable(T value = default,
            NetworkVariableReadPermission read = NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission write = NetworkVariableWritePermission.Server) { }
        public T Value { get; set; }
    }

    public struct ServerRpcReceiveParams { public ulong SenderClientId; }
    public struct ServerRpcSendParams { }
    public struct ServerRpcParams { public ServerRpcReceiveParams Receive; public ServerRpcSendParams Send; }
    public struct ClientRpcSendParams { public IReadOnlyList<ulong> TargetClientIds; }
    public struct ClientRpcReceiveParams { }
    public struct ClientRpcParams { public ClientRpcSendParams Send; public ClientRpcReceiveParams Receive; }

    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute { public bool RequireOwnership { get; set; } = true; }
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute { }

    public enum SceneEventProgressStatus { Started, SceneNotLoaded, SceneEventInProgress, InvalidSceneName, InternalNetcodeError }

    public struct NetworkTime { public double Time => 0d; public float TimeAsFloat => 0f; public int Tick => 0; }

    public class NetworkObject : Component
    {
        public delegate bool VisibilityDelegate(ulong clientId);
        public VisibilityDelegate CheckObjectVisibility { get; set; }
        public bool IsSpawned => false;
        public ulong NetworkObjectId => 0;
        public void Spawn(bool destroyWithScene = false) { }
        public void SpawnAsPlayerObject(ulong clientId, bool destroyWithScene = false) { }
        public void Despawn(bool destroy = true) { }
        public void NetworkShow(ulong clientId) { }
        public void NetworkHide(ulong clientId) { }
        public bool IsNetworkVisibleTo(ulong clientId) => false;
    }

    public class NetworkClient { public ulong ClientId => 0; public NetworkObject PlayerObject => null; }

    public class NetworkConfig {
        public bool ConnectionApproval { get; set; }
        public bool EnableSceneManagement { get; set; }
        public byte[] ConnectionData { get; set; }
        public NetworkObject PlayerPrefab { get; set; }
    }

    public class NetworkSceneManager {
        public event Action<ulong, string, LoadSceneMode> OnLoadComplete;
        public event Action<string, LoadSceneMode, List<ulong>, List<ulong>> OnLoadEventCompleted;
        public event Action<ulong> OnSynchronizeComplete;
        public SceneEventProgressStatus LoadScene(string sceneName, LoadSceneMode mode) => SceneEventProgressStatus.Started;
        public SceneEventProgressStatus UnloadScene(Scene scene) => SceneEventProgressStatus.Started;
    }

    public class NetworkManager : MonoBehaviour
    {
        public struct ConnectionApprovalRequest { public byte[] Payload; public ulong ClientNetworkId; }
        public class ConnectionApprovalResponse {
            public bool Approved; public bool CreatePlayerObject; public bool Pending; public string Reason;
            public Vector3? Position; public Quaternion? Rotation;
        }

        public static NetworkManager Singleton { get; }
        public Action<ConnectionApprovalRequest, ConnectionApprovalResponse> ConnectionApprovalCallback;
        public event Action OnServerStarted;
        public event Action<ulong> OnClientConnectedCallback;
        public event Action<ulong> OnClientDisconnectCallback;
        public event Action OnTransportFailure;
        public event Action<bool> OnServerStopped;
        public event Action<bool> OnClientStopped;

        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsHost => false;
        public bool IsListening => false;
        public bool IsConnectedClient => false;
        public bool ShutdownInProgress => false;
        public ulong LocalClientId => 0;
        public ulong ServerClientId => 0;
        public string DisconnectReason => string.Empty;
        public NetworkTime ServerTime => default;
        public NetworkConfig NetworkConfig { get; } = new NetworkConfig();
        public NetworkSceneManager SceneManager => null;
        public IReadOnlyDictionary<ulong, NetworkClient> ConnectedClients => null;
        public IReadOnlyList<NetworkClient> ConnectedClientsList => null;
        public IReadOnlyList<ulong> ConnectedClientsIds => null;
        public bool StartHost() => false;
        public bool StartClient() => false;
        public bool StartServer() => false;
        public void Shutdown(bool discardMessageQueue = false) { }
    }

    public abstract class NetworkBehaviour : MonoBehaviour
    {
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsHost => false;
        public bool IsOwner => false;
        public bool IsSpawned => false;
        public ulong OwnerClientId => 0;
        public ulong NetworkObjectId => 0;
        public NetworkObject NetworkObject => null;
        public NetworkManager NetworkManager => null;
        public virtual void OnNetworkSpawn() { }
        public virtual void OnNetworkDespawn() { }
        public virtual void OnDestroy() { }
    }
}

namespace Unity.Netcode.Components
{
    public class NetworkTransform : NetworkBehaviour {
        protected virtual bool OnIsServerAuthoritative() => true;
    }
}

namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : MonoBehaviour {
        public void SetConnectionData(string ipv4Address, ushort port) { }
        public void SetConnectionData(string ipv4Address, ushort port, string listenAddress) { }
    }
}
