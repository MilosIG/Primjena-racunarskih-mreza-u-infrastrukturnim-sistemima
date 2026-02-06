// See https://aka.ms/new-console-template for more information
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

using CovjeceNeLjutiSe.Models;
using System.Text.Json;

namespace Client
{
    internal class Program
    {
        private const int ServerUdpPort = 5000;
        private static readonly Random Rng = new Random();

        static void Main(string[] args)
        {
            Console.Write("Unesi ime igraca: ");
            var name = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Player";

            while (true)
            {
                // 1) UDP JOIN
                var (tcpPort, playerId) = JoinViaUdp(name);
                Console.WriteLine($"JOIN OK. TCP port={tcpPort}, PlayerId={playerId}");

                // 2) TCP connect
                var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Connect(new IPEndPoint(IPAddress.Loopback, tcpPort));
                sock.Blocking = false;
                sock.NoDelay = true;

                SendLine(sock, $"HELLO|PLAYERID|{playerId}");
                Console.WriteLine("TCP povezan. Saljem HELLO...");

                var buffer = new StringBuilder();

                bool wantRejoin = false;

                // ===== GAME LOOP =====
                while (true)
                {
                    var readList = new List<Socket> { sock };
                    Socket.Select(readList, null, null, 200_000);

                    if (!readList.Contains(sock))
                        continue;

                    if (!TryReceiveLines(sock, buffer, out var lines))
                    {
                        Console.WriteLine("Server zatvorio konekciju.");
                        return;
                    }

                    foreach (var line in lines)
                    {
                        // ================= STATE =================
                        if (line.StartsWith("STATE|", StringComparison.OrdinalIgnoreCase))
                        {
                            var payload = line.Substring("STATE|".Length);


                            if (payload.StartsWith("P1(", StringComparison.OrdinalIgnoreCase))
                                continue;

                            try
                            {
                                var report = JsonSerializer.Deserialize<GameReport>(payload);
                                if (report == null)
                                {
                                    Console.WriteLine("STATE: report je null.");
                                    continue;
                                }

                                Console.WriteLine();
                                Console.WriteLine("========================================");
                                Console.WriteLine("           STANJE IGRE (STATE)          ");
                                Console.WriteLine("========================================");

                                // Ko je na potezu
                                if (report.Players != null &&
                                    report.Players.Count > 0 &&
                                    report.CurrentPlayerIndex >= 0 &&
                                    report.CurrentPlayerIndex < report.Players.Count)
                                {
                                    var tp = report.Players[report.CurrentPlayerIndex];
                                    Console.WriteLine($"NA POTEZU: P{tp.Index} {tp.Name}");
                                }
                                else
                                {
                                    Console.WriteLine("NA POTEZU: (nepoznato)");
                                }

                                Console.WriteLine("----------------------------------------");

                                // Ispis igraca i figura
                                if (report.Players == null || report.Players.Count == 0)
                                {
                                    Console.WriteLine("Nema igraca u izvestaju.");
                                }
                                else
                                {
                                    for (int pi = 0; pi < report.Players.Count; pi++)
                                    {
                                        var p = report.Players[pi];
                                        bool isTurn = (pi == report.CurrentPlayerIndex);

                                        Console.WriteLine(
                                            $"{(isTurn ? "➡ " : "  ")}P{p.Index} {p.Name} (ID={p.Id}) | Start={p.StartPosition} | Safe={p.SafeHouse}");

                                        if (p.Figures == null || p.Figures.Count == 0)
                                        {
                                            Console.WriteLine("     (nema figura)");
                                            continue;
                                        }

                                        for (int i = 0; i < p.Figures.Count; i++)
                                        {
                                            var f = p.Figures[i];

                                            string status =
                                                f.IsFinished ? "FINISH" :
                                                f.IsActive ? "ACTIVE" :
                                                "HOME";

                                            Console.WriteLine(
                                                $"     F{i}: {status,-6} | Pos={f.Position,3} | Steps={f.StepsFromStart,3} | Dist={f.DistanceToGoal,3}");
                                        }

                                        Console.WriteLine("----------------------------------------");
                                    }
                                }

                                Console.WriteLine("========================================");
                                Console.WriteLine();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("STATE: Greska pri deserijalizaciji JSON-a: " + ex.Message);
                            }

                            continue;
                        }

                        // ================= OSTALE PORUKE =================
                        Console.WriteLine("SERVER: " + line);

                        if (line.StartsWith("WAITACK", StringComparison.OrdinalIgnoreCase))
                        {
                            SendLine(sock, "ACK");
                            Console.WriteLine("JA: ACK");
                            continue;
                        }

                        if (line.StartsWith("YOURTURN", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Na potezu si. ENTER za bacanje kocke...");
                            Console.ReadLine();

                            int dice = Rng.Next(1, 7);
                            var action = (dice == 6) ? "Activate" : "Move";
                            int figureIndex = 0;

                            var move = $"MOVE|{playerId}|{figureIndex}|{dice}|{action}";
                            SendLine(sock, move);
                            Console.WriteLine("JA: " + move);
                            continue;
                        }

                        if (line.StartsWith("RANK|", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("=== RANG LISTA ===");
                            PrintRank(line);
                            continue;
                        }

                        if (line.Equals("REJOIN", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Igra je zavrsena. ENTER = prijavi se za novu, 'q' = izlaz.");
                            var ans = Console.ReadLine();

                            if (string.Equals(ans, "q", StringComparison.OrdinalIgnoreCase))
                            {
                                SendLine(sock, "QUIT");
                                try { sock.Close(); } catch { }
                                return;
                            }

                            wantRejoin = true;
                            try { sock.Close(); } catch { }
                            break;
                        }
                    }

                    if (wantRejoin)
                        break;
                }

                // ide nova igra
            }
        }

        private static (int tcpPort, int playerId) JoinViaUdp(string name)
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 5000;

            var joinMsg = $"JOIN|{name}";
            var bytes = Encoding.UTF8.GetBytes(joinMsg);

            udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, ServerUdpPort));

            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            var replyBytes = udp.Receive(ref remote);
            var reply = Encoding.UTF8.GetString(replyBytes);

            Console.WriteLine("UDP REPLY: " + reply);

            var parts = reply.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 &&
                parts[0] == "TCPPORT" &&
                parts[2] == "PLAYERID" &&
                int.TryParse(parts[1], out int port) &&
                int.TryParse(parts[3], out int pid))
            {
                return (port, pid);
            }

            throw new Exception("JOIN nije uspeo: " + reply);
        }

        private static void SendLine(Socket sock, string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            sock.Send(bytes);
        }

        private static bool TryReceiveLines(Socket sock, StringBuilder buffer, out List<string> lines)
        {
            lines = new List<string>();

            var buf = new byte[4096];
            int received;

            try
            {
                received = sock.Receive(buf);
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

            buffer.Append(Encoding.UTF8.GetString(buf, 0, received));

            while (true)
            {
                var s = buffer.ToString();
                var idx = s.IndexOf('\n');
                if (idx < 0) break;

                var line = s.Substring(0, idx).TrimEnd('\r');
                lines.Add(line);

                buffer.Clear();
                buffer.Append(s.Substring(idx + 1));
            }

            return true;
        }

        private static void PrintRank(string line)
        {
            // RANK|COUNT|N|POS|1|NAME|X|SAFEHOUSE|k|POS|2|...
            var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i += 1)
            {
                //moze se uraditi lijepi ispis!!!
            }
            Console.WriteLine(line);
        }
    }
}
