using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ooda.Net;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Ooda.Online
{
    /// <summary>
    /// The link between the host and a friend through Unity Relay: the host asks for a room and gets a short code, the
    /// friend types the code, and every message goes by way of the relay, so neither machine learns or shows the other's
    /// address and no router needs setting up. It carries the same whole messages as the direct NetLink (a type byte, then
    /// the bytes), in order and without loss (Unity Transport's reliable pipeline, with fragmentation for the big ones).
    /// The project must be linked to a Unity Cloud project (Edit > Project Settings > Services) for the relay to answer.
    /// Everything here runs on the main thread: a hidden object pumps the driver every frame, across scene changes.
    /// </summary>
    public sealed class RelayLink : INetLink
    {
        /// <summary>The biggest message the pipeline carries (reliable and fragmented it tops out near 88 KB); a battle's snapshot is about 1 KB.</summary>
        const int MaxMessage = 60000;
        const double PingSec = 2, SilenceSec = NetLink.SilenceSec;

        public bool IsHost { get; private set; }
        public bool Connected => connected && !lost;
        public bool Lost => lost;
        public string Error { get; private set; }
        public int Port => 0;
        public string PeerAddress { get; private set; }
        /// <summary>The host's room code once the relay has given one (null until then, and on the friend's side what was typed).</summary>
        public string JoinCode { get; private set; }

        bool connected, lost, closing;
        NetworkDriver driver;
        NetworkPipeline pipeline;
        NetworkConnection peer;
        readonly Queue<(Msg type, byte[] data)> inbox = new Queue<(Msg, byte[])>();
        readonly Queue<byte[]> outbox = new Queue<byte[]>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastHeard, lastSent;
        Pumper pumper;

        RelayLink() { }

        /// <summary>Open a room for one friend. JoinCode is set a moment later, once the relay answers.</summary>
        public static RelayLink Host()
        {
            var l = new RelayLink { IsHost = true };
            l.StartPump();
            l.BeginHost();
            return l;
        }

        /// <summary>Join the room with this code.</summary>
        public static RelayLink Join(string code)
        {
            code = CleanCode(code);
            var l = new RelayLink { IsHost = false, JoinCode = code, PeerAddress = "room " + code };
            l.StartPump();
            l.BeginJoin(code);
            return l;
        }

        /// <summary>A room code as typed: no spaces, capitals.</summary>
        public static string CleanCode(string text) => (text ?? "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

        /// <summary>True when what was typed is a room code, not an address (an address has a dot or a colon in it, or is localhost).</summary>
        public static bool LooksLikeCode(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length < 4 || t.Length > 12 || t.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char c in t) if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-') return false;
            return true;
        }

        static async System.Threading.Tasks.Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        async void BeginHost()
        {
            try
            {
                await SignIn();
                Allocation room = await RelayService.Instance.CreateAllocationAsync(1);   // one friend
                string code = await RelayService.Instance.GetJoinCodeAsync(room.AllocationId);
                if (closing || lost) return;
                Open(room.ToRelayServerData("dtls"));
                if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0 || driver.Listen() != 0) { Fail("Could not open the room: the relay connection would not start."); return; }
                JoinCode = code;
            }
            catch (Exception e) { if (!closing) Fail("Could not open a room: " + Why(e)); }
        }

        async void BeginJoin(string code)
        {
            try
            {
                await SignIn();
                JoinAllocation room = await RelayService.Instance.JoinAllocationAsync(code);
                if (closing || lost) return;
                var data = room.ToRelayServerData("dtls");
                Open(data);
                if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0) { Fail("Could not join: the relay connection would not start."); return; }
                peer = driver.Connect(data.Endpoint);
                lastHeard = clock.Elapsed.TotalSeconds;   // the silence clock covers the connecting too
            }
            catch (Exception e) { if (!closing) Fail($"Could not join room {code}: {Why(e)}"); }
        }

        /// <summary>The service's own words, with a plainer line for the two things that usually go wrong.</summary>
        static string Why(Exception e)
        {
            string m = e.Message ?? e.GetType().Name;
            if (m.IndexOf("join code", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                return "no room has that code. Check it with your friend (codes stop working when the host leaves).";
            if (m.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 && m.IndexOf("link", StringComparison.OrdinalIgnoreCase) >= 0)
                return "this build is not linked to the online service.";
            return m;
        }

        void Open(RelayServerData data)
        {
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref data);
            settings.WithFragmentationStageParameters(payloadCapacity: MaxMessage + 16);
            driver = NetworkDriver.Create(settings);
            pipeline = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        }

        // ---------- every frame ----------

        void StartPump()
        {
            var go = new GameObject("Relay link") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            pumper = go.AddComponent<Pumper>();
            pumper.link = this;
        }

        /// <summary>Keeps the driver turning every frame whatever scene is up (the relay drops a room that goes quiet), and closes it if the game quits.</summary>
        sealed class Pumper : MonoBehaviour
        {
            public RelayLink link;
            void Update() => link?.Pump();
            void OnApplicationQuit() => link?.Dispose();
        }

        void Pump()
        {
            if (!driver.IsCreated || closing) return;
            driver.ScheduleUpdate().Complete();
            double now = clock.Elapsed.TotalSeconds;

            if (IsHost)
            {
                NetworkConnection c;
                while ((c = driver.Accept()) != default)
                {
                    if (peer.IsCreated) { driver.Disconnect(c); continue; }   // one friend
                    peer = c;
                    PeerAddress = "your room";
                    connected = true;
                    lastHeard = now;
                }
            }

            NetworkEvent.Type ev;
            while ((ev = driver.PopEvent(out var from, out var reader)) != NetworkEvent.Type.Empty)
            {
                if (from != peer) continue;
                if (ev == NetworkEvent.Type.Connect) { connected = true; lastHeard = now; }
                else if (ev == NetworkEvent.Type.Disconnect) { peer = default; Fail(connected ? "The connection dropped." : "Could not reach the room. Is the host still waiting?"); return; }
                else if (ev == NetworkEvent.Type.Data)
                {
                    lastHeard = now;
                    if (reader.Length < 1) continue;
                    var buf = new NativeArray<byte>(reader.Length, Allocator.Temp);
                    reader.ReadBytes(buf);
                    var type = (Msg)buf[0];
                    var data = new byte[buf.Length - 1];
                    if (data.Length > 0) NativeArray<byte>.Copy(buf, 1, data, 0, data.Length);
                    buf.Dispose();
                    if (type == Msg.Ping) continue;
                    if (type == Msg.Bye) { Fail("Your friend left the game."); return; }
                    inbox.Enqueue((type, data));
                }
            }
            if (lost) return;

            if (connected && now - lastSent >= PingSec && outbox.Count == 0) Send(Msg.Ping, null);
            Flush();
            if ((connected || !IsHost && peer.IsCreated) && now - lastHeard >= SilenceSec)
                Fail(connected ? "Nothing heard from your friend for 20 seconds." : "No answer from the room. Is the host still waiting?");
        }

        /// <summary>Send what is waiting, oldest first; what the reliable window has no room for yet waits for the next frame.</summary>
        void Flush()
        {
            while (connected && !lost && outbox.Count > 0)
            {
                var bytes = outbox.Peek();
                if (driver.BeginSend(pipeline, peer, out var writer) != 0) return;
                var native = new NativeArray<byte>(bytes, Allocator.Temp);
                writer.WriteBytes(native);
                native.Dispose();
                int sent = driver.EndSend(writer);
                if (sent == (int)Unity.Networking.Transport.Error.StatusCode.NetworkSendQueueFull) return;
                if (sent < 0) { Fail($"The connection dropped (send error {sent})."); return; }
                outbox.Dequeue();
                lastSent = clock.Elapsed.TotalSeconds;
            }
        }

        void Fail(string why)
        {
            if (lost) return;
            Error = why;
            lost = true;
            Shut();
        }

        void Shut()
        {
            if (driver.IsCreated)
            {
                if (peer.IsCreated) { driver.Disconnect(peer); driver.ScheduleUpdate().Complete(); }
                driver.Dispose();
            }
            peer = default;
            if (pumper != null) { pumper.link = null; UnityEngine.Object.Destroy(pumper.gameObject); pumper = null; }
        }

        public void Send(Msg type, byte[] data)
        {
            if (!connected || lost) return;
            int n = data?.Length ?? 0;
            if (n > MaxMessage) { Fail($"A message of {n} bytes is too big for the relay."); return; }
            var bytes = new byte[n + 1];
            bytes[0] = (byte)type;
            if (n > 0) Buffer.BlockCopy(data, 0, bytes, 1, n);
            outbox.Enqueue(bytes);
        }

        public bool TryReceive(out Msg type, out byte[] data)
        {
            if (inbox.Count > 0) { (type, data) = inbox.Dequeue(); return true; }
            type = 0; data = null; return false;
        }

        /// <summary>Say goodbye and close the room.</summary>
        public void Dispose()
        {
            if (closing) return;
            if (Connected && driver.IsCreated)
            {
                Send(Msg.Bye, null);
                Flush();
                driver.ScheduleUpdate().Complete();
            }
            closing = true;
            Error = Error ?? "You left.";
            lost = true;
            Shut();
        }
    }
}
