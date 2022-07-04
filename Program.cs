using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;

using Nivera;
using Nivera.IO;
using Nivera.Utils;
using Nivera.Logging;
using Nivera.Networking;

using Newtonsoft.Json;

namespace Secret_Reboot_Server_Console
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

        public static NetworkServer Server { get; set; }

        public static string Ip { get; set; }

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

            Database.Load();

            Ip = IpUtils.RetrieveCurrentIp();

            Server = new NetworkServer();

            Server.OnConnectionEstablished += (x, e) =>
            {
                Players[x.Id] = new NetworkPlayer(e);

                NiveraLog.Info($"CLIENT {x.Id} CONNECTED FROM {x.EndPoint}");
            };

            Server.OnConnectionTerminated += (x, e) =>
            {
                NiveraLog.Info($"CLIENT {x.Id} DISCONNECTED FROM {x.EndPoint}: {e.Reason} ({e.SocketErrorCode})");

                Players[x.Id].PrepareForRemoval();
                Players.Remove(x.Id);
            };

            Server.Start();

            NiveraLog.Info($"Server is listening on IP {Ip} and all ports (UDP)");
            NiveraLog.Info($"Finished loading.");

            await Task.Delay(-1);
        }
    }

    public class PacketBuilder
    {
        private NetworkPacket packet = new NetworkPacket();

        public static PacketBuilder Default => new PacketBuilder().WithHeader("Sender", "Console").WithIp(Program.Ip);

        public PacketBuilder WithHeader(string name, string header)
        {
            packet.Headers[name] = header;

            return this;
        }

        public PacketBuilder WithIp(string ip)
        {
            packet.Headers["SenderAddress"] = ip;

            return this;
        }

        public PacketBuilder WithArg(string name, string value)
        {
            packet.Args[name] = value;

            return this;
        }

        public PacketBuilder WithContent(object content)
        {
            var contents = new List<string>(packet.Content);

            contents.Add(content.ToString());

            packet.Content = contents.ToArray();

            return this;
        }

        public NetworkPacket Complete()
            => packet;
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

        public static Dictionary<string, ServerListServerData> VerifiedServers = new Dictionary<string, ServerListServerData>();

        public static void UpdateDataOnList(string serverToken, Dictionary<string, string> data)
        {
            SavedServerData savedServerData = Database.Servers.Find(x => x.Token == serverToken);

            if (savedServerData == null)
                return;

            if (!savedServerData.IsVerified)
                return;

            if (savedServerData.ServerPunishmentHistory.History.Any(x => x.Severity == ServerPunishmentSeverity.PermanentServerListRemoval || x.Severity == ServerPunishmentSeverity.TemporaryServerListRemoval))
                return;

            if (!VerifiedServers.ContainsKey(serverToken))
                VerifiedServers[serverToken] = new ServerListServerData();

            VerifiedServers[serverToken].IP = data["Address"];
            VerifiedServers[serverToken].MaxPlayers = int.Parse(data["MaxPlayers"]);
            VerifiedServers[serverToken].Name = data["Name"];
            VerifiedServers[serverToken].Pastebin = data["Pastebin"];
            VerifiedServers[serverToken].Players = int.Parse(data["Players"]);
            VerifiedServers[serverToken].Port = int.Parse(data["Port"]);
        }
    }

    public class NetworkPlayer
    {
        public NetworkConnection Connection { get; set; }

        public string IP { get; set; }
        public int Port { get; set; }
        public bool IsServer { get; set; }     

        public NetworkPlayer(NetworkConnection networkConnection)
        {
            Connection = networkConnection;

            networkConnection.OnDataReceived += x =>
            {
                NiveraLog.Info($"CLIENT {Connection.netPeer.Id} FROM {x.Headers["SenderAddress"]} SENT {x.Headers["Sender"]} PACKET ({x.Content.Length}/{x.Headers.Values.Count})");

                if (x.Headers["Sender"] == "Identity")
                {
                    IsServer = bool.Parse(x.Headers["ServerIdentity"]);
                    Port = int.Parse(x.Headers["ServerPort"]);
                    IP = x.Headers["SenderAddress"];

                    return;
                }

                if (x.Headers["Sender"] == "ServerClient")
                    ProcessServer(x);
                else
                    ProcessClient(x);

                NiveraLog.Info($"CLIENT {Connection.netPeer.Id} PING: {Connection.latency} ms");
            };
        }

        public void ProcessCommand(NetworkPacket networkPacket)
        {
            SavedPlayerData adminData = Database.Players.Find(x => x.HWID == networkPacket.Headers["PlayerHwId"]);

            if (adminData == null)
            {
                Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "AuthCommand").WithArg("Result", "Failed").WithArg("FailReason", "UNAUTHORIZED_PLAYER").Complete());

                return;
            }

            switch (networkPacket.Args["AuthCommandName"])
            {
                case "GlobalBanAdd":
                    {
                        if (!adminData.Token.Contains("gbAllowed"))
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "AuthCommand").WithArg("Result", "Failed").WithArg("FailReason", "UNAUTHORIZED_PLAYER").Complete());

                            return;
                        }

                        SavedPlayerData savedPlayerData = Database.Players.Find(x => x.HWID == networkPacket.Args["BannedHwId"]);

                        if (savedPlayerData == null)
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "AuthCommand").WithArg("AuthCommandName", "GlobalBanAdd").WithArg("Result", "Failed").WithArg("FailReason", "UNKNOWN_PLAYER").Complete());

                            break;
                        }

                        savedPlayerData.BanHistory.History.Add(new PlayerBan
                        {
                            ActiveFrom = DateTime.Now,
                            ActiveUntil = DateTime.Parse(networkPacket.Args["BanActiveUntil"]),
                            BanID = new Random().Next(10, 1000),
                            IsGlobal = true,
                            IsPermanent = networkPacket.Args["IsPermanent"] == "True",
                            IssuerID = adminData.ID,
                            Reason = networkPacket.Args["BanReason"]
                        });

                        foreach (var server in ServerList.VerifiedServers)
                        {
                            SavedServerData savedServerData = Database.Servers.Find(x => x.Token == server.Key);

                            if (savedServerData == null)
                                continue;

                            var player = Program.Players.FirstOrDefault(x => x.Value.Connection.netPeer.EndPoint.Address.ToString().Split(':')[0] == savedServerData.IP).Value;

                            player.Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "NewGlobalBan").WithArg("PlayerHwId", savedPlayerData.HWID).Complete());
                        }

                        Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "AuthCommand").WithArg("AuthCommandName", "GlobalBanAdd").WithArg("Result", "Success").WithArg("GlobalBanId", savedPlayerData.BanHistory.History.Last().BanID.ToString()).Complete());

                        Database.ValidatePunishments();
                        Database.Save();

                        break;
                    }
            }
        }

        public void ProcessServer(NetworkPacket networkPacket)
        {
            switch (networkPacket.Headers["RequestType"])
            {
                case "None":
                    {
                        break;
                    }

                case "PlayerInfo":
                    {
                        SavedPlayerData savedPlayerData = Database.Players.Find(x => x.HWID == networkPacket.Args["PlayerHwId"]);

                        if (savedPlayerData == null)
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "PlayerInfo").WithArg("Result", "Failed").WithArg("FailReason", "UNKNOWN_PLAYER").Complete());

                            return;
                        }

                        Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "PlayerInfo").WithArg("Result", "Ok").WithArg("PlayerData", JsonConvert.SerializeObject(savedPlayerData)).Complete());

                        break;
                    }

                case "AuthCommand":
                    {
                        SavedPlayerData savedPlayerData = Database.Players.Find(x => x.HWID == networkPacket.Args["PlayerHwId"]);

                        if (savedPlayerData == null)
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "AuthCommand").WithArg("Result", "Failed").WithArg("FailReason", "UNAUTHORIZED_PLAYER").Complete());

                            return;
                        }

                        ProcessCommand(networkPacket);

                        break;
                    }

                case "DataUpdate":
                    {
                        ServerList.UpdateDataOnList(networkPacket.Args["ServerToken"], networkPacket.Args);

                        break;
                    }

                case "DownloadToken":
                    {
                        SavedServerData savedServerData = Database.Servers.FirstOrDefault(x => x.IP == networkPacket.Args["ServerAddress"]);

                        if (savedServerData == null || !savedServerData.IsVerified)
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "DownloadToken").WithArg("Result", "Failed").WithArg("FailReason", "UNAUTHORIZED_SERVER").Complete());

                            return;
                        }

                        Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "DownloadToken").WithArg("ServerToken", savedServerData.Token).Complete());

                        break;
                    }
            }
        }

        public void ProcessClient(NetworkPacket networkPacket)
        {
            switch (networkPacket.Headers["RequestType"])
            {
                case "DownloadServerList":
                    {
                        SavedPlayerData savedPlayerData = Database.Players.Find(x => x.HWID == networkPacket.Args["PlayerHwId"]);

                        if (savedPlayerData == null)
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "DownloadServerList").WithArg("Result", "Failed").WithArg("FailReason", "UNAUTHORIZED_PLAYER").Complete());

                            return;
                        }

                        if (savedPlayerData.BanHistory.IsPermanentActive)
                        {
                            Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "DownloadServerList").WithArg("Result", "Failed").WithArg("FailReason", "ACTIVE_BAN").Complete());

                            return;
                        }

                        Connection.Send(PacketBuilder.Default.WithHeader("RequestType", "DownloadServerList").WithArg("Result", "Ok").WithArg("ServerListData", JsonConvert.SerializeObject(ServerList.VerifiedServers)).Complete());

                        break;
                    }
            }
        }

        public void PrepareForRemoval()
        {
            Connection.Disconnect();
            Connection = null;
        }
    }
}
