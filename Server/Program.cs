// See https://aka.ms/new-console-template for more information
using CovjeceNeLjutiSe.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Server
{
    internal class Program
    {
        private const int UdpJoinPort = 5000;
        private const int TcpGamePort = 6000;

        private static readonly TimeSpan FirstJoinWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RejoinIdleTimeout = TimeSpan.FromSeconds(60);

        private const int SelectTimeoutMicros = 200_000; // 200ms

        private sealed class ClientConn
        {
            public Socket Sock { get; }
            public StringBuilder Buffer { get; } = new StringBuilder();
            public int? PlayerId { get; set; } = null;
            public bool Welcomed { get; set; } = false;

            public ClientConn(Socket sock) => Sock = sock;

            public void Close()
            {
                try { Sock.Shutdown(SocketShutdown.Both); } catch { }
                try { Sock.Close(); } catch { }
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("SERVER pokrenut (MULTIPLEX + CLEAN).");

            using var udp = new UdpClient(UdpJoinPort);
            udp.Client.Blocking = false;

            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, TcpGamePort));
            listener.Listen(50);
            listener.Blocking = false;

            Console.WriteLine($"UDP JOIN port: {UdpJoinPort}");
            Console.WriteLine($"TCP GAME port: {TcpGamePort}");

            var clients = new List<ClientConn>();

            int? expectedSeats = null;
            bool firstMatch = true;

            DateTime lastActivityUtc = DateTime.UtcNow;

            // Za "q" gasenje
            var quitPlayers = new HashSet<int>();

            while (true)
            {
                // ===== NEW MATCH =====
                var gm = new GameManager();
                gm.CreateGame(boardSize: 40, maxPlayers: 4, figuresPerPlayer: 4);

                bool joinPhase = true;
                bool gameStarted = false;

                int seatsToFill;
                DateTime joinDeadlineUtc = DateTime.UtcNow;

                if (firstMatch)
                {
                    seatsToFill = 4;
                    joinDeadlineUtc = DateTime.UtcNow + FirstJoinWindow;
                    Console.WriteLine($"[GAME 1] Cekam JOIN poruke {FirstJoinWindow.TotalSeconds:0}s...");
                }
                else
                {
                    seatsToFill = expectedSeats!.Value;
                    Console.WriteLine($"[NEW GAME] Cekam da se prijavi ukupno {seatsToFill} igraca (popunjavanje mesta)...");
                    lastActivityUtc = DateTime.UtcNow;
                }

                while (true)
                {
                    // =======================
                    // 1) UDP JOIN polling
                    // =======================
                    if (joinPhase)
                    {
                        while (udp.Available > 0)
                        {
                            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                            var data = udp.Receive(ref remote);
                            var msg = Encoding.UTF8.GetString(data);
                            lastActivityUtc = DateTime.UtcNow;

                            if (!GameManager.TryParseJoin(msg, out var name, out var err))
                            {
                                SendUdp(udp, remote, $"ERROR|{err}");
                                continue;
                            }

                            // limit seats
                            if (firstMatch)
                            {
                                if (gm.State.Players.Count >= 4)
                                {
                                    SendUdp(udp, remote, "ERROR|Igra je vec popunjena.");
                                    continue;
                                }
                            }
                            else
                            {
                                if (gm.State.Players.Count >= seatsToFill)
                                {
                                    SendUdp(udp, remote, "ERROR|Igra je vec popunjena.");
                                    continue;
                                }
                            }

                            try
                            {
                                var p = gm.RegisterPlayer(name);
                                SendUdp(udp, remote, $"TCPPORT|{TcpGamePort}|PLAYERID|{p.Id}");
                                Console.WriteLine($"JOIN: {p.Name} UDP={remote} => PLAYERID={p.Id}");
                            }
                            catch (Exception ex)
                            {
                                SendUdp(udp, remote, $"ERROR|{ex.Message}");
                            }
                        }

                        if (firstMatch)
                        {
                            if (DateTime.UtcNow >= joinDeadlineUtc)
                            {
                                joinPhase = false;
                                Console.WriteLine($"JOIN prozor istekao. Prijavljeno igraca: {gm.State.Players.Count}");
                                if (gm.State.Players.Count == 0)
                                {
                                    Console.WriteLine("Nema igraca. Gasim server.");
                                    goto SERVER_END;
                                }
                                gm.AssignStartPositions();
                            }
                        }
                        else
                        {
                            if (gm.State.Players.Count >= seatsToFill)
                            {
                                joinPhase = false;
                                Console.WriteLine($"Popunjena mesta: {gm.State.Players.Count}/{seatsToFill}. Igra krece.");
                                gm.AssignStartPositions();
                            }
                        }

                        // posle gameover: ne gasi odmah, nego kad nema aktivnosti
                        if (!firstMatch && joinPhase && clients.Count == 0 && gm.State.Players.Count == 0)
                        {
                            if (DateTime.UtcNow - lastActivityUtc > RejoinIdleTimeout)
                            {
                                Console.WriteLine("Nema aktivnosti posle kraja igre. Server se gasi.");
                                goto SERVER_END;
                            }
                        }
                    }

                    // =======================
                    // 2) MULTIPLEX SELECT
                    // =======================
                    var readList = new List<Socket> { listener };
                    readList.AddRange(clients.Select(c => c.Sock));

                    try { Socket.Select(readList, null, null, SelectTimeoutMicros); } catch { }

                    // Accept novih TCP konekcija
                    if (readList.Contains(listener))
                    {
                        while (true)
                        {
                            try
                            {
                                var sock = listener.Accept();
                                sock.Blocking = false;
                                sock.NoDelay = true;
                                clients.Add(new ClientConn(sock));
                                lastActivityUtc = DateTime.UtcNow;
                                Console.WriteLine($"TCP CONNECT accepted. Total TCP conns: {clients.Count}");
                            }
                            catch (SocketException)
                            {
                                break;
                            }
                        }
                    }

                    // Obrada TCP poruka
                    foreach (var c in clients.ToList())
                    {
                        if (!readList.Contains(c.Sock))
                            continue;

                        if (!TryReceiveLines(c, out var lines))
                        {
                            Console.WriteLine($"TCP DISCONNECT (PlayerId={c.PlayerId?.ToString() ?? "?"})");
                            c.Close();
                            clients.Remove(c);
                            continue;
                        }

                        if (lines.Count > 0) lastActivityUtc = DateTime.UtcNow;

                        foreach (var line in lines)
                        {
                            // QUIT
                            if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                            {
                                if (c.PlayerId.HasValue)
                                {
                                    quitPlayers.Add(c.PlayerId.Value);
                                    Console.WriteLine($"QUIT received from PlayerId={c.PlayerId.Value} ({quitPlayers.Count}/{expectedSeats ?? 0})");
                                }

                                c.Close();
                                clients.Remove(c);

                                if (!firstMatch && expectedSeats.HasValue && quitPlayers.Count >= expectedSeats.Value)
                                {
                                    Console.WriteLine("Svi igraci su izasli (QUIT). Server se gasi.");
                                    goto SERVER_END;
                                }
                                continue;
                            }

                            // HELLO
                            if (GameManager.TryParseHello(line, out var pid))
                            {
                                c.PlayerId = pid;
                                Console.WriteLine($"HELLO received => PLAYERID={pid}");
                                continue;
                            }

                            // ACK
                            if (line.Equals("ACK", StringComparison.OrdinalIgnoreCase))
                            {
                                if (gm.OnAck(c.PlayerId))
                                {
                                    SendYourTurnToCurrent(gm, clients);
                                }
                                continue;
                            }

                            // MOVE
                            if (GameManager.TryParseMove(line, out var move, out var parseErr))
                            {
                                if (!gameStarted)
                                {
                                    SendLine(c.Sock, "MOVERESULT|INVALID|Igra nije pocela.");
                                    continue;
                                }

                                if (!gm.CanAcceptMove(move, c.PlayerId, out var err))
                                {
                                    SendLine(c.Sock, $"MOVERESULT|INVALID|{err}");
                                    continue;
                                }

                                var ok = gm.ApplyMoveWithSafeHouseAndEating(move, out var detail, out var gameOver);
                                if (!ok)
                                {
                                    SendLine(c.Sock, $"MOVERESULT|INVALID|{detail}");
                                    SendLine(c.Sock, "YOURTURN");
                                    gm.MarkYourTurnSent();
                                    continue;
                                }

                                SendLine(c.Sock, $"MOVERESULT|OK|{detail}");
                                Broadcast(clients, $"STATE|{gm.BuildStateSummary()}");

                                var jsonReport = gm.BuildSerializedGameReport();

                                try
                                {
                                    var pretty = JsonSerializer.Serialize(
                                        JsonSerializer.Deserialize<JsonElement>(jsonReport),
                                        new JsonSerializerOptions { WriteIndented = true }
                                    );

                                    Console.WriteLine("STATE|\n" + pretty);
                                }
                                catch
                                {
                                    Console.WriteLine("STATE|" + jsonReport);
                                }

                                Broadcast(clients, "STATE|" + jsonReport);

                                if (gameOver || gm.State.IsFinished)
                                {
                                    var ranking = gm.BuildRanking();

                                    Broadcast(clients, $"GAMEOVER|WINNER|{ranking[0].Name}|SAFEHOUSE|{ranking[0].SafeHouse}");
                                    Broadcast(clients, GameManager.BuildRankMessage(ranking));
                                    Broadcast(clients, "REJOIN");

                                    Console.WriteLine("GAME OVER. Poslata rang lista i REJOIN.");

                                    expectedSeats = gm.State.Players.Count;
                                    firstMatch = false;
                                    quitPlayers.Clear();

                                    foreach (var cc in clients) cc.Close();
                                    clients.Clear();

                                    goto NEXT_MATCH;
                                }

                                SendLine(c.Sock, "WAITACK");
                                continue;
                            }

                            SendLine(c.Sock, "ERROR|Nepoznata poruka.");
                        }
                    }

                    // =======================
                    // 3) START igre
                    // =======================
                    if (!gameStarted && !joinPhase)
                    {
                        var mappedCount = clients.Count(x => x.PlayerId.HasValue);
                        if (mappedCount >= gm.State.Players.Count)
                        {
                            foreach (var p in gm.State.Players)
                            {
                                var conn = clients.FirstOrDefault(x => x.PlayerId == p.Id);
                                if (conn != null && !conn.Welcomed)
                                {
                                    SendLine(conn.Sock,
                                        $"WELCOME|PLAYERID|{p.Id}|INDEX|{p.Index}|START|{p.StartPosition}|BOARDSIZE|{gm.State.BoardSize}");
                                    conn.Welcomed = true;
                                }
                            }

                            gameStarted = true;

                            // prvi potez
                            SendYourTurnToCurrent(gm, clients);

                            Console.WriteLine("Igra pocinje (CLEAN).");
                        }
                    }
                }

            NEXT_MATCH:
                continue;
            }

        SERVER_END:
            foreach (var c in clients) c.Close();
            try { listener.Close(); } catch { }
            Console.WriteLine("Server zavrsio.");
        }

        // ===================== Networking helpers =====================

        private static void SendYourTurnToCurrent(GameManager gm, List<ClientConn> clients)
        {
            if (!gm.CurrentTurnPlayerId.HasValue) return;
            int pid = gm.CurrentTurnPlayerId.Value;

            ClientConn? conn = null;
            foreach (var cc in clients)
            {
                if (cc.PlayerId == pid) { conn = cc; break; }
            }
            if (conn == null) return;

            SendLine(conn.Sock, "YOURTURN");
            gm.MarkYourTurnSent();
        }

        private static void SendUdp(UdpClient udp, IPEndPoint remote, string msg)
        {
            var bytes = Encoding.UTF8.GetBytes(msg);
            udp.Send(bytes, bytes.Length, remote);
        }

        private static void Broadcast(List<ClientConn> clients, string line)
        {
            foreach (var c in clients)
                SendLine(c.Sock, line);
        }

        private static void SendLine(Socket sock, string line)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                sock.Send(bytes);
            }
            catch { }
        }

        private static bool TryReceiveLines(ClientConn c, out List<string> lines)
        {
            lines = new List<string>();
            var buf = new byte[4096];

            int received;
            try
            {
                received = c.Sock.Receive(buf);
                if (received <= 0) return false;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                    return true;
                return false;
            }
            catch
            {
                return false;
            }

            c.Buffer.Append(Encoding.UTF8.GetString(buf, 0, received));

            while (true)
            {
                var s = c.Buffer.ToString();
                var idx = s.IndexOf('\n');
                if (idx < 0) break;

                var line = s.Substring(0, idx).TrimEnd('\r');
                lines.Add(line);

                c.Buffer.Clear();
                c.Buffer.Append(s.Substring(idx + 1));
            }

            return true;
        }
    }
}
