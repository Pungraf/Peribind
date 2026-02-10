using System;
using System.Collections.Generic;
using System.Text;
using Peribind.Application.Sessions;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.Board;
using Peribind.Unity.ScriptableObjects;
using Unity.Netcode;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class NetworkGameController : NetworkBehaviour
    {
        private static NetworkGameController _instance;
        private static readonly Dictionary<ulong, int> s_clientToPlayerId = new Dictionary<ulong, int>();
        private static readonly Dictionary<ulong, string> s_clientToAuthId = new Dictionary<ulong, string>();
        private static readonly Dictionary<string, int> s_authToPlayerId = new Dictionary<string, int>();
        [SerializeField] private GridMapper gridMapper;
        [SerializeField] private GameConfigSO gameConfig;

        [SerializeField] private PlayerIdentityProvider identityProvider;
        [SerializeField] private string disconnectSceneName = "LobbyScene";
        private GameSession _session;
        private Dictionary<string, PieceDefinitionSO> _piecesById;
        private Dictionary<int, string> _pieceIdByHash;
        private int _localPlayerIdOverride = -1;
        private NetworkList<AuthPlayerNet> _authPlayerMap = new NetworkList<AuthPlayerNet>();
        private NetworkList<PlacedPieceMetaNet> _placedPieceMeta = new NetworkList<PlacedPieceMetaNet>();
        private NetworkList<PlacedPieceCellNet> _placedPieceCells = new NetworkList<PlacedPieceCellNet>();
        private NetworkList<InventoryEntryNet> _inventoryEntries = new NetworkList<InventoryEntryNet>();
        private NetworkList<TerritoryCellNet> _territoryCells = new NetworkList<TerritoryCellNet>();
        private NetworkList<PlayerScoreNet> _playerScores = new NetworkList<PlayerScoreNet>();
        private NetworkList<PlayerFinishedNet> _playerFinished = new NetworkList<PlayerFinishedNet>();

        private NetworkVariable<int> _stateVersion = new NetworkVariable<int>();
        private NetworkVariable<int> _currentPlayerId = new NetworkVariable<int>();
        private NetworkVariable<int> _currentRound = new NetworkVariable<int>();
        private NetworkVariable<int> _phase = new NetworkVariable<int>();
        private NetworkVariable<int> _roundRevision = new NetworkVariable<int>();
        private NetworkVariable<bool> _isGameOver = new NetworkVariable<bool>();
        private NetworkVariable<bool> _wasSurrendered = new NetworkVariable<bool>();
        private NetworkVariable<int> _surrenderingPlayerId = new NetworkVariable<int>();
        private NetworkVariable<int> _winningPlayerId = new NetworkVariable<int>();

        public GameSession Session => _session;
        public event Action SessionUpdated;
        public event Action<int, int> SurrenderResolved;

        public int LocalPlayerId => ResolvePlayerId(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0);
        public bool WasSurrendered => _session != null && _session.WasSurrendered;
        public int SurrenderingPlayerId => _session != null ? _session.SurrenderingPlayerId : -1;
        public int WinningPlayerId => _session != null ? _session.WinningPlayerId : -1;
        public bool SurrenderAckReceived => _surrenderAckReceived;

        private bool _surrenderAckReceived;

        private void Awake()
        {
            _instance = this;
            if (identityProvider == null)
            {
                identityProvider = FindObjectOfType<PlayerIdentityProvider>();
            }

            InitializeSessionIfNeeded();
        }

        private void Start()
        {
            TrySpawnOnServer();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            InitializeNetworkLists();
            Debug.Log($"[NetState] OnNetworkSpawn IsServer={IsServer} IsClient={IsClient} IsHost={IsHost}");

            if (IsServer && NetworkManager.Singleton != null)
            {
                SyncAuthMapFromRegistry();
                CacheExistingClients();
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
            else if (IsClient && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            if (!IsServer)
            {
                SubscribeToNetworkState();
                StartCoroutine(DeferredApplyNetworkState());
            }
            else
            {
                SyncStateToNetwork();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            else if (IsClient && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            UnsubscribeFromNetworkState();
            base.OnNetworkDespawn();
        }

        private void TrySpawnOnServer()
        {
            if (IsSpawned)
            {
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            var networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && !networkObject.IsSpawned)
            {
                networkObject.Spawn();
            }
        }

        private void Update()
        {
        }

        private void InitializeSessionIfNeeded()
        {
            if (_session != null)
            {
                return;
            }

            if (gridMapper == null || gameConfig == null)
            {
                return;
            }

            var inventories = BuildInventories(gameConfig.PlayerOnePieceSet, gameConfig.PlayerTwoPieceSet);
            var pieceSizes = BuildPieceSizes(gameConfig.PlayerOnePieceSet, gameConfig.PlayerTwoPieceSet, gameConfig.CathedralPiece);
            _session = new GameSession(new BoardSize(gridMapper.Width, gridMapper.Height), gameConfig.CathedralPiece.Id, inventories, pieceSizes);
            _piecesById = BuildPieceLookup(gameConfig);
            _pieceIdByHash = BuildPieceHashLookup(gameConfig);
        }

        public bool IsLocalPlayerTurn()
        {
            if (_session == null)
            {
                return false;
            }

            if (_session.IsGameOver)
            {
                return false;
            }

            return _session.CurrentPlayerId == LocalPlayerId;
        }

        public void RequestPlacePiece(string pieceId, Cell origin, Rotation rotation)
        {
            if (_session == null)
            {
                InitializeSessionIfNeeded();
            }

            if (!IsSpawned)
            {
                Debug.LogWarning("NetworkGameController is not spawned yet. Ignoring placement request.");
                return;
            }

            if (IsServer)
            {
                if (!CanPlayerAct(LocalPlayerId))
                {
                    RejectAction(NetworkManager.ServerClientId, "Not your turn.");
                    return;
                }

                ApplyPlacement(pieceId, origin, rotation, isFromNetwork: false);
                PlacePieceClientRpc(pieceId, origin.X, origin.Y, (int)rotation);
                return;
            }

            PlacePieceServerRpc(pieceId, origin.X, origin.Y, (int)rotation);
        }

        public void RequestFinishRound()
        {
            if (_session == null)
            {
                InitializeSessionIfNeeded();
            }

            if (!IsSpawned)
            {
                Debug.LogWarning("NetworkGameController is not spawned yet. Ignoring finish round request.");
                return;
            }

            if (IsServer)
            {
                if (!CanPlayerAct(LocalPlayerId))
                {
                    RejectAction(NetworkManager.ServerClientId, "Not your turn.");
                    return;
                }

                ApplyFinishRound(isFromNetwork: false);
                FinishRoundClientRpc();
                return;
            }

            FinishRoundServerRpc();
        }

        public void RequestSurrender()
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("NetworkGameController is not spawned yet. Ignoring surrender request.");
                return;
            }

            if (IsServer)
            {
                ResolveSurrender(LocalPlayerId);
                return;
            }

            SurrenderServerRpc();
        }

        public void RequestResync()
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("NetworkGameController is not spawned yet. Ignoring resync request.");
                return;
            }

            // No-op in full Netcode replication mode.
        }

        public void NotifyLocalInput()
        {
            // No-op in full Netcode replication mode.
        }

        [ServerRpc(RequireOwnership = false)]
        private void PlacePieceServerRpc(string pieceId, int x, int y, int rotation, ServerRpcParams rpcParams = default)
        {
            InitializeSessionIfNeeded();
            if (_session == null)
            {
                return;
            }

            var senderId = ResolvePlayerId(rpcParams.Receive.SenderClientId);
            Debug.Log($"[NetState] PlacePieceServerRpc sender={rpcParams.Receive.SenderClientId} playerId={senderId} piece={pieceId} at {x},{y} rot={rotation}");
            if (!CanPlayerAct(senderId))
            {
                RejectAction(rpcParams.Receive.SenderClientId, "Not your turn.");
                return;
            }

            if (!ApplyPlacement(pieceId, new Cell(x, y), (Rotation)rotation, isFromNetwork: false))
            {
                return;
            }

            PlacePieceClientRpc(pieceId, x, y, rotation);
        }

        [ClientRpc]
        private void PlacePieceClientRpc(string pieceId, int x, int y, int rotation)
        {
            if (IsServer)
            {
                return;
            }

            // No-op; clients are driven by network state replication.
        }

        [ServerRpc(RequireOwnership = false)]
        private void FinishRoundServerRpc(ServerRpcParams rpcParams = default)
        {
            InitializeSessionIfNeeded();
            if (_session == null)
            {
                return;
            }

            var senderId = ResolvePlayerId(rpcParams.Receive.SenderClientId);
            if (!CanPlayerAct(senderId))
            {
                RejectAction(rpcParams.Receive.SenderClientId, "Not your turn.");
                return;
            }

            ApplyFinishRound(isFromNetwork: false);
            FinishRoundClientRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestResyncServerRpc(ServerRpcParams rpcParams = default)
        {
            // No-op in full Netcode replication mode.
        }

        [ServerRpc(RequireOwnership = false)]
        private void SurrenderServerRpc(ServerRpcParams rpcParams = default)
        {
            var senderId = ResolvePlayerId(rpcParams.Receive.SenderClientId);
            ResolveSurrender(senderId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SurrenderAckServerRpc(ServerRpcParams rpcParams = default)
        {
            _surrenderAckReceived = true;
        }

        [ClientRpc]
        private void FinishRoundClientRpc()
        {
            if (IsServer)
            {
                return;
            }

            // No-op; clients are driven by network state replication.
        }

        [ClientRpc]
        private void ApplySurrenderClientRpc(int surrenderingPlayerId, int winningPlayerId)
        {
            if (!IsServer)
            {
                // No-op; clients are driven by network state replication.
                SurrenderAckServerRpc();
            }

            SurrenderResolved?.Invoke(surrenderingPlayerId, winningPlayerId);
        }

        private bool ApplyPlacement(string pieceId, Cell origin, Rotation rotation, bool isFromNetwork)
        {
            if (_session == null)
            {
                return false;
            }

            if (_piecesById == null || !_piecesById.TryGetValue(pieceId, out var pieceAsset) || pieceAsset == null)
            {
                Debug.LogWarning($"Unknown piece id '{pieceId}'.");
                return false;
            }

            var definition = pieceAsset.ToDomainDefinition();
            if (!_session.TryPlacePiece(definition, pieceId, origin, rotation, out _, out _, out _, out _, out _))
            {
                if (isFromNetwork)
                {
                    Debug.LogWarning($"Client placement desync for '{pieceId}' at {origin}.");
                    RequestResync();
                }
                return false;
            }

            SessionUpdated?.Invoke();
            if (IsServer)
            {
                SyncStateToNetwork();
            }
            return true;
        }

        private void ApplyFinishRound(bool isFromNetwork)
        {
            if (_session == null)
            {
                return;
            }

            _session.FinishRoundForCurrentPlayer();
            SessionUpdated?.Invoke();
            if (IsServer)
            {
                SyncStateToNetwork();
            }
        }

        private void ResolveSurrender(int surrenderingPlayerId)
        {
            InitializeSessionIfNeeded();
            if (_session == null || _session.IsGameOver)
            {
                return;
            }

            _surrenderAckReceived = false;
            _session.Surrender(surrenderingPlayerId);
            SessionUpdated?.Invoke();

            var winningPlayerId = _session.WinningPlayerId;
            ApplySurrenderClientRpc(surrenderingPlayerId, winningPlayerId);
            SyncStateToNetwork();
        }

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton != null && clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            if (IsServer && _session != null && _session.IsGameOver)
            {
                ResetMatchState();
            }

            if (s_clientToAuthId.TryGetValue(clientId, out var authId))
            {
                AssignPlayerIdByAuth(authId);
                var assigned = ResolvePlayerIdFromAuth(authId);
                if (assigned >= 0)
                {
                    s_clientToPlayerId[clientId] = assigned;
                    SendAssignedPlayerId(clientId, assigned);
                }
            }
        }

        private void SendAssignedPlayerId(ulong clientId, int playerId)
        {
            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };

            AssignPlayerIdClientRpc(playerId, clientParams);
        }

        [ClientRpc]
        private void AssignPlayerIdClientRpc(int playerId, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
            {
                return;
            }

            _localPlayerIdOverride = playerId;
            SessionUpdated?.Invoke();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
                {
                    var reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : string.Empty;
                    Debug.LogWarning($"[NetworkGameController] Disconnected from server. Reason='{reason}'");
                    HandleClientDisconnected();
                }
                return;
            }

            s_clientToPlayerId.Remove(clientId);
            if (!s_clientToAuthId.TryGetValue(clientId, out var authId))
            {
                if (IsServer && _session != null && _session.IsGameOver && GetActiveClientCount() == 0)
                {
                    ResetMatchState();
                }
                return;
            }

            s_clientToAuthId.Remove(clientId);

            if (IsServer && _session != null && _session.IsGameOver && GetActiveClientCount() == 0)
            {
                ResetMatchState();
            }
        }

        private void HandleClientDisconnected()
        {
            _localPlayerIdOverride = -1;
            _session = null;
            _piecesById = null;
            _pieceIdByHash = null;

            if (!string.IsNullOrWhiteSpace(disconnectSceneName))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(disconnectSceneName);
            }
        }

        private int GetActiveClientCount()
        {
            if (NetworkManager.Singleton == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private void ResetMatchState()
        {
            s_clientToPlayerId.Clear();
            s_clientToAuthId.Clear();
            s_authToPlayerId.Clear();
            _localPlayerIdOverride = -1;
            _session = null;
            InitializeSessionIfNeeded();
            if (IsServer)
            {
                SyncStateToNetwork();
            }
        }


        // Snapshot-based sync removed in favor of NetworkVariables/NetworkLists.

        private bool CanPlayerAct(int playerId)
        {
            if (_session == null)
            {
                return false;
            }

            if (_session.IsGameOver)
            {
                return false;
            }

            if (playerId < 0)
            {
                return false;
            }

            return _session.CurrentPlayerId == playerId;
        }

        private int ResolvePlayerId(ulong clientId)
        {
            if (IsServer)
            {
                if (s_clientToAuthId.TryGetValue(clientId, out var authId))
                {
                    var resolved = ResolvePlayerIdFromAuth(authId);
                    if (resolved >= 0)
                    {
                        return resolved;
                    }
                }

                if (s_clientToPlayerId.TryGetValue(clientId, out var playerId))
                {
                    return playerId;
                }

                return clientId == NetworkManager.ServerClientId ? 0 : 1;
            }

            if (NetworkManager.Singleton == null)
            {
                return 0;
            }

            if (_localPlayerIdOverride >= 0)
            {
                return _localPlayerIdOverride;
            }

            var mapped = ResolveLocalPlayerIdFromAuthMap();
            if (mapped >= 0)
            {
                _localPlayerIdOverride = mapped;
                return mapped;
            }

            var localAuthId = GetLocalAuthId();
            var authResolved = ResolvePlayerIdFromAuth(localAuthId);
            if (authResolved >= 0)
            {
                return authResolved;
            }

            if (IsMatchLocked() && s_authToPlayerId.Count >= 2)
            {
                return -1;
            }

            return NetworkManager.Singleton.LocalClientId == NetworkManager.ServerClientId ? 0 : 1;
        }

        private void CacheExistingClients()
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                var assigned = AssignPlayerId(clientId);
                SendAssignedPlayerId(clientId, assigned);
            }
        }

        public static void ConfigureConnectionApproval(NetworkManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = ApprovalCheckStatic;
        }

        private static void ApprovalCheckStatic(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var authId = request.Payload != null && request.Payload.Length > 0
                ? Encoding.UTF8.GetString(request.Payload)
                : string.Empty;
            var authPreview = string.IsNullOrWhiteSpace(authId)
                ? "<empty>"
                : (authId.Length <= 8 ? authId : authId.Substring(0, 8));

            if (string.IsNullOrWhiteSpace(authId))
            {
                response.Approved = false;
                response.Reason = "Missing credentials.";
                Debug.LogWarning($"[Approval] Rejected client={request.ClientNetworkId} reason='{response.Reason}' auth={authPreview}");
                return;
            }

            if (IsAuthAlreadyConnectedStatic(authId, request.ClientNetworkId))
            {
                response.Approved = false;
                response.Reason = "Player already connected.";
                Debug.LogWarning($"[Approval] Rejected client={request.ClientNetworkId} reason='{response.Reason}' auth={authPreview}");
                return;
            }

            var locked = _instance != null && _instance.IsMatchLocked();
            if (locked && !s_authToPlayerId.ContainsKey(authId) && s_authToPlayerId.Count >= 2)
            {
                response.Approved = false;
                response.Reason = "Match already started. Rejoin with original credentials.";
                Debug.LogWarning($"[Approval] Rejected client={request.ClientNetworkId} reason='{response.Reason}' auth={authPreview}");
                return;
            }

            s_clientToAuthId[request.ClientNetworkId] = authId;
            if (_instance != null)
            {
                _instance.AssignPlayerIdByAuth(authId);
            }
            else if (!s_authToPlayerId.ContainsKey(authId) && s_authToPlayerId.Count < 2)
            {
                var fallback = s_authToPlayerId.ContainsValue(0) ? 1 : 0;
                s_authToPlayerId[authId] = fallback;
            }

            var assigned = _instance != null
                ? _instance.ResolvePlayerIdFromAuth(authId)
                : (s_authToPlayerId.TryGetValue(authId, out var mapped) ? mapped : -1);
            if (assigned >= 0)
            {
                s_clientToPlayerId[request.ClientNetworkId] = assigned;
                _instance?.UpdateAuthMap(authId, assigned);
            }

            response.Approved = true;
            response.CreatePlayerObject = false;
            response.Pending = false;
            Debug.Log($"[Approval] Approved client={request.ClientNetworkId} auth={authPreview} assigned={assigned}");
        }

        private int AssignPlayerId(ulong clientId)
        {
            if (s_clientToPlayerId.ContainsKey(clientId))
            {
                return s_clientToPlayerId[clientId];
            }

            var manager = NetworkManager.Singleton;
            var isDedicatedServer = manager != null && manager.IsServer && !manager.IsClient;
            int playerId;
            if (isDedicatedServer)
            {
                playerId = s_clientToPlayerId.ContainsValue(0) ? 1 : 0;
            }
            else
            {
                playerId = clientId == NetworkManager.ServerClientId ? 0 : 1;
            }
            s_clientToPlayerId[clientId] = playerId;
            return playerId;
        }

        private void AssignPlayerIdByAuth(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return;
            }

            if (s_authToPlayerId.ContainsKey(authId))
            {
                return;
            }

            if (IsMatchLocked() && s_authToPlayerId.Count >= 2)
            {
                return;
            }

            var resolved = ResolvePlayerIdFromAuth(authId);
            if (resolved >= 0)
            {
                s_authToPlayerId[authId] = resolved;
                UpdateAuthMap(authId, resolved);
                return;
            }

            if (!HasFreePlayerSlot())
            {
                return;
            }

            var assigned = s_authToPlayerId.ContainsValue(0) ? 1 : 0;
            s_authToPlayerId[authId] = assigned;
            UpdateAuthMap(authId, assigned);
        }

        private int ResolvePlayerIdFromAuth(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return -1;
            }

            if (s_authToPlayerId.TryGetValue(authId, out var existing))
            {
                return existing;
            }

            return -1;
        }

        private bool IsMatchLocked()
        {
            if (_session == null)
            {
                return false;
            }

            if (_session.IsGameOver)
            {
                return false;
            }

            if (_session.PlacedPieces != null && _session.PlacedPieces.Count > 0)
            {
                return true;
            }

            return _session.CurrentRound > 1;
        }

        private bool HasFreePlayerSlot()
        {
            return !s_authToPlayerId.ContainsValue(0) || !s_authToPlayerId.ContainsValue(1);
        }

        private void UpdateAuthMap(string authId, int playerId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(authId))
            {
                return;
            }

            InitializeNetworkLists();
            var hash = ComputeStableHash(authId);
            for (var i = 0; i < _authPlayerMap.Count; i++)
            {
                if (_authPlayerMap[i].AuthHash == hash)
                {
                    _authPlayerMap[i] = new AuthPlayerNet { AuthHash = hash, PlayerId = playerId };
                    return;
                }
            }

            _authPlayerMap.Add(new AuthPlayerNet { AuthHash = hash, PlayerId = playerId });
        }

        private void SyncAuthMapFromRegistry()
        {
            if (!IsServer || _authPlayerMap == null)
            {
                return;
            }

            foreach (var pair in s_authToPlayerId)
            {
                UpdateAuthMap(pair.Key, pair.Value);
            }
        }

        private int ResolveLocalPlayerIdFromAuthMap()
        {
            if (identityProvider == null)
            {
                identityProvider = FindObjectOfType<PlayerIdentityProvider>();
            }

            if (identityProvider == null)
            {
                return -1;
            }

            var authId = identityProvider.PlayerId;
            if (string.IsNullOrWhiteSpace(authId))
            {
                return -1;
            }

            var hash = ComputeStableHash(authId);
            for (var i = 0; i < _authPlayerMap.Count; i++)
            {
                if (_authPlayerMap[i].AuthHash == hash)
                {
                    return _authPlayerMap[i].PlayerId;
                }
            }

            return -1;
        }

        private void UpdateLocalPlayerIdFromAuthMap()
        {
            var mapped = ResolveLocalPlayerIdFromAuthMap();
            if (mapped >= 0)
            {
                _localPlayerIdOverride = mapped;
            }
        }

        private bool IsAuthAlreadyConnected(string authId, ulong clientId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return false;
            }

            foreach (var pair in s_clientToAuthId)
            {
                if (pair.Key == clientId)
                {
                    continue;
                }

                if (string.Equals(pair.Value, authId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAuthAlreadyConnectedStatic(string authId, ulong clientId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return false;
            }

            foreach (var pair in s_clientToAuthId)
            {
                if (pair.Key == clientId)
                {
                    continue;
                }

                if (string.Equals(pair.Value, authId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetLocalAuthId()
        {
            if (!TryGetLocalAuthId(out var authId))
            {
                return string.Empty;
            }

            return authId;
        }

        private bool TryGetLocalAuthId(out string authId)
        {
            authId = string.Empty;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
            {
                return false;
            }

            if (identityProvider == null)
            {
                identityProvider = FindObjectOfType<PlayerIdentityProvider>();
            }

            if (identityProvider == null)
            {
                return false;
            }

            authId = identityProvider.PlayerId ?? string.Empty;
            return !string.IsNullOrWhiteSpace(authId);
        }

        private void RejectAction(ulong clientId, string reason)
        {
            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };

            NotifyActionRejectedClientRpc(reason, clientParams);
        }

        [ClientRpc]
        private void NotifyActionRejectedClientRpc(string reason, ClientRpcParams rpcParams = default)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "Action rejected by server.";
            }

            Debug.LogWarning(reason);
        }

        private void InitializeNetworkLists()
        {
            // Lists are initialized at declaration to ensure Netcode syncs correctly.
        }

        private bool _networkSubscribed;

        private void SubscribeToNetworkState()
        {
            if (_networkSubscribed)
            {
                return;
            }

            _stateVersion.OnValueChanged += OnNetworkStateVersionChanged;
            _currentPlayerId.OnValueChanged += OnNetworkStateValueChanged;
            _currentRound.OnValueChanged += OnNetworkStateValueChanged;
            _phase.OnValueChanged += OnNetworkStateValueChanged;
            _roundRevision.OnValueChanged += OnNetworkStateValueChanged;
            _isGameOver.OnValueChanged += OnNetworkStateValueChanged;
            _wasSurrendered.OnValueChanged += OnNetworkStateValueChanged;
            _surrenderingPlayerId.OnValueChanged += OnNetworkStateValueChanged;
            _winningPlayerId.OnValueChanged += OnNetworkStateValueChanged;

            _placedPieceMeta.OnListChanged += OnNetworkListChanged;
            _placedPieceCells.OnListChanged += OnNetworkListChanged;
            _inventoryEntries.OnListChanged += OnNetworkListChanged;
            _territoryCells.OnListChanged += OnNetworkListChanged;
            _playerScores.OnListChanged += OnNetworkListChanged;
            _playerFinished.OnListChanged += OnNetworkListChanged;
            _authPlayerMap.OnListChanged += OnAuthMapChanged;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSynchronize += OnClientSynchronized;
            }
            _networkSubscribed = true;
        }

        private void UnsubscribeFromNetworkState()
        {
            if (!_networkSubscribed)
            {
                return;
            }

            _stateVersion.OnValueChanged -= OnNetworkStateVersionChanged;
            _currentPlayerId.OnValueChanged -= OnNetworkStateValueChanged;
            _currentRound.OnValueChanged -= OnNetworkStateValueChanged;
            _phase.OnValueChanged -= OnNetworkStateValueChanged;
            _roundRevision.OnValueChanged -= OnNetworkStateValueChanged;
            _isGameOver.OnValueChanged -= OnNetworkStateValueChanged;
            _wasSurrendered.OnValueChanged -= OnNetworkStateValueChanged;
            _surrenderingPlayerId.OnValueChanged -= OnNetworkStateValueChanged;
            _winningPlayerId.OnValueChanged -= OnNetworkStateValueChanged;

            _placedPieceMeta.OnListChanged -= OnNetworkListChanged;
            _placedPieceCells.OnListChanged -= OnNetworkListChanged;
            _inventoryEntries.OnListChanged -= OnNetworkListChanged;
            _territoryCells.OnListChanged -= OnNetworkListChanged;
            _playerScores.OnListChanged -= OnNetworkListChanged;
            _playerFinished.OnListChanged -= OnNetworkListChanged;
            _authPlayerMap.OnListChanged -= OnAuthMapChanged;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSynchronize -= OnClientSynchronized;
            }
            _networkSubscribed = false;
        }

        private void OnNetworkStateVersionChanged(int previous, int current)
        {
            ApplyNetworkStateToSession();
        }

        private void OnNetworkStateValueChanged<T>(T previous, T current) where T : struct
        {
            ApplyNetworkStateToSession();
        }

        private void OnNetworkListChanged<T>(NetworkListEvent<T> changeEvent) where T : unmanaged, IEquatable<T>
        {
            ApplyNetworkStateToSession();
        }

        private void OnAuthMapChanged(NetworkListEvent<AuthPlayerNet> changeEvent)
        {
            UpdateLocalPlayerIdFromAuthMap();
        }

        private void OnClientSynchronized(ulong clientId)
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            if (clientId != NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            ApplyNetworkStateToSession();
        }

        private System.Collections.IEnumerator DeferredApplyNetworkState()
        {
            yield return null;
            ApplyNetworkStateToSession();
        }

        private void SyncStateToNetwork()
        {
            if (!IsServer)
            {
                return;
            }

            if (_session == null)
            {
                return;
            }

            InitializeNetworkLists();
            var snapshot = _session.BuildSnapshot();
            Debug.Log($"[NetState] SyncStateToNetwork placed={snapshot.PlacedPieces?.Count ?? 0} phase={(int)snapshot.Phase} round={snapshot.CurrentRound} currentPlayer={snapshot.CurrentPlayerId}");

            _currentPlayerId.Value = snapshot.CurrentPlayerId;
            _currentRound.Value = snapshot.CurrentRound;
            _phase.Value = (int)snapshot.Phase;
            _roundRevision.Value = snapshot.RoundRevision;
            _isGameOver.Value = snapshot.IsGameOver;
            _wasSurrendered.Value = snapshot.WasSurrendered;
            _surrenderingPlayerId.Value = snapshot.SurrenderingPlayerId;
            _winningPlayerId.Value = snapshot.WinningPlayerId;

            _placedPieceMeta.Clear();
            _placedPieceCells.Clear();
            if (snapshot.PlacedPieces != null)
            {
                foreach (var piece in snapshot.PlacedPieces)
                {
                    if (piece == null)
                    {
                        continue;
                    }

                    _placedPieceMeta.Add(new PlacedPieceMetaNet
                    {
                        InstanceId = piece.InstanceId,
                        PlayerId = piece.PlayerId,
                        PieceIdHash = ComputeStableHash(piece.PieceId),
                        IsCathedral = piece.IsCathedral
                    });

                    if (piece.Cells != null)
                    {
                        foreach (var cell in piece.Cells)
                        {
                            _placedPieceCells.Add(new PlacedPieceCellNet
                            {
                                InstanceId = piece.InstanceId,
                                X = cell.X,
                                Y = cell.Y
                            });
                        }
                    }
                }
            }

            _inventoryEntries.Clear();
            if (snapshot.Inventories != null)
            {
                for (var playerId = 0; playerId < snapshot.Inventories.Length; playerId++)
                {
                    var inventory = snapshot.Inventories[playerId];
                    if (inventory == null)
                    {
                        continue;
                    }

                    foreach (var entry in inventory)
                    {
                        _inventoryEntries.Add(new InventoryEntryNet
                        {
                            PlayerId = playerId,
                            PieceIdHash = ComputeStableHash(entry.Key),
                            Count = entry.Value
                        });
                    }
                }
            }

            _territoryCells.Clear();
            if (snapshot.ClaimedTerritories != null)
            {
                for (var playerId = 0; playerId < snapshot.ClaimedTerritories.Length; playerId++)
                {
                    var cells = snapshot.ClaimedTerritories[playerId];
                    if (cells == null)
                    {
                        continue;
                    }

                    foreach (var cell in cells)
                    {
                        _territoryCells.Add(new TerritoryCellNet
                        {
                            PlayerId = playerId,
                            X = cell.X,
                            Y = cell.Y
                        });
                    }
                }
            }

            _playerScores.Clear();
            if (snapshot.TotalScores != null)
            {
                for (var playerId = 0; playerId < snapshot.TotalScores.Length; playerId++)
                {
                    _playerScores.Add(new PlayerScoreNet
                    {
                        PlayerId = playerId,
                        Score = snapshot.TotalScores[playerId]
                    });
                }
            }

            _playerFinished.Clear();
            if (snapshot.FinishedThisRound != null)
            {
                for (var playerId = 0; playerId < snapshot.FinishedThisRound.Length; playerId++)
                {
                    _playerFinished.Add(new PlayerFinishedNet
                    {
                        PlayerId = playerId,
                        Finished = snapshot.FinishedThisRound[playerId]
                    });
                }
            }

            _stateVersion.Value++;
        }

        private void ApplyNetworkStateToSession()
        {
            if (IsServer && !IsClient)
            {
                return;
            }

            InitializeSessionIfNeeded();
            if (_session == null)
            {
                return;
            }

            var snapshot = BuildSnapshotFromNetwork();
            var firstCells = snapshot.PlacedPieces != null && snapshot.PlacedPieces.Count > 0
                ? (snapshot.PlacedPieces[0].Cells != null ? snapshot.PlacedPieces[0].Cells.Count : 0)
                : 0;
            var cellsCount = _placedPieceCells != null ? _placedPieceCells.Count : 0;
            Debug.Log($"[NetState] ApplyNetworkStateToSession placed={snapshot.PlacedPieces?.Count ?? 0} cellsList={cellsCount} firstCells={firstCells} phase={(int)snapshot.Phase} round={snapshot.CurrentRound} currentPlayer={snapshot.CurrentPlayerId}");
            _session.LoadSnapshot(snapshot);
            SessionUpdated?.Invoke();
        }

        private GameSessionSnapshot BuildSnapshotFromNetwork()
        {
            var snapshot = new GameSessionSnapshot
            {
                CurrentPlayerId = _currentPlayerId.Value,
                Phase = (GamePhase)_phase.Value,
                CurrentRound = _currentRound.Value,
                RoundRevision = _roundRevision.Value,
                IsGameOver = _isGameOver.Value,
                WasSurrendered = _wasSurrendered.Value,
                SurrenderingPlayerId = _surrenderingPlayerId.Value,
                WinningPlayerId = _winningPlayerId.Value
            };

            var playerCount = 2;
            snapshot.Inventories = new Dictionary<string, int>[playerCount];
            snapshot.ClaimedTerritories = new List<Cell>[playerCount];
            snapshot.TotalScores = new int[playerCount];
            snapshot.FinishedThisRound = new bool[playerCount];

            for (var i = 0; i < playerCount; i++)
            {
                snapshot.Inventories[i] = new Dictionary<string, int>();
                snapshot.ClaimedTerritories[i] = new List<Cell>();
            }

            foreach (var entry in _inventoryEntries)
            {
                if (entry.PlayerId < 0 || entry.PlayerId >= playerCount)
                {
                    continue;
                }
                var pieceId = ResolvePieceId(entry.PieceIdHash);
                if (!string.IsNullOrWhiteSpace(pieceId))
                {
                    snapshot.Inventories[entry.PlayerId][pieceId] = entry.Count;
                }
            }

            foreach (var cell in _territoryCells)
            {
                if (cell.PlayerId < 0 || cell.PlayerId >= playerCount)
                {
                    continue;
                }
                snapshot.ClaimedTerritories[cell.PlayerId].Add(new Cell(cell.X, cell.Y));
            }

            foreach (var score in _playerScores)
            {
                if (score.PlayerId < 0 || score.PlayerId >= playerCount)
                {
                    continue;
                }
                snapshot.TotalScores[score.PlayerId] = score.Score;
            }

            foreach (var finished in _playerFinished)
            {
                if (finished.PlayerId < 0 || finished.PlayerId >= playerCount)
                {
                    continue;
                }
                snapshot.FinishedThisRound[finished.PlayerId] = finished.Finished;
            }

            var pieceMap = new Dictionary<int, PlacedPieceSnapshot>();
            foreach (var piece in _placedPieceMeta)
            {
                pieceMap[piece.InstanceId] = new PlacedPieceSnapshot
                {
                    InstanceId = piece.InstanceId,
                    PlayerId = piece.PlayerId,
                    PieceId = ResolvePieceId(piece.PieceIdHash),
                    IsCathedral = piece.IsCathedral,
                    Cells = new List<Cell>()
                };
            }

            foreach (var cell in _placedPieceCells)
            {
                if (!pieceMap.TryGetValue(cell.InstanceId, out var piece))
                {
                    continue;
                }
                piece.Cells.Add(new Cell(cell.X, cell.Y));
            }

            snapshot.PlacedPieces = new List<PlacedPieceSnapshot>(pieceMap.Values);
            return snapshot;
        }

        private static PlayerInventory[] BuildInventories(PieceSetSO playerOneSet, PieceSetSO playerTwoSet)
        {
            var playerOne = new PlayerInventory(BuildCounts(playerOneSet));
            var playerTwo = new PlayerInventory(BuildCounts(playerTwoSet));
            return new[] { playerOne, playerTwo };
        }

        private static Dictionary<string, int> BuildCounts(PieceSetSO set)
        {
            var counts = new Dictionary<string, int>();
            foreach (var entry in set.Entries)
            {
                if (entry.piece == null)
                {
                    continue;
                }

                var count = Mathf.Max(0, entry.count);
                if (counts.ContainsKey(entry.piece.Id))
                {
                    counts[entry.piece.Id] += count;
                }
                else
                {
                    counts.Add(entry.piece.Id, count);
                }
            }

            return counts;
        }

        private static Dictionary<string, int> BuildPieceSizes(PieceSetSO playerOneSet, PieceSetSO playerTwoSet, PieceDefinitionSO cathedral)
        {
            var sizes = new Dictionary<string, int>();
            AddPieceSizes(sizes, playerOneSet);
            AddPieceSizes(sizes, playerTwoSet);
            if (cathedral != null && !sizes.ContainsKey(cathedral.Id))
            {
                sizes[cathedral.Id] = cathedral.Cells.Count;
            }
            return sizes;
        }

        private static void AddPieceSizes(Dictionary<string, int> sizes, PieceSetSO set)
        {
            foreach (var entry in set.Entries)
            {
                if (entry.piece == null)
                {
                    continue;
                }

                if (!sizes.ContainsKey(entry.piece.Id))
                {
                    sizes[entry.piece.Id] = entry.piece.Cells.Count;
                }
            }
        }

        private static Dictionary<string, PieceDefinitionSO> BuildPieceLookup(GameConfigSO config)
        {
            var lookup = new Dictionary<string, PieceDefinitionSO>();
            if (config == null)
            {
                return lookup;
            }

            AddPieces(lookup, config.PlayerOnePieceSet);
            AddPieces(lookup, config.PlayerTwoPieceSet);

            if (config.CathedralPiece != null && !lookup.ContainsKey(config.CathedralPiece.Id))
            {
                lookup[config.CathedralPiece.Id] = config.CathedralPiece;
            }

            return lookup;
        }

        private static Dictionary<int, string> BuildPieceHashLookup(GameConfigSO config)
        {
            var lookup = new Dictionary<int, string>();
            if (config == null)
            {
                return lookup;
            }

            AddPieceHashes(lookup, config.PlayerOnePieceSet);
            AddPieceHashes(lookup, config.PlayerTwoPieceSet);

            if (config.CathedralPiece != null)
            {
                var hash = ComputeStableHash(config.CathedralPiece.Id);
                if (!lookup.ContainsKey(hash))
                {
                    lookup[hash] = config.CathedralPiece.Id;
                }
            }

            return lookup;
        }

        private static void AddPieceHashes(Dictionary<int, string> lookup, PieceSetSO set)
        {
            if (set == null)
            {
                return;
            }

            foreach (var entry in set.Entries)
            {
                if (entry.piece == null)
                {
                    continue;
                }

                var hash = ComputeStableHash(entry.piece.Id);
                if (!lookup.ContainsKey(hash))
                {
                    lookup[hash] = entry.piece.Id;
                }
            }
        }

        private static int ComputeStableHash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            unchecked
            {
                var hash = (int)2166136261;
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
                return hash;
            }
        }

        private string ResolvePieceId(int hash)
        {
            if (hash == 0)
            {
                return string.Empty;
            }

            if (_pieceIdByHash == null || _pieceIdByHash.Count == 0)
            {
                if (gameConfig != null)
                {
                    _pieceIdByHash = BuildPieceHashLookup(gameConfig);
                }
            }

            if (_pieceIdByHash != null && _pieceIdByHash.TryGetValue(hash, out var id))
            {
                return id;
            }

            return string.Empty;
        }

        private static void AddPieces(Dictionary<string, PieceDefinitionSO> lookup, PieceSetSO set)
        {
            if (set == null)
            {
                return;
            }

            foreach (var entry in set.Entries)
            {
                if (entry.piece == null)
                {
                    continue;
                }

                if (!lookup.ContainsKey(entry.piece.Id))
                {
                    lookup[entry.piece.Id] = entry.piece;
                }
            }
        }

        private struct PlacedPieceMetaNet : INetworkSerializable, IEquatable<PlacedPieceMetaNet>
        {
            public int InstanceId;
            public int PlayerId;
            public int PieceIdHash;
            public bool IsCathedral;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref InstanceId);
                serializer.SerializeValue(ref PlayerId);
                serializer.SerializeValue(ref PieceIdHash);
                serializer.SerializeValue(ref IsCathedral);
            }

            public bool Equals(PlacedPieceMetaNet other)
            {
                return InstanceId == other.InstanceId &&
                       PlayerId == other.PlayerId &&
                       PieceIdHash == other.PieceIdHash &&
                       IsCathedral == other.IsCathedral;
            }
        }

        private struct PlacedPieceCellNet : INetworkSerializable, IEquatable<PlacedPieceCellNet>
        {
            public int InstanceId;
            public int X;
            public int Y;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref InstanceId);
                serializer.SerializeValue(ref X);
                serializer.SerializeValue(ref Y);
            }

            public bool Equals(PlacedPieceCellNet other)
            {
                return InstanceId == other.InstanceId && X == other.X && Y == other.Y;
            }
        }

        private struct InventoryEntryNet : INetworkSerializable, IEquatable<InventoryEntryNet>
        {
            public int PlayerId;
            public int PieceIdHash;
            public int Count;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref PlayerId);
                serializer.SerializeValue(ref PieceIdHash);
                serializer.SerializeValue(ref Count);
            }

            public bool Equals(InventoryEntryNet other)
            {
                return PlayerId == other.PlayerId &&
                       PieceIdHash == other.PieceIdHash &&
                       Count == other.Count;
            }
        }

        private struct TerritoryCellNet : INetworkSerializable, IEquatable<TerritoryCellNet>
        {
            public int PlayerId;
            public int X;
            public int Y;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref PlayerId);
                serializer.SerializeValue(ref X);
                serializer.SerializeValue(ref Y);
            }

            public bool Equals(TerritoryCellNet other)
            {
                return PlayerId == other.PlayerId && X == other.X && Y == other.Y;
            }
        }

        private struct PlayerScoreNet : INetworkSerializable, IEquatable<PlayerScoreNet>
        {
            public int PlayerId;
            public int Score;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref PlayerId);
                serializer.SerializeValue(ref Score);
            }

            public bool Equals(PlayerScoreNet other)
            {
                return PlayerId == other.PlayerId && Score == other.Score;
            }
        }

        private struct PlayerFinishedNet : INetworkSerializable, IEquatable<PlayerFinishedNet>
        {
            public int PlayerId;
            public bool Finished;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref PlayerId);
                serializer.SerializeValue(ref Finished);
            }

            public bool Equals(PlayerFinishedNet other)
            {
                return PlayerId == other.PlayerId && Finished == other.Finished;
            }
        }

        private struct AuthPlayerNet : INetworkSerializable, IEquatable<AuthPlayerNet>
        {
            public int AuthHash;
            public int PlayerId;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref AuthHash);
                serializer.SerializeValue(ref PlayerId);
            }

            public bool Equals(AuthPlayerNet other)
            {
                return AuthHash == other.AuthHash && PlayerId == other.PlayerId;
            }
        }

        // Session removal hooks removed (direct-connection flow).
    }
}
