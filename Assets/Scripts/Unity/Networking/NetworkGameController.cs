using System;
using System.Collections.Generic;
using System.IO;
using Peribind.Application.Sessions;
using Peribind.Domain.Board;
using Peribind.Domain.Pieces;
using Peribind.Unity.Board;
using Peribind.Unity.ScriptableObjects;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class NetworkGameController : NetworkBehaviour
    {
        [SerializeField] private GridMapper gridMapper;
        [SerializeField] private GameConfigSO gameConfig;

        [SerializeField] private MultiplayerSessionController sessionController;
        private GameSession _session;
        private Dictionary<string, PieceDefinitionSO> _piecesById;
        private readonly Dictionary<ulong, int> _clientToPlayerId = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> _clientToAuthId = new Dictionary<ulong, string>();
        private readonly Dictionary<string, int> _authToPlayerId = new Dictionary<string, int>();
        private bool _localAuthRegistered;
        [SerializeField] private float autoResyncIntervalSeconds = 10f;
        [SerializeField] private bool resyncOnlyWhenIdle = true;
        [SerializeField] private float idleSecondsBeforeResync = 8f;
        private float _nextResyncTime;
        private float _lastInputTime;

        public GameSession Session => _session;
        public event Action SessionUpdated;

        public int LocalPlayerId => ResolvePlayerId(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0);

        private void Awake()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>();
            }

            InitializeSessionIfNeeded();
        }

        private void Start()
        {
            TrySpawnOnServer();
            ScheduleNextResync();
            _lastInputTime = Time.unscaledTime;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer && NetworkManager.Singleton != null)
            {
                CacheExistingClients();
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            RegisterLocalPlayer();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }

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
            if (!IsClient || IsServer)
            {
                return;
            }

            TryRegisterLocalPlayer();

            if (autoResyncIntervalSeconds <= 0f || !IsSpawned)
            {
                return;
            }

            if (Time.unscaledTime < _nextResyncTime)
            {
                return;
            }

            if (resyncOnlyWhenIdle && Time.unscaledTime - _lastInputTime < Mathf.Max(0f, idleSecondsBeforeResync))
            {
                return;
            }

            _nextResyncTime = Time.unscaledTime + autoResyncIntervalSeconds;
            RequestResync();
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

        public void RequestResync()
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("NetworkGameController is not spawned yet. Ignoring resync request.");
                return;
            }

            if (IsServer)
            {
                SendSnapshotToClient(NetworkManager.ServerClientId);
                return;
            }

            RequestResyncServerRpc();
        }

        public void NotifyLocalInput()
        {
            _lastInputTime = Time.unscaledTime;
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

            InitializeSessionIfNeeded();
            ApplyPlacement(pieceId, new Cell(x, y), (Rotation)rotation, isFromNetwork: true);
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
            SendSnapshotToClient(rpcParams.Receive.SenderClientId);
        }

        [ClientRpc]
        private void FinishRoundClientRpc()
        {
            if (IsServer)
            {
                return;
            }

            InitializeSessionIfNeeded();
            ApplyFinishRound(isFromNetwork: true);
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
        }

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton != null && clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            AssignPlayerId(clientId);
            SendSnapshotToClient(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            _clientToPlayerId.Remove(clientId);
            if (!_clientToAuthId.TryGetValue(clientId, out var authId))
            {
                return;
            }

            _clientToAuthId.Remove(clientId);
            _ = TryRemoveSessionPlayerAsync(authId);
        }

        private void ScheduleNextResync()
        {
            if (autoResyncIntervalSeconds <= 0f)
            {
                _nextResyncTime = float.MaxValue;
                return;
            }

            _nextResyncTime = Time.unscaledTime + autoResyncIntervalSeconds;
        }

        private void SendSnapshotToClient(ulong clientId)
        {
            if (_session == null)
            {
                InitializeSessionIfNeeded();
            }

            if (_session == null)
            {
                return;
            }

            var snapshot = _session.BuildSnapshot();
            var payload = SerializeSnapshot(snapshot);
            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };

            ReceiveSnapshotClientRpc(payload, clientParams);
        }

        [ClientRpc]
        private void ReceiveSnapshotClientRpc(byte[] payload, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
            {
                return;
            }

            if (payload == null || payload.Length == 0)
            {
                return;
            }

            InitializeSessionIfNeeded();
            var snapshot = DeserializeSnapshot(payload);
            if (snapshot == null)
            {
                return;
            }

            _session.LoadSnapshot(snapshot);
            SessionUpdated?.Invoke();
        }

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

            return _session.CurrentPlayerId == playerId;
        }

        private int ResolvePlayerId(ulong clientId)
        {
            if (IsServer)
            {
                if (_clientToAuthId.TryGetValue(clientId, out var authId))
                {
                    var resolved = ResolvePlayerIdFromAuth(authId);
                    if (resolved >= 0)
                    {
                        return resolved;
                    }
                }

                if (_clientToPlayerId.TryGetValue(clientId, out var playerId))
                {
                    return playerId;
                }

                return clientId == NetworkManager.ServerClientId ? 0 : 1;
            }

            if (NetworkManager.Singleton == null)
            {
                return 0;
            }

            var localAuthId = GetLocalAuthId();
            var authResolved = ResolvePlayerIdFromAuth(localAuthId);
            if (authResolved >= 0)
            {
                return authResolved;
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
                AssignPlayerId(clientId);
            }
        }

        private void RegisterLocalPlayer()
        {
            if (!IsClient)
            {
                return;
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                return;
            }

            var authId = AuthenticationService.Instance.PlayerId;
            if (string.IsNullOrWhiteSpace(authId))
            {
                return;
            }

            _localAuthRegistered = true;

            if (IsServer)
            {
                _clientToAuthId[NetworkManager.ServerClientId] = authId;
                AssignPlayerIdByAuth(authId);
                return;
            }

            RegisterPlayerServerRpc(authId);
        }

        private void TryRegisterLocalPlayer()
        {
            if (_localAuthRegistered)
            {
                return;
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                return;
            }

            RegisterLocalPlayer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RegisterPlayerServerRpc(string authPlayerId, ServerRpcParams rpcParams = default)
        {
            if (string.IsNullOrWhiteSpace(authPlayerId))
            {
                return;
            }

            _clientToAuthId[rpcParams.Receive.SenderClientId] = authPlayerId;
            AssignPlayerIdByAuth(authPlayerId);
        }

        private void AssignPlayerId(ulong clientId)
        {
            if (_clientToPlayerId.ContainsKey(clientId))
            {
                return;
            }

            var playerId = clientId == NetworkManager.ServerClientId ? 0 : 1;
            _clientToPlayerId[clientId] = playerId;
        }

        private void AssignPlayerIdByAuth(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return;
            }

            if (_authToPlayerId.ContainsKey(authId))
            {
                return;
            }

            var resolved = ResolvePlayerIdFromAuth(authId);
            if (resolved >= 0)
            {
                _authToPlayerId[authId] = resolved;
                return;
            }

            _authToPlayerId[authId] = _authToPlayerId.ContainsValue(0) ? 1 : 0;
        }

        private int ResolvePlayerIdFromAuth(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return -1;
            }

            if (_authToPlayerId.TryGetValue(authId, out var existing))
            {
                return existing;
            }

            var session = sessionController != null ? sessionController.CurrentSession : null;
            if (session != null && session.IsHost)
            {
                var hostId = session.AsHost().Host;
                if (!string.IsNullOrWhiteSpace(hostId))
                {
                    return string.Equals(authId, hostId, StringComparison.Ordinal) ? 0 : 1;
                }
            }

            return -1;
        }

        private static string GetLocalAuthId()
        {
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                return string.Empty;
            }

            return AuthenticationService.Instance.PlayerId ?? string.Empty;
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

        private static byte[] SerializeSnapshot(GameSessionSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(1); // version
            writer.Write(snapshot.CurrentPlayerId);
            writer.Write((int)snapshot.Phase);
            writer.Write(snapshot.CurrentRound);
            writer.Write(snapshot.RoundRevision);
            writer.Write(snapshot.IsGameOver);

            WriteIntArray(writer, snapshot.TotalScores);
            WriteBoolArray(writer, snapshot.FinishedThisRound);
            WriteInventories(writer, snapshot.Inventories);
            WritePlacedPieces(writer, snapshot.PlacedPieces);
            WriteTerritories(writer, snapshot.ClaimedTerritories);

            return stream.ToArray();
        }

        private static GameSessionSnapshot DeserializeSnapshot(byte[] payload)
        {
            using var stream = new MemoryStream(payload);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadInt32();
            if (version != 1)
            {
                return null;
            }

            var snapshot = new GameSessionSnapshot
            {
                CurrentPlayerId = reader.ReadInt32(),
                Phase = (GamePhase)reader.ReadInt32(),
                CurrentRound = reader.ReadInt32(),
                RoundRevision = reader.ReadInt32(),
                IsGameOver = reader.ReadBoolean(),
                TotalScores = ReadIntArray(reader),
                FinishedThisRound = ReadBoolArray(reader),
                Inventories = ReadInventories(reader),
                PlacedPieces = ReadPlacedPieces(reader),
                ClaimedTerritories = ReadTerritories(reader)
            };

            return snapshot;
        }

        private static void WriteIntArray(BinaryWriter writer, int[] values)
        {
            if (values == null)
            {
                writer.Write(0);
                return;
            }

            writer.Write(values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                writer.Write(values[i]);
            }
        }

        private static int[] ReadIntArray(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var values = new int[length];
            for (var i = 0; i < length; i++)
            {
                values[i] = reader.ReadInt32();
            }
            return values;
        }

        private static void WriteBoolArray(BinaryWriter writer, bool[] values)
        {
            if (values == null)
            {
                writer.Write(0);
                return;
            }

            writer.Write(values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                writer.Write(values[i]);
            }
        }

        private static bool[] ReadBoolArray(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var values = new bool[length];
            for (var i = 0; i < length; i++)
            {
                values[i] = reader.ReadBoolean();
            }
            return values;
        }

        private static void WriteInventories(BinaryWriter writer, Dictionary<string, int>[] inventories)
        {
            if (inventories == null)
            {
                writer.Write(0);
                return;
            }

            writer.Write(inventories.Length);
            for (var i = 0; i < inventories.Length; i++)
            {
                var inventory = inventories[i];
                if (inventory == null)
                {
                    writer.Write(0);
                    continue;
                }

                writer.Write(inventory.Count);
                foreach (var pair in inventory)
                {
                    writer.Write(pair.Key ?? string.Empty);
                    writer.Write(pair.Value);
                }
            }
        }

        private static Dictionary<string, int>[] ReadInventories(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var inventories = new Dictionary<string, int>[count];
            for (var i = 0; i < count; i++)
            {
                var entryCount = reader.ReadInt32();
                var dict = new Dictionary<string, int>();
                for (var j = 0; j < entryCount; j++)
                {
                    var key = reader.ReadString();
                    var value = reader.ReadInt32();
                    dict[key] = value;
                }
                inventories[i] = dict;
            }
            return inventories;
        }

        private static void WritePlacedPieces(BinaryWriter writer, List<PlacedPieceSnapshot> pieces)
        {
            if (pieces == null)
            {
                writer.Write(0);
                return;
            }

            writer.Write(pieces.Count);
            foreach (var piece in pieces)
            {
                writer.Write(piece.InstanceId);
                writer.Write(piece.PlayerId);
                writer.Write(piece.PieceId ?? string.Empty);
                writer.Write(piece.IsCathedral);

                var cells = piece.Cells ?? new List<Cell>();
                writer.Write(cells.Count);
                foreach (var cell in cells)
                {
                    writer.Write(cell.X);
                    writer.Write(cell.Y);
                }
            }
        }

        private static List<PlacedPieceSnapshot> ReadPlacedPieces(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var pieces = new List<PlacedPieceSnapshot>(count);
            for (var i = 0; i < count; i++)
            {
                var snapshot = new PlacedPieceSnapshot
                {
                    InstanceId = reader.ReadInt32(),
                    PlayerId = reader.ReadInt32(),
                    PieceId = reader.ReadString(),
                    IsCathedral = reader.ReadBoolean()
                };

                var cellCount = reader.ReadInt32();
                var cells = new List<Cell>(cellCount);
                for (var c = 0; c < cellCount; c++)
                {
                    var x = reader.ReadInt32();
                    var y = reader.ReadInt32();
                    cells.Add(new Cell(x, y));
                }

                snapshot.Cells = cells;
                pieces.Add(snapshot);
            }

            return pieces;
        }

        private static void WriteTerritories(BinaryWriter writer, List<Cell>[] territories)
        {
            if (territories == null)
            {
                writer.Write(0);
                return;
            }

            writer.Write(territories.Length);
            for (var i = 0; i < territories.Length; i++)
            {
                var cells = territories[i] ?? new List<Cell>();
                writer.Write(cells.Count);
                foreach (var cell in cells)
                {
                    writer.Write(cell.X);
                    writer.Write(cell.Y);
                }
            }
        }

        private static List<Cell>[] ReadTerritories(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var territories = new List<Cell>[count];
            for (var i = 0; i < count; i++)
            {
                var cellCount = reader.ReadInt32();
                var cells = new List<Cell>(cellCount);
                for (var c = 0; c < cellCount; c++)
                {
                    var x = reader.ReadInt32();
                    var y = reader.ReadInt32();
                    cells.Add(new Cell(x, y));
                }
                territories[i] = cells;
            }

            return territories;
        }

        private async System.Threading.Tasks.Task TryRemoveSessionPlayerAsync(string authId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(authId))
            {
                return;
            }

            try
            {
                if (sessionController != null)
                {
                    await sessionController.RemovePlayerAsync(authId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to remove player '{authId}' from session: {ex.Message}");
            }
        }
    }
}
