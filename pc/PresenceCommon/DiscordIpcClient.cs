using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PresenceCommon
{
    /// <summary>
    /// Lightweight Discord Rich Presence client using the raw local IPC protocol.
    ///
    /// Design mirrors pypresence (the Python library used by PS3-Rich-Presence-for-Discord):
    ///   - Fully synchronous — no background reader thread
    ///   - After every write, the response is read back on the same thread
    ///   - PINGs from Discord are handled inline during the read cycle
    ///
    /// This exposes the "name" field in the SET_ACTIVITY payload, which overrides the bold
    /// Discord application name shown in the presence card on a per-update basis.
    /// </summary>
    public class DiscordIpcClient : IDisposable
    {
        // Discord IPC opcodes
        private const int OP_HANDSHAKE = 0;
        private const int OP_FRAME     = 1;
        private const int OP_CLOSE     = 2;
        private const int OP_PING      = 3;
        private const int OP_PONG      = 4;

        private readonly string _clientId;
        private readonly object _lock = new object();

        private NamedPipeClientStream _pipe;

        public bool IsDisposed  { get; private set; }
        public bool IsConnected => _pipe != null && _pipe.IsConnected && !IsDisposed;

        public DiscordIpcClient(string clientId)
        {
            _clientId = clientId;
        }

        /// <summary>
        /// Connects to Discord's local IPC pipe and performs the handshake.
        /// Returns true on success.
        /// </summary>
        public bool Initialize()
        {
            for (int i = 0; i <= 9; i++)
            {
                try
                {
                    var pipe = new NamedPipeClientStream(
                        ".", $"discord-ipc-{i}",
                        PipeDirection.InOut,
                        PipeOptions.None);
                    pipe.Connect(2000);
                    _pipe = pipe;
                    break;
                }
                catch { }
            }

            if (_pipe == null || !_pipe.IsConnected)
                return false;

            try
            {
                // Send HANDSHAKE
                WriteFrame(OP_HANDSHAKE,
                    JsonConvert.SerializeObject(new { v = 1, client_id = _clientId }));

                // Read response — expect READY
                var (opcode, json) = ReadFrameOrPing();
                if (opcode != OP_FRAME)
                    return false;

                var obj = JObject.Parse(json);
                return obj["cmd"]?.ToString() == "DISPATCH"
                    && obj["evt"]?.ToString() == "READY";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Sends a rich presence activity update.</summary>
        public void SetPresence(VitaActivity activity)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Discord IPC pipe is not connected.");

            var activityObj = new JObject
            {
                ["type"] = 0
            };

            // The "name" field overrides the bold app title in the Discord presence card
            if (!string.IsNullOrWhiteSpace(activity.Name))
                activityObj["name"] = activity.Name;

            if (!string.IsNullOrWhiteSpace(activity.Details))
                activityObj["details"] = activity.Details;

            if (!string.IsNullOrWhiteSpace(activity.State))
                activityObj["state"] = activity.State;

            if (activity.TimestampStart.HasValue)
            {
                activityObj["timestamps"] = new JObject
                {
                    ["start"] = activity.TimestampStart.Value
                };
            }

            var assets = new JObject();
            if (!string.IsNullOrWhiteSpace(activity.LargeImageKey))
                assets["large_image"] = activity.LargeImageKey;
            if (!string.IsNullOrWhiteSpace(activity.LargeImageText))
                assets["large_text"] = activity.LargeImageText;
            if (assets.Count > 0)
                activityObj["assets"] = assets;

            var payload = new JObject
            {
                ["cmd"]   = "SET_ACTIVITY",
                ["nonce"] = Guid.NewGuid().ToString("N"),
                ["args"]  = new JObject
                {
                    ["pid"]      = Process.GetCurrentProcess().Id,
                    ["activity"] = activityObj
                }
            };

            lock (_lock)
            {
                WriteFrame(OP_FRAME, payload.ToString(Formatting.None));
                ReadFrameOrPing(); // read and discard the SET_ACTIVITY response (mirrors pypresence)
            }
        }

        /// <summary>Clears the current presence entirely.</summary>
        public void ClearPresence()
        {
            if (!IsConnected) return;

            var payload = new JObject
            {
                ["cmd"]   = "SET_ACTIVITY",
                ["nonce"] = Guid.NewGuid().ToString("N"),
                ["args"]  = new JObject
                {
                    ["pid"]      = Process.GetCurrentProcess().Id,
                    ["activity"] = JValue.CreateNull()
                }
            };

            try
            {
                lock (_lock)
                {
                    WriteFrame(OP_FRAME, payload.ToString(Formatting.None));
                    ReadFrameOrPing();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Reads the next frame from Discord, handling any PINGs inline (just like pypresence does).
        /// Returns the first non-PING frame received.
        /// </summary>
        private (int opcode, string json) ReadFrameOrPing()
        {
            while (true)
            {
                var (opcode, json) = ReadFrameBlocking();
                if (opcode == OP_PING)
                {
                    WriteFrame(OP_PONG, json); // respond to ping, then keep reading
                    continue;
                }
                if (opcode == OP_CLOSE)
                    throw new IOException($"Discord sent CLOSE: {json}");
                return (opcode, json);
            }
        }

        private void WriteFrame(int opcode, string json)
        {
            byte[] body   = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(opcode),      0, header, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, header, 4, 4);

            _pipe.Write(header, 0, 8);
            _pipe.Write(body,   0, body.Length);
            _pipe.Flush();
        }

        private (int opcode, string json) ReadFrameBlocking()
        {
            byte[] header = ReadExact(8);
            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            byte[] body = ReadExact(length);
            return (opcode, Encoding.UTF8.GetString(body));
        }

        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = _pipe.Read(buf, read, count - read);
                if (n == 0) throw new EndOfStreamException("Discord IPC pipe closed.");
                read += n;
            }
            return buf;
        }
    }
}
