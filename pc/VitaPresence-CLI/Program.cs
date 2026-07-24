using PresenceCommon;
using PresenceCommon.Types;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace VitaPresence_CLI
{
    class Program
    {
        static Timer timer;
        static Socket client;
        static string LastGame = "";
        static long? time = null;
        static DiscordIpcClient rpc;
        static string clientId;
        static string steamGridDbKey;
        static bool swapPresenceStyle = false;

        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: VitaPresence-CLI <IP> [Client ID] [SteamGridDB Key] [--swap]");
                return 1;
            }

            if (!IPAddress.TryParse(args[0], out IPAddress iPAddress))
            {
                Console.WriteLine("Invalid IP address");
                return 1;
            }

            clientId       = (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1])) ? args[1] : CoverResolver.DEFAULT_CLIENT_ID;
            steamGridDbKey = (args.Length >= 3 && !args[2].StartsWith("--")) ? args[2] : null;

            foreach (var arg in args)
            {
                if (arg.Equals("--swap", StringComparison.OrdinalIgnoreCase))
                    swapPresenceStyle = true;
            }

            Console.WriteLine($"Initializing Discord IPC with Client ID: {clientId}");
            rpc = new DiscordIpcClient(clientId);

            if (!rpc.Initialize())
            {
                Console.WriteLine("Unable to connect to Discord! Is Discord running?");
                return 2;
            }

            Console.WriteLine("Connected to Discord.");

            IPEndPoint localEndPoint = new IPEndPoint(iPAddress, 0xCAFE);

            timer = new Timer()
            {
                Interval = 60000,
                Enabled  = false,
            };
            timer.Elapsed += new ElapsedEventHandler(OnConnectTimeout);

            bool firstRun = true;

            while (true)
            {
                client = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveTimeout = 5500,
                    SendTimeout    = 5500,
                };

                timer.Enabled = true;

                try
                {
                    IAsyncResult result = client.BeginConnect(localEndPoint, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(2000, true);
                    if (!success)
                    {
                        Console.WriteLine("Could not connect to PS Vita! Retrying in 10s...");
                        client.Close();
                        if (rpc != null && !rpc.IsDisposed) rpc.ClearPresence();
                    }
                    else
                    {
                        client.EndConnect(result);
                        timer.Enabled = false;

                        DataListenOne(ref firstRun);

                        client.Close();
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"Connection error: {ex.Message}");
                    client.Close();
                    if (rpc != null && !rpc.IsDisposed) rpc.ClearPresence();
                }

                Thread.Sleep(10000); // 10s update interval
            }
        }

        private static void DataListenOne(ref bool firstRun)
        {
            byte[] bytes = new byte[600];
            int cnt = client.Receive(bytes);

            if (cnt <= 0) return;

            Title title = new Title(bytes);
            if (title.Magic == 0xCAFECAFE || title.Magic == 0xCAFECAFF)
            {
                if (LastGame != title.TitleID)
                {
                    time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                if (LastGame != title.TitleID || firstRun)
                {
                    var presence = Utils.CreateDiscordPresence(
                        title, time, "",
                        steamGridDbKey, clientId,
                        swapPresenceStyle: swapPresenceStyle,
                        showTimer: true);

                    rpc.SetPresence(presence);

                    string titleInfo = title.Index == 0 ? "In LiveArea" : $"Playing [{title.TitleID}] {title.TitleName}";
                    Console.WriteLine($"[Updated] {titleInfo}");

                    LastGame = title.TitleID;
                    firstRun = false;
                }
            }
            else
            {
                Console.WriteLine("Received invalid magic packet from Vita.");
                if (rpc != null && !rpc.IsDisposed) rpc.ClearPresence();
            }
        }

        private static void OnConnectTimeout(object sender, ElapsedEventArgs e)
        {
            LastGame = "";
            time     = null;
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            if (client != null && client.Connected)
                client.Close();

            if (rpc != null && !rpc.IsDisposed)
                rpc.Dispose();
        }
    }
}
