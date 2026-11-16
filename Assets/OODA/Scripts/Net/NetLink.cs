using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ooda.Net
{
    /// <summary>What a message on the link is.</summary>
    public enum Msg : byte { Hello = 1, Setup = 2, Plans = 3, Turn = 4, Resync = 5, Snapshot = 6, Draft = 7, Ping = 8, Bye = 9 }

    /// <summary>
    /// A link to the one other machine in the game, carrying whole messages in order. NetLink is the direct one (TCP, by
    /// address); the game's room codes go through a relay (Online/RelayLink.cs), so nobody's address is ever typed or shown.
    /// </summary>
    public interface INetLink : IDisposable
    {
        bool IsHost { get; }
        /// <summary>Connected to the other machine, and not lost since.</summary>
        bool Connected { get; }
        /// <summary>The connection failed, dropped, went silent, or the other side left.</summary>
        bool Lost { get; }
        /// <summary>Why it was lost or never made, for the screen.</summary>
        string Error { get; }
        /// <summary>The port a direct host listens on; 0 through the relay.</summary>
        int Port { get; }
        /// <summary>Who is at the other end, for the screen (an address, or the room).</summary>
        string PeerAddress { get; }
        /// <summary>Send one message. Does nothing once lost.</summary>
        void Send(Msg type, byte[] data);
        /// <summary>The next message that arrived, oldest first.</summary>
        bool TryReceive(out Msg type, out byte[] data);
    }

    /// <summary>
    /// One TCP connection between the host and a friend, carrying whole messages: a length, a type byte, then the bytes.
    /// Reading happens on a thread of its own and lands in an inbox the game empties each frame; a second thread sends a
    /// ping every couple of seconds and calls the link lost when nothing has been heard for a while. Sending is from the
    /// main thread. Pure C#, no Unity, so it can be checked on localhost without the game.
    /// </summary>
    public sealed class NetLink : INetLink
    {
        public const int DefaultPort = 7777;
        /// <summary>Nothing heard for this long (pings included) and the link counts as lost.</summary>
        public const double SilenceSec = 20;
        const double PingSec = 2;
        const int MaxMessage = 16 << 20;

        public bool IsHost { get; private set; }
        /// <summary>Connected to the other machine, and not lost since.</summary>
        public bool Connected => connected && !lost;
        /// <summary>The connection failed, dropped, went silent, or the other side left.</summary>
        public bool Lost => lost;
        /// <summary>Why it was lost or never made, for the screen.</summary>
        public string Error { get; private set; }
        /// <summary>The port the host listens on (useful when asked for port 0, any free one).</summary>
        public int Port { get; private set; }
        public string PeerAddress { get; private set; }

        volatile bool connected, lost, closing;
        TcpListener listener;
        TcpClient client;
        NetworkStream stream;
        readonly object sendLock = new object();
        readonly ConcurrentQueue<(Msg type, byte[] data)> inbox = new ConcurrentQueue<(Msg, byte[])>();
        long lastHeardTicks, lastSentTicks;

        NetLink() { }

        /// <summary>Listen for one friend on this port (the self-tests and a typed address; friends meet through Relay room codes).</summary>
        public static NetLink Host(int port = DefaultPort)
        {
            var l = new NetLink { IsHost = true };
            l.listener = new TcpListener(IPAddress.Any, port);
            l.listener.Start();
            l.Port = ((IPEndPoint)l.listener.LocalEndpoint).Port;
            new Thread(l.Accept) { IsBackground = true, Name = "OODA net accept" }.Start();
            return l;
        }

        /// <summary>Connect to a host by name or address, giving up after timeoutSec.</summary>
        public static NetLink Join(string address, int port = DefaultPort, double timeoutSec = 10)
        {
            var l = new NetLink { IsHost = false, Port = port, PeerAddress = address };
            new Thread(() => l.Connect(address, port, timeoutSec)) { IsBackground = true, Name = "OODA net connect" }.Start();
            return l;
        }

        /// <summary>"192.168.1.20:7777" or "my-pc" (the default port): the address and port to Join.</summary>
        public static bool ParseAddress(string text, out string host, out int port)
        {
            host = (text ?? "").Trim(); port = DefaultPort;
            if (host.Length == 0) return false;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && host.IndexOf(':') == colon && int.TryParse(host.Substring(colon + 1), out int p) && p > 0 && p < 65536)
            { port = p; host = host.Substring(0, colon); }
            return host.Length > 0;
        }

        void Accept()
        {
            try
            {
                var c = listener.AcceptTcpClient();
                listener.Stop();   // one friend
                PeerAddress = (c.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString();
                Begin(c);
            }
            catch (Exception e) { if (!closing) Fail("Could not take the connection: " + e.Message); }
        }

        void Connect(string address, int port, double timeoutSec)
        {
            try
            {
                var c = new TcpClient();
                var t = c.ConnectAsync(address, port);
                if (!t.Wait(TimeSpan.FromSeconds(timeoutSec))) { c.Close(); Fail($"No answer from {address}:{port}. Is the host's game hosting, and is the address right?"); return; }
                if (t.IsFaulted) throw t.Exception.GetBaseException();
                Begin(c);
            }
            catch (Exception e) { if (!closing) Fail($"Could not reach {address}:{port}: {(e is AggregateException ? e.GetBaseException() : e).Message}"); }
        }

        void Begin(TcpClient c)
        {
            client = c;
            client.NoDelay = true;
            stream = client.GetStream();
            Interlocked.Exchange(ref lastHeardTicks, DateTime.UtcNow.Ticks);
            connected = true;
            new Thread(ReadLoop) { IsBackground = true, Name = "OODA net read" }.Start();
            new Thread(KeepAlive) { IsBackground = true, Name = "OODA net ping" }.Start();
        }

        void ReadLoop()
        {
            try
            {
                var head = new byte[5];
                while (!lost)
                {
                    ReadExactly(head, 5);
                    int len = BitConverter.ToInt32(head, 0);
                    if (len < 0 || len > MaxMessage) throw new InvalidDataException($"a message of {len} bytes");
                    var data = new byte[len];
                    ReadExactly(data, len);
                    Interlocked.Exchange(ref lastHeardTicks, DateTime.UtcNow.Ticks);
                    var type = (Msg)head[4];
                    if (type == Msg.Ping) continue;
                    if (type == Msg.Bye) { Fail("Your friend left the game."); return; }
                    inbox.Enqueue((type, data));
                }
            }
            catch (Exception e) { if (!closing) Fail("The connection dropped: " + e.Message); }
        }

        void ReadExactly(byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int k = stream.Read(buf, got, n - got);
                if (k <= 0) throw new EndOfStreamException("the other side closed it");
                got += k;
            }
        }

        void KeepAlive()
        {
            while (!lost && !closing)
            {
                Thread.Sleep(250);
                var now = DateTime.UtcNow.Ticks;
                if (TimeSpan.FromTicks(now - Interlocked.Read(ref lastSentTicks)).TotalSeconds >= PingSec) Send(Msg.Ping, new byte[0]);
                if (TimeSpan.FromTicks(now - Interlocked.Read(ref lastHeardTicks)).TotalSeconds >= SilenceSec) Fail("Nothing heard from your friend for 20 seconds.");
            }
        }

        void Fail(string why)
        {
            if (lost) return;
            Error = why;
            lost = true;
            try { client?.Close(); } catch { }
            try { listener?.Stop(); } catch { }
        }

        /// <summary>Send one message. Does nothing once lost (the game sees Lost and carries on alone).</summary>
        public void Send(Msg type, byte[] data)
        {
            if (!connected || lost) return;
            data = data ?? new byte[0];
            var head = new byte[5];
            BitConverter.GetBytes(data.Length).CopyTo(head, 0);
            head[4] = (byte)type;
            try
            {
                lock (sendLock)
                {
                    stream.Write(head, 0, 5);
                    if (data.Length > 0) stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
                Interlocked.Exchange(ref lastSentTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception e) { if (!closing) Fail("The connection dropped: " + e.Message); }
        }

        /// <summary>The next message that arrived, oldest first.</summary>
        public bool TryReceive(out Msg type, out byte[] data)
        {
            if (inbox.TryDequeue(out var m)) { type = m.type; data = m.data; return true; }
            type = 0; data = null; return false;
        }

        /// <summary>Say goodbye and close.</summary>
        public void Dispose()
        {
            if (Connected) Send(Msg.Bye, new byte[0]);
            closing = true;
            Fail(Error ?? "You left.");
        }
    }
}
