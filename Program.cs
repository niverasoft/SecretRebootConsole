using System.Collections.Generic;
using System.IO;
using System;

using Nivera;
using Nivera.IO;
using Nivera.Utils;
using Nivera.Logging;
using Nivera.Networking;

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

                return;
            }

            var file = BinaryFile.ReadFrom("./database");

            Players.AddRange(file.DeserializeFile<List<SavedPlayerData>>("players"));
            Servers.AddRange(file.DeserializeFile<List<SavedServerData>>("servers"));

            NiveraLog.Info("Database loaded.");
        }

        public static void ValidatePunishments()
        {
            NiveraLog.Info($"Validating server and player punishments ..");

            NiveraLog.Info("Server and player punishments were succesfully validated.");
        }

        public static void Save()
        {
            var file = new BinaryFile();

            file.SerializeFile(Players, "players");
            file.SerializeFile(Servers, "servers");
            file.WriteTo("./database");
        }
    }

    public static class Program
    {
        public static Dictionary<int, NetworkPlayer> Players { get; } = new Dictionary<int, NetworkPlayer>();

        public static NetworkServer Server { get; set; }

        public static string Ip { get; set; }

        public static void Main(string[] args)
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

            Ip = IpUtils.RetrieveCurrentIp();

            Server = new NetworkServer();

            Server.OnConnectionEstablished += x =>
            {
                Players[x.Id] = new NetworkPlayer(new NetworkConnection(Server.netManager, Server.eventBasedNetListener));

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
        }
    }

    public class PacketBuilder
    {
        private NetworkPacket packet = new NetworkPacket();

        public static PacketBuilder Default => new PacketBuilder().WithHeader("CONSOLE").WithIp(Program.Ip);

        public PacketBuilder WithChannel(byte channel)
        {
            packet.Channel = channel;

            return this;
        }

        public PacketBuilder WithHeader(string header)
        {
            packet.Header = header;

            return this;
        }

        public PacketBuilder WithIp(string ip)
        {
            packet.SourceIp = ip;

            return this;
        }

        public PacketBuilder AddArg(object arg)
        {
            var args = new List<string>(packet.Args);

            args.Add(arg.ToString());

            packet.Args = args.ToArray();

            return this;
        }

        public PacketBuilder AddContent(object content)
        {
            var contents = new List<string>(packet.Content);

            contents.Add(content.ToString());

            packet.Content = contents.ToArray();

            return this;
        }

        public NetworkPacket Complete()
            => packet;
    }

    public class NetworkPlayer
    {
        public NetworkConnection Connection { get; set; }

        public NetworkPlayer(NetworkConnection networkConnection)
        {
            Connection = networkConnection;

            networkConnection.OnDataReceived += x =>
            {
                NiveraLog.Info($"CLIENT {Connection.netPeer.Id} FROM {x.SourceIp} SENT {x.Header} PACKET ON CHANNEL {x.Channel} ({x.Content.Length}/{x.Args.Length})");

                if (x.Header == "SERVER")
                    ProcessServer(x);
                else
                    ProcessClient(x);

                NiveraLog.Info($"CLIENT {Connection.netPeer.Id} PING: {Connection.latency} ms");
            };
        }

        public void ProcessServer(NetworkPacket networkPacket)
        {
            switch (networkPacket.Channel)
            {
                // Hello
                case 0:
                    {
                        Connection.Send(PacketBuilder.Default.AddContent("Ok").Complete());

                        break;
                    }
            }
        }

        public void ProcessClient(NetworkPacket networkPacket)
        {
            switch (networkPacket.Channel)
            {

            }
        }

        public void PrepareForRemoval()
        {
            Connection.Disconnect();
            Connection = null;
        }
    }

    public static class KeyGen
    {

    }
}
