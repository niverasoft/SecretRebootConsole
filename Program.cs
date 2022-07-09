using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System;

using Nivera;
using Nivera.IO;
using Nivera.Utils;
using Nivera.Logging;

using LiteNetLib;
using LiteNetLib.Utils;

using Newtonsoft.Json;

namespace SecretRebootConsole
{
    public enum ServerPunishmentSeverity
    {
        Warning,
        Monitoring,
        TemporaryServerListRemoval,
        PermanentServerListRemoval
    }

    public class ServerPunishment
    {
        public ServerPunishmentSeverity Severity { get; set; } = ServerPunishmentSeverity.Warning;
        public string Reason { get; set; } = "DEFAULT";
        public string IssuerID { get; set; } = "DEFAULT";

        public int PunishmentID { get; set; } = 0;

        public DateTime ActiveFrom { get; set; } = DateTime.MinValue;
        public DateTime ActiveUntil { get; set; } = DateTime.MinValue;

        public bool IsGlobal { get; set; }
        public bool IsPermanent { get; set; }
    }

    public class PlayerBan
    {
        public string Reason { get; set; } = "DEFAULT";
        public string IssuerID { get; set; } = "DEFAULT";
        public int BanID { get; set; } = 0;

        public DateTime ActiveFrom { get; set; } = DateTime.MinValue;
        public DateTime ActiveUntil { get; set; } = DateTime.MinValue;

        public bool IsGlobal { get; set; }
        public bool IsPermanent { get; set; }
    }

    public class PlayerBanHistory
    {
        public bool IsPermanentActive { get; set; }

        public List<PlayerBan> History { get; set; } = new List<PlayerBan>();
    }

    public class ServerPunishmentHistory
    {
        public bool IsPermanentActive { get; set; }

        public List<ServerPunishment> History { get; set; } = new List<ServerPunishment>();
    }

    public class SavedPlayerData
    {
        public string Nickname { get; set; } = "DEFAULT";
        public string ID { get; set; } = "DEFAULT";
        public string IP { get; set; } = "DEFAULT";
        public string Token { get; set; } = "DEFAULT";
        public string HWID { get; set; } = "DEFAULT";

        public PlayerBanHistory BanHistory { get; set; } = new PlayerBanHistory();
    }

    public class SavedServerData
    {
        public bool IsVerified { get; set; }

        public string Name { get; set; } = "DEFAULT";

        public int PlayersActive { get; set; } = 0;
        public int MaxPlayers { get; set; } = 0;
        public int Port { get; set; } = 0;

        public string IP { get; set; } = "DEFAULT";
        public string Token { get; set; } = "DEFAULT";
        public string ID { get; set; } = "DEFAULT";
        public string HWID { get; set; } = "DEFAULT";

        public ServerPunishmentHistory ServerPunishmentHistory { get; set; } = new ServerPunishmentHistory();
    }

    public static class Database
    {
        public static List<SavedPlayerData> Players { get; set; } = new List<SavedPlayerData>();
        public static List<SavedServerData> Servers { get; set; } = new List<SavedServerData>();

        public static void Load()
        {
            NiveraLog.Info("Loading database");

            if (!File.Exists("./database"))
            {
                Save();

                NiveraLog.Info("Database loaded.");

                return;
            }

            var file = BinaryFile.ReadFrom("./database");

            Players.AddRange(file.DeserializeFile<List<SavedPlayerData>>("players"));
            Servers.AddRange(file.DeserializeFile<List<SavedServerData>>("servers"));

            NiveraLog.Info("Database loaded.");

            Task.Run(ValidatePunishmentsLoop);
        }

        public static async Task ValidatePunishmentsLoop()
        {
            while (true)
            {
                await Task.Delay(1000);

                ValidatePunishments();
            }
        }

        public static void ValidatePunishments()
        {
            bool save = false;

            foreach (var playerData in Players)
            {
                playerData.BanHistory.IsPermanentActive = playerData.BanHistory.History.Any(x => x.IsPermanent);

                foreach (var playBan in playerData.BanHistory.History)
                {
                    if (playBan.IsPermanent)
                        continue;

                    if (playBan.ActiveUntil < DateTime.Now)
                    {
                        playerData.BanHistory.History.Remove(playBan);

                        save = true;
                    }
                }
            }
            
            foreach (var serverData in Servers)
            {
                serverData.ServerPunishmentHistory.IsPermanentActive = serverData.ServerPunishmentHistory.History.Any(x => x.IsPermanent);

                foreach (var serverPunishment in serverData.ServerPunishmentHistory.History)
                {
                    if (serverPunishment.IsPermanent)
                        continue;

                    if (serverPunishment.ActiveUntil < DateTime.Now)
                    {
                        serverData.ServerPunishmentHistory.History.Remove(serverPunishment);

                        save = true;
                    }
                }
            }

            if (save)
                Save();
        }

        public static void Save()
        {
            var file = new BinaryFile();

            file.SerializeFile(Players, "players");
            file.SerializeFile(Servers, "servers");
            file.WriteTo("./database");

            NiveraLog.Info("Saved database.");
        }
    }

    public static class Program
    {
        public static Dictionary<int, NetworkPlayer> Players { get; } = new Dictionary<int, NetworkPlayer>();

        public static NetManager NetManager { get; set; }
        public static EventBasedNetListener EventBasedNetListener { get; set; }

        public static string Ip { get; set; }

        public static string CdnDirectory { get; set; }
        public static string ConnectionKey { get; set; }
        public static int[] EncryptionKey { get; set; }

        public static string CreateConnectionKey()
        {
            return RandomGen.RandomBytesString(50, 50);
        }

        public static void Exit()
        {
            Database.Save();
            ServerList.NoServers();

            File.Delete($"{CdnDirectory}/connectionkey.txt");
        }

        public static async Task Main(string[] args)
        {
            LibProperties.Logger = new SystemConsoleLogger();
            LibProperties.Log_AddStackTraceToThrowHelper = true;
            LibProperties.Log_DateTimeFormat = "t";
            LibProperties.Log_EnableDebugLog = true;
            LibProperties.Log_EnableVerboseLog = true;
            LibProperties.Log_ShowCurrentFunctionInLog = true;
            LibProperties.System_HandleUnhandledExceptions = true;
            LibProperties.System_UseUnityCompatModule = false;
            LibProperties.Load();

            NiveraLog.Info("Hello! Loading Secret Reboot ..");

            CdnDirectory = args[0];

            NiveraLog.Info($"CDN directory set to {CdnDirectory}");

            Database.Load();
            ServerList.Start();

            Ip = IpUtils.RetrieveCurrentIp();

            NiveraLog.Info($"Building network components ..");

            EventBasedNetListener = new EventBasedNetListener();
            NetManager = new NetManager(EventBasedNetListener);

            NiveraLog.Info($"Generating encryption key ..");

            EncryptionKey = Nivera.Encryption.EncryptionKey.GenerateKey(256);

            NiveraLog.Info($"Encryption key generated.");
            NiveraLog.Info($"Generating connection key ..");

            ConnectionKey = CreateConnectionKey();

            File.WriteAllText($"{CdnDirectory}/connectionkey.txt", ConnectionKey);

            NiveraLog.Info($"Connection key generated: {ConnectionKey}");
            NiveraLog.Info("Finished building network");

            NetManager.AllowPeerAddressChange = false;
            NetManager.BroadcastReceiveEnabled = false;
            NetManager.AutoRecycle = true;
            NetManager.DisconnectOnUnreachable = true;
            NetManager.DisconnectTimeout = 10000;
            NetManager.EnableStatistics = true;
            NetManager.IPv6Mode = IPv6Mode.Disabled;
            NetManager.MaxConnectAttempts = 5;
            NetManager.PingInterval = 200;
            NetManager.ReconnectDelay = 1000;
            NetManager.ReuseAddress = true;
            NetManager.UnconnectedMessagesEnabled = false;

            NiveraLog.Info("Server Manager configured, registering event listeners ..");

            EventBasedNetListener.ConnectionRequestEvent += x =>
            {
                NiveraLog.Verbose($"Received a connection request from {x.RemoteEndPoint.Address}");

                x.AcceptIfKey(ConnectionKey);
            };

            EventBasedNetListener.DeliveryEvent += (x, e) =>
            {
                NiveraLog.Warn($"DeliveryEvent: {e} FROM {x.EndPoint.Address}");
            };

            EventBasedNetListener.NetworkErrorEvent += (x, e) =>
            {
                NiveraLog.Error($"A socket error occured on endpoint {x.Address}: {e}");
            };

            EventBasedNetListener.NetworkLatencyUpdateEvent += (x, e) =>
            {
                Players[x.Id].Ping = e;

                NiveraLog.Debug($"Updated latency of {x.EndPoint}: {e}");
            };

            EventBasedNetListener.NetworkReceiveEvent += (x, e, z, y) =>
            {
                if (y != DeliveryMethod.ReliableOrdered)
                {
                    NiveraLog.Warn($"Received a {y} packet from {x.EndPoint}");
                    return;
                }

                if (!Players.TryGetValue(x.Id, out var player))
                {
                    NiveraLog.Warn($"Failed to find a corresponding network handler for {x.EndPoint}");
                    return;
                }

                int[] key = JsonConvert.DeserializeObject<int[]>(e.GetString());

                if (key == null || key != EncryptionKey)
                {
                    NiveraLog.Warn($"Client {x.EndPoint} sent a invalid encryption key.");
                    return;
                }

                string json = Nivera.Encryption.Encryption.Decrypt(EncryptionKey, e.GetString());

                Dictionary<string, string> packet = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                if (packet["SenderType"] == "GameClient")
                    player.ProcessClient(packet);
                else if (packet["SenderType"] == "ServerClient")
                    player.ProcessServer(packet);
                else
                    NiveraLog.Warn($"Received an invalid SenderType from {x.EndPoint}: {packet["SenderType"]}");
            };

            EventBasedNetListener.NetworkReceiveUnconnectedEvent += (x, e, z) =>
            {
                NiveraLog.Warn($"Recieved an unconnected message from {x}: {z}");
            };

            EventBasedNetListener.PeerConnectedEvent += x =>
            {
                NiveraLog.Info($"Peer connected, sending encryption key ..");

                NetDataWriter netDataWriter = new NetDataWriter(true);

                netDataWriter.Put(JsonConvert.SerializeObject(EncryptionKey));

                x.Send(netDataWriter, DeliveryMethod.ReliableOrdered);

                NiveraLog.Info($"Encryption key sent, creating network handler ..");

                Players[x.Id] = new NetworkPlayer(x);

                NiveraLog.Info("Network Handler created.");
            };

            EventBasedNetListener.PeerDisconnectedEvent += (x, e) =>
            {
                NiveraLog.Info($"Peer disconnected from {x.EndPoint}: {e.Reason}/{e.SocketErrorCode}");
                NiveraLog.Info($"Destroying network handler ..");

                if (Players[x.Id].IsServer && !string.IsNullOrEmpty(Players[x.Id].Token))
                    ServerList.OnServerDisconnected(Players[x.Id].Token);

                Players[x.Id].PrepareForRemoval();
                Players.Remove(x.Id);
            };

            Console.CancelKeyPress += (x, e) =>
            {
                Exit();
            };

            AppDomain.CurrentDomain.UnhandledException += (x, e) =>
            {
                NiveraLog.Error(e.ExceptionObject);

                if (e.IsTerminating)
                    Exit();
            };

            AppDomain.CurrentDomain.ProcessExit += (x, e) =>
            {
                Exit();
            };

            NetManager.Start();

            NiveraLog.Info($"Server is listening on IP {Ip} and all ports (UDP)");
            NiveraLog.Info($"Finished loading.");

            await PollEvents();

            await Task.Delay(-1);
        }

        public static async Task PollEvents()
        {
            NiveraLog.Info("NetManager EventPolling Started");

            while (true)
            {
                await Task.Delay(20);

                NetManager.PollEvents();
            }
        }
    }

    public static class ServerList
    {
        public class ServerListServerData
        {
            public string Name { get; set; }
            public string IP { get; set; }
            public string Pastebin { get; set; }
            public int Port { get; set; }
            public int Players { get; set; }
            public int MaxPlayers { get; set; }
        }

        public class NoServersResponse
        {
            [JsonProperty("response")]
            public string Response { get; set; } = "No servers available.";
        }

        public static Dictionary<string, ServerListServerData> VerifiedServers = new Dictionary<string, ServerListServerData>();

        public static void NoServers()
        {
            File.WriteAllText($"{Program.CdnDirectory}/verifiedserverlist.json", JsonConvert.SerializeObject(new NoServersResponse(), Formatting.Indented));
        }

        public static void Start()
        {
            NoServers();
        }

        public static void UpdateDataOnList(string serverToken, Dictionary<string, string> data)
        {
            SavedServerData savedServerData = Database.Servers.Find(x => x.Token == serverToken);

            if (savedServerData == null)
                return;

            if (!savedServerData.IsVerified)
                return;

            if (!VerifiedServers.ContainsKey(serverToken))
                VerifiedServers[serverToken] = new ServerListServerData();

            VerifiedServers[serverToken].IP = data["ServerIp"];
            VerifiedServers[serverToken].MaxPlayers = int.Parse(data["ServerMaxPlayers"]);
            VerifiedServers[serverToken].Name = data["ServerName"];
            VerifiedServers[serverToken].Pastebin = data["ServerPastebinId"];
            VerifiedServers[serverToken].Players = int.Parse(data["ServerCurrentPlayers"]);
            VerifiedServers[serverToken].Port = int.Parse(data["ServerPort"]);

            File.WriteAllText($"{Program.CdnDirectory}/verifiedserverlist.json", JsonConvert.SerializeObject(VerifiedServers, Formatting.Indented));
        }

        public static void OnServerDisconnected(string serverToken)
        {
            VerifiedServers.Remove(serverToken);

            File.WriteAllText($"{Program.CdnDirectory}/verifiedserverlist.json", JsonConvert.SerializeObject(VerifiedServers, Formatting.Indented));
        }
    }

    public static class PacketExtensions
    {
        public static Dictionary<string, string> AddData(this Dictionary<string, string> packet, string key, object value)
        {
            packet.Add(key, value.ToString());

            return packet;
        }
    }

    public class NetworkPlayer
    {
        public NetPeer Peer { get; }
        public string IP { get; set; }
        public int Port { get; set; }
        public bool IsServer { get; set; }    
        public int Ping { get; set; }
        public string Token { get; set; }

        public IPEndPoint EndPoint { get; set; }

        public NetworkPlayer(NetPeer netPeer)
        {
            Peer = netPeer;
            EndPoint = netPeer.EndPoint;
        }

        public Dictionary<string, string> CreatePacket()
        {
            return new Dictionary<string, string>()
            {
                ["SenderType"] = "ConsoleClient",
                ["SenderAddress"] = Program.Ip,
                ["RequestType"] = "ConsoleRequest",
                ["RequestName"] = "",
            };
        }

        public void Send(Dictionary<string, string> packet)
        {
            if (Peer.ConnectionState != ConnectionState.Disconnected)
            {
                NetDataWriter netDataWriter = new NetDataWriter(true);

                netDataWriter.Put(JsonConvert.SerializeObject(packet));

                Peer.Send(netDataWriter, DeliveryMethod.ReliableOrdered);
            }
        }

        public void ProcessCommand(Dictionary<string, string> packet)
        {
            SavedPlayerData adminData = Database.Players.FirstOrDefault(x => x.HWID == packet["TargetHwIdToken"]);

            switch (packet["AuthCommandName"])
            {
                case "GlobalBanAdd":
                    {
                        if (!adminData.Token.Contains("gbAllowed"))
                        {
                            Send(CreatePacket().AddData("Result", "False").AddData("Reason", "Unauthorized_Player"));

                            return;
                        }

                        SavedPlayerData savedPlayerData = Database.Players.FirstOrDefault(x => x.HWID == packet["BannedHwIdToken"]);

                        if (savedPlayerData == null)
                        {
                            Send(CreatePacket().AddData("Result", "False").AddData("Reason", "Unknown_Target_Player"));

                            break;
                        }

                        savedPlayerData.BanHistory.History.Add(new PlayerBan
                        {
                            ActiveFrom = DateTime.Now,
                            ActiveUntil = DateTime.Parse(packet["BanActiveUntil"]),
                            BanID = new Random().Next(10, 1000),
                            IsGlobal = true,
                            IsPermanent = packet["IsPermanent"] == "True",
                            IssuerID = adminData.ID,
                            Reason = packet["BanReason"]
                        });

                        foreach (var server in ServerList.VerifiedServers)
                        {
                            NetworkPlayer networkPlayer = Program.Players.FirstOrDefault(x => x.Value.IsServer && x.Value.Token == server.Key).Value;

                            if (networkPlayer != null)
                            {
                                networkPlayer.Send(networkPlayer.CreatePacket().AddData("RequestName", "CheckGlobalBan").AddData("BannedHwIdToken", packet["BannedHwIdToken"]));
                            }
                        }

                        Send(CreatePacket().AddData("Result", "True").AddData("GlobalBanId", savedPlayerData.BanHistory.History.Last().BanID));

                        Database.ValidatePunishments();
                        Database.Save();

                        break;
                    }
            }
        }

        public string GenerateRandomIdentifier()
        {
            List<byte> bytes = new List<byte>();

            Random random = new Random();

            while (bytes.Count != 30)
            {
                bytes.Add(Convert.ToByte(random.Next(0, 255)));
            }

            return Encoding.UTF32.GetString(bytes.ToArray());
        }

        public string GenerateRandomToken()
        {
            List<byte> bytes = new List<byte>();

            Random random = new Random();

            while (bytes.Count != 50)
            {
                bytes.Add(Convert.ToByte(random.Next(0, 255)));
            }

            return Convert.ToBase64String(bytes.ToArray());
        }
        
        public void ProcessServer(Dictionary<string, string> packet)
        {
            switch (packet["RequestName"])
            {
                case "ServerInfo":
                    {
                        IP = packet["ServerIp"];
                        IsServer = bool.Parse(packet["ServerStatus"]);
                        Port = int.Parse(packet["ServerPort"]);

                        SavedServerData savedServerData = Database.Servers.FirstOrDefault(x => x.IP == IP && x.Port == Port && x.HWID == packet["HwIdToken"]);

                        if (savedServerData == null)
                        {
                            savedServerData = new SavedServerData()
                            {
                                HWID = packet["HwIdToken"],
                                ID = GenerateRandomIdentifier(),
                                IP = IP,
                                IsVerified = false,
                                MaxPlayers = int.Parse(packet["ServerMaxPlayers"]),
                                Name = packet["ServerName"],
                                PlayersActive = int.Parse(packet["ServerCurrentPlayers"]),
                                Port = Port,
                                ServerPunishmentHistory = new ServerPunishmentHistory(),
                                Token = GenerateRandomToken()
                            };

                            Database.Servers.Add(savedServerData);
                            Database.Save();
                        }

                        Send(CreatePacket().AddData("ServerData", JsonConvert.SerializeObject(savedServerData)).AddData("RequestName", "ServerInfo"));

                        break;
                    }

                case "PlayerInfo":
                    {
                        SavedPlayerData savedPlayerData = Database.Players.FirstOrDefault(x => x.HWID == packet["TargetHwIdToken"]);

                        if (savedPlayerData == null)
                        {
                            Send(CreatePacket().AddData("Result", "False").AddData("Reason", "Unknown_Player").AddData("RequestName", "PlayerInfo"));

                            return;
                        }

                        Send(CreatePacket().AddData("Result", "True").AddData("PlayerData", JsonConvert.SerializeObject(savedPlayerData)).AddData("RequestName", "PlayerInfo"));

                        break;
                    }

                case "AuthCommand":
                    {
                        SavedPlayerData savedPlayerData = Database.Players.FirstOrDefault(x => x.HWID == packet["TargetHwIdToken"]);

                        if (savedPlayerData == null)
                        {
                            Send(CreatePacket().AddData("Result", "False").AddData("Reason", "Unknown_Player").AddData("RequestName", "AuthCommand"));

                            return;
                        }

                        if (!savedPlayerData.Token.Contains("authCmds"))
                        {
                            Send(CreatePacket().AddData("Result", "False").AddData("Reason", "Unauthorized_Player").AddData("RequestName", "AuthCommand"));

                            return;
                        }

                        ProcessCommand(packet); ;

                        break;
                    }

                case "DataUpdate":
                    {
                        ServerList.UpdateDataOnList(packet["ServerToken"], packet);

                        break;
                    }

                case "DownloadToken":
                    {
                        SavedServerData savedServerData = Database.Servers.FirstOrDefault(x => x.IP == IP && x.Port == Port && x.HWID == packet["HwIdToken"]);

                        if (savedServerData == null)
                        {
                            savedServerData = new SavedServerData()
                            {
                                HWID = packet["HwIdToken"],
                                ID = GenerateRandomIdentifier(),
                                IP = IP,
                                IsVerified = false,
                                MaxPlayers = int.Parse(packet["ServerMaxPlayers"]),
                                Name = packet["ServerName"],
                                PlayersActive = int.Parse(packet["ServerCurrentPlayers"]),
                                Port = Port,
                                ServerPunishmentHistory = new ServerPunishmentHistory(),
                                Token = GenerateRandomToken()
                            };

                            Database.Servers.Add(savedServerData);
                            Database.Save();
                        }

                        Send(CreatePacket().AddData("Result", "True").AddData("ServerToken", savedServerData.Token).AddData("RequestName", "DownloadToken"));

                        Token = savedServerData.Token;

                        break;
                    }
            }
        }

        public void ProcessClient(Dictionary<string, string> packet)
        {
            switch (packet["RequestName"])
            {

            }
        }

        public void PrepareForRemoval()
        {
            
        }
    }
}
