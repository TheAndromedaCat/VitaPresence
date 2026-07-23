using Newtonsoft.Json;
using PresenceCommon;
using PresenceCommon.Types;
using VitaPresence_GUI.Properties;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using Timer = System.Timers.Timer;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace VitaPresence_GUI
{
    public partial class MainForm : Form
    {
        private const int WM_SHOWME = 0x8000; // Definir la constante WM_SHOWME

        private Thread listenThread;
        private static Socket client;
        private static DiscordIpcClient rpc;
        private IPAddress ipAddress;
        private int updateInterval = 10;

        private bool ManualUpdate = false;
        private string LastTitleID = "";
        private long? time = null;
        private static Timer timer;
        private bool HasSeenMacPrompt = false;

        public MainForm()
        {
            InitializeComponent();
            listenThread = new Thread(TryConnect);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWME)
            {
                ShowMe();
            }
            base.WndProc(ref m);
        }

        private void ShowMe()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            // Asegurarse de que la ventana est� visible y al frente
            bool top = TopMost;
            TopMost = true;
            TopMost = top;
            Show();
            Activate();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (connectButton.Text == "Connect")
            {
                if (string.IsNullOrWhiteSpace(clientBox.Text))
                {
                    clientBox.Text = PresenceCommon.CoverResolver.DEFAULT_CLIENT_ID;
                }

                // Check and see if we have an IP
                // If we have an IP, prompt to swap to MAC Address
                if (IPAddress.TryParse(addressBox.Text, out ipAddress))
                {
                    if (!HasSeenMacPrompt)
                    {
                        HasSeenMacPrompt = true;

                        string message = "We've detected that you're using an IP to connect to your Vita. Connecting via MAC address may make it easier to reconnect to your device in case the IP changes." +
                                         "\n\nWould you like to swap to connecting via MAC address? \n(We'll only ask this once.)";

                        if (MessageBox.Show(message, "IP Detected", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            UseMacDefault.Checked = true;
                            IpToMac();
                        }
                        else
                            UseMacDefault.Checked = false;
                    }
                    else if (UseMacDefault.Checked == true)
                        IpToMac();
                }
                else
                {
                    // If in this block, means we dont have a valid IP.
                    // Check and see if it's a MAC Address
                    try
                    {
                        IPAddress.TryParse(Utils.GetIpByMac(addressBox.Text), out ipAddress);
                        if (ipAddress == null)
                        {
                            Show();
                            Activate();
                            UpdateStatus("Couldn't translate MAC to IP\nIs your Vita connected to Wifi?", Color.DarkRed);
                            SystemSounds.Exclamation.Play();
                            return;
                        }

                    }
                    catch (FormatException)
                    {
                        Show();
                        Activate();
                        UpdateStatus("Invalid IP or MAC Address", Color.DarkRed);
                        SystemSounds.Exclamation.Play();
                        return;
                    }
                }

                // Parse update interval
                if (!int.TryParse(updateIntervalBox.Text, out updateInterval))
                {
                    Show();
                    Activate();
                    UpdateStatus("Invalid update interval!\nPlease enter time in seconds.", Color.DarkRed);
                    SystemSounds.Exclamation.Play();
                    return;
                }

                listenThread.Start();

                connectButton.Text = "Disconnect";
                connectToolStripMenuItem.Text = "Disconnect";

                addressBox.Enabled = false;
                clientBox.Enabled = false;
                updateIntervalBox.Enabled = false;
                steamGridDbBox.Enabled = false;
            }
            else
            {
                // Dispose the IPC client first — this closes the pipe handle,
                // which unblocks any ReadFrameBlocking() call in the ReadLoop thread
                // so Thread.Abort() doesn't have to interrupt native I/O.
                try
                {
                    if (rpc != null && !rpc.IsDisposed)
                    {
                        rpc.ClearPresence();
                        rpc.Dispose();
                    }
                }
                catch { }

                try { if (client != null) client.Close(); } catch { }
                try { if (timer != null) timer.Dispose(); } catch { }

                try { listenThread?.Abort(); } catch { }

                listenThread = new Thread(TryConnect);
                UpdateStatus("", Color.Gray);
                connectButton.Text = "Connect";
                connectToolStripMenuItem.Text = "Connect";
                trayIcon.Icon = Resources.Disconnected;
                trayIcon.Text = "VitaPresence (Disconnected)";

                ipAddress = null;
                addressBox.Enabled = true;
                clientBox.Enabled = true;
                updateIntervalBox.Enabled = true;
                steamGridDbBox.Enabled = true;
                LastTitleID = "";
                time = null;
            }
        }

        private void OnConnectTimeout(object source, ElapsedEventArgs e)
        {
            LastTitleID = "";
            time = null;
        }

        private void SetUserInfoConnecting()
        {
            UpdateStatus("Attemping to connect to PS Vita...", Color.Gray);
            trayIcon.Icon = Resources.Disconnected;
            trayIcon.Text = "VitaPresence (Connecting...)";
        }

        private void TryConnect()
        {
            if (rpc != null && !rpc.IsDisposed)
            {
                rpc.ClearPresence();
                rpc.Dispose();
            }

            rpc = new DiscordIpcClient(clientBox.Text);
            if (!rpc.Initialize())
            {
                UpdateStatus("Could not connect to Discord!", Color.DarkRed);
                return;
            }

            // Create a timer that clears game info if connection is lost for 60s
            timer = new Timer()
            {
                Interval = 60000,
                SynchronizingObject = this,
                Enabled = false,
            };
            timer.Elapsed += new ElapsedEventHandler(OnConnectTimeout);

            SetUserInfoConnecting();

            while (true)
            {
                client = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveTimeout = 5500,
                    SendTimeout = 5500,
                };
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 0xCAFE);

                timer.Enabled = true;

                try
                {
                    IAsyncResult result = client.BeginConnect(localEndPoint, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(2000, true);
                    if (!success)
                    {
                        SetUserInfoConnecting();
                        client.Close();
                        if (rpc != null && !rpc.IsDisposed) rpc.ClearPresence();
                    }
                    else
                    {
                        timer.Enabled = false;
                        DataListenOne();
                        client.EndConnect(result);
                        client.Close();
                    }

                    Thread.Sleep(updateInterval * 1000); // wait before another connect
                }
                catch (ArgumentNullException)
                {
                    Thread.Sleep(1000);
                    IPAddress.TryParse(Utils.GetIpByMac(addressBox.Text), out ipAddress);
                }
                catch (SocketException e)
                {
                    UpdateStatus(e.Message, Color.Red);
                    Thread.Sleep(2000);
                    client.Close();
                    if (rpc != null && !rpc.IsDisposed) rpc.ClearPresence();
                    SetUserInfoConnecting();
                }
                catch (ThreadAbortException)
                {
                    return; // Disconnect was clicked — exit cleanly
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.GetType().Name}: {ex.Message}", Color.Red);
                    Thread.Sleep(2000);
                }
            }
        }

        private void DataListenOne()
        {
            ManualUpdate = true;
            byte[] bytes = new byte[600];
            int cnt = client.Receive(bytes);

            Title title = new Title(bytes);
            if (title.Magic == 0xCAFECAFE)
            {
                trayIcon.Icon = Resources.Connected;
                trayIcon.Text = "VitaPresence (Connected)";

                var coverResult = PresenceCommon.CoverResolver.ResolveCoverImageUrl(
                    title.Index == 0 ? "mainmenu" : title.TitleID,
                    title.Index == 0 ? "LiveArea" : title.TitleName,
                    "psv",
                    steamGridDbBox.Text,
                    clientBox.Text
                );
                string sourceInfo = coverResult.Item2;

                if (title.Index == 0)
                {
                    UpdateStatus($"In LiveArea ({sourceInfo})", Color.Green);
                }
                else
                {
                    UpdateStatus($"Playing [{title.TitleID}] ({sourceInfo})", Color.Green);
                }

                if (LastTitleID != title.TitleID)
                {
                    time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                if ((LastTitleID != title.TitleID) || ManualUpdate)
                {
                    if (rpc != null)
                    {
                        try
                        {
                            if (checkMainMenu.Checked == false && title.Index == 0)
                                rpc.ClearPresence();
                            else
                                rpc.SetPresence(PresenceCommon.Utils.CreateDiscordPresence(
                                    title, time, stateBox.Text,
                                    steamGridDbBox.Text, clientBox.Text,
                                    swapPresenceCheckbox.Checked, checkTime.Checked));
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus($"Discord error: {ex.GetType().Name}: {ex.Message}", Color.DarkRed);
                        }
                    }
                    ManualUpdate = false;
                    LastTitleID = title.TitleID;
                }
            }
            else
            {
                UpdateStatus("Invalid magic!", Color.Red);

                if (rpc != null && !rpc.IsDisposed) rpc.ClearPresence();
                return;
            }
        }

        private void IpToMac()
        {
            string macAddress = Utils.GetMacByIp(ipAddress.ToString());
            if (macAddress != null)
                addressBox.Text = macAddress;
            else
                MessageBox.Show("Can't convert to MAC Address! Sorry!");
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            string configPath = Utils.GetAppDataConfigPath();
            if (File.Exists(configPath))
            {
                Config cfg = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                checkTime.Checked = cfg.DisplayTimer;
                addressBox.Text = cfg.IP;
                stateBox.Text = cfg.State;
                clientBox.Text = string.IsNullOrWhiteSpace(cfg.Client) ? PresenceCommon.CoverResolver.DEFAULT_CLIENT_ID : cfg.Client;
                steamGridDbBox.Text = cfg.SteamGridDBApiKey ?? "";
                updateIntervalBox.Text = cfg.UpdateInterval;
                checkTray.Checked = cfg.AllowTray;
                checkMainMenu.Checked = cfg.DisplayMainMenu;
                swapPresenceCheckbox.Checked = cfg.SwapPresenceStyle;
                HasSeenMacPrompt = cfg.SeenAutoMacPrompt;
                UseMacDefault.Checked = cfg.AutoToMac;
                StartWithSystem.CheckedChanged -= StartWithSystem_CheckedChanged;
                StartWithSystem.Checked = cfg.StartWithSystem;
                StartWithSystem.CheckedChanged += StartWithSystem_CheckedChanged;
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            listenThread.Abort();
            if (rpc != null && !rpc.IsDisposed)
            {
                rpc.ClearPresence();
                rpc.Dispose();
            }

            if (client != null) client.Close();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && checkTray.Checked)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                if (timer != null) timer.Dispose();

                Config cfg = new Config()
                {
                    IP = addressBox.Text,
                    Client = clientBox.Text,
                    State = stateBox.Text,
                    UpdateInterval = updateIntervalBox.Text,
                    SteamGridDBApiKey = steamGridDbBox.Text,
                    DisplayTimer = checkTime.Checked,
                    AllowTray = checkTray.Checked,
                    DisplayMainMenu = checkMainMenu.Checked,
                    SwapPresenceStyle = swapPresenceCheckbox.Checked,
                    SeenAutoMacPrompt = HasSeenMacPrompt,
                    AutoToMac = UseMacDefault.Checked,
                    StartWithSystem = StartWithSystem.Checked
                };

                string configPath = Utils.GetAppDataConfigPath();
                File.WriteAllText(configPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
        }

        private void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            Activate();
        }

        private void UpdateStatus(string text, Color color)
        {
            // BeginInvoke is non-blocking — the listen thread never waits on the UI thread,
            // preventing deadlocks when Disconnect is clicked mid-update.
            BeginInvoke((MethodInvoker)(() =>
            {
                statusLabel.Text = text;
                statusLabel.ForeColor = color;
            }));
        }

        private void CheckTime_CheckedChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void BigKeyBox_TextChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void SKeyBox_TextChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void StateBox_TextChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void BigTextBox_TextChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void TrayExitMenuItem_Click(object sender, EventArgs e) => Application.Exit();

        private void LinkLabel1_LinkClicked_1(object sender, LinkLabelLinkClickedEventArgs e) => Process.Start($"https://discordapp.com/developers/applications/{clientBox.Text}");

        private void CheckMainMenu_CheckedChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void SwapPresenceCheckbox_CheckedChanged(object sender, EventArgs e) => ManualUpdate = true;

        private void UseMacDefault_CheckedChanged(object sender, EventArgs e) => HasSeenMacPrompt = true;

        private void StartWithSystem_CheckedChanged(object sender, EventArgs e)
        {
            if (StartWithSystem.Checked)
            {
                AddApplicationToStartup();
            }
            else
            {
                RemoveApplicationFromStartup();
            }
        }

        private void AddApplicationToStartup()
        {
            string appName = "VitaPresence";
            string appPath = Application.ExecutablePath;
            string configPath = Utils.GetAppDataConfigPath();

            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key.SetValue(appName, $"\"{appPath}\" --config \"{configPath}\"");
            MessageBox.Show("Added to system startup.");
        }

        private void RemoveApplicationFromStartup()
        {
            string appName = "VitaPresence";

            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key.DeleteValue(appName, false);
            MessageBox.Show("Removed from system startup.");
        }
    }
}
