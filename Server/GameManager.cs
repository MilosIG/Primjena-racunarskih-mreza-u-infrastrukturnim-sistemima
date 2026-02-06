using CovjeceNeLjutiSe.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Server
{
    internal class GameManager
    {
        public const int HOME_POSITION = -1;

        public GameState State { get; private set; } = new GameState();

        private int _maxPlayers;
        private int _nextPlayerId = 1;

        // =========================
        // Session runtime (turn/ack)
        // =========================
        public int LastSteps { get; private set; } = 0;
        public bool AwaitingMove { get; private set; } = false;
        public bool AwaitingAck { get; private set; } = false;
        public int? CurrentTurnPlayerId { get; private set; } = null;

        // ======================================================
        // 1. CreateGame – inicijalizacija tj reset igre
        // ======================================================
        public void CreateGame(int boardSize, int maxPlayers, int figuresPerPlayer)
        {
            ValidateConfig(boardSize, maxPlayers, figuresPerPlayer);

            _maxPlayers = maxPlayers;
            _nextPlayerId = 1;

            State = new GameState
            {
                BoardSize = boardSize,
                FiguresPerPlayer = figuresPerPlayer,
                CurrentPlayerIndex = 0,
                IsFinished = false,
                Players = new List<Player>()
            };

            ResetTurnRuntime();
        }

        private void ResetTurnRuntime()
        {
            LastSteps = 0;
            AwaitingMove = false;
            AwaitingAck = false;
            CurrentTurnPlayerId = null;
        }

        // ======================================================
        // 2. RegisterPlayer – dodavanje igraca
        // ======================================================
        public Player RegisterPlayer(string name)
        {
            //Provjeri da li je igra inicijalizovana
            EnsureGameCreated();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Ime igraca ne smije biti prazno.");

            if (State.Players.Count >= _maxPlayers)
                throw new InvalidOperationException("Igra je vec popunjena.");

            var player = new Player
            {
                Id = _nextPlayerId++,
                Name = name.Trim(),
                Index = State.Players.Count + 1, // 1..N
                StartPosition = 0,               // dodeljuje se u AssignStartPositions
                GoalPosition = 0,
                Figures = new List<Figure>()
            };

            // Ako Player nema SafeHouse u modelu, dodaj ga tamo.
            player.SafeHouse = 0;

            // inicijalizacija figura (sve u bazi, neaktivne)
            for (int i = 0; i < State.FiguresPerPlayer; i++)
            {
                player.Figures.Add(new Figure
                {
                    IsActive = false,
                    Position = HOME_POSITION,
                    StepsFromStart = 0,
                    DistanceToGoal = 0,
                    IsFinished = false
                });
            }

            State.Players.Add(player);
            return player;
        }

        // ======================================================
        // 3. AssignStartPositions – raspodjela start polja (offset 0,10,20,30)
        // ======================================================
        public void AssignStartPositions()
        {
            EnsureGameCreated();

            int step = State.BoardSize / 4; // za 40 => 10

            for (int i = 0; i < State.Players.Count; i++)
            {
                var player = State.Players[i];
                player.StartPosition = i * step;
                player.GoalPosition = player.StartPosition;
            }

            State.CurrentPlayerIndex = 0;
            State.IsFinished = false;

            ResetTurnRuntime();

            if (State.Players.Count > 0)
                CurrentTurnPlayerId = State.Players[State.CurrentPlayerIndex].Id;
        }

        // ======================================================
        // TURN CONTROL
        // ======================================================
        public void MarkYourTurnSent()
        {
            AwaitingMove = true;
        }

        public bool CanAcceptMove(Move move, int? fromPlayerId, out string err)
        {
            err = "";

            if (!CurrentTurnPlayerId.HasValue)
            {
                err = "Igra nije spremna.";
                return false;
            }

            if (!AwaitingMove)
            {
                err = "Server ne ocekuje potez.";
                return false;
            }

            if (!fromPlayerId.HasValue || fromPlayerId.Value != CurrentTurnPlayerId.Value)
            {
                err = "Nije tvoj potez.";
                return false;
            }

            if (move.PlayerId != CurrentTurnPlayerId.Value)
            {
                err = "Nije tvoj potez.";
                return false;
            }

            return true;
        }

        public bool OnAck(int? fromPlayerId)
        {
            if (!AwaitingAck) return false;
            if (!CurrentTurnPlayerId.HasValue) return false;
            if (!fromPlayerId.HasValue) return false;
            if (fromPlayerId.Value != CurrentTurnPlayerId.Value) return false;

            AwaitingAck = false;

            // 6 => isti igrac ponovo, inace sledeci
            if (LastSteps != 6)
                AdvanceTurnInternal();

            AwaitingMove = true;
            return true;
        }

        private void AdvanceTurnInternal()
        {
            State.CurrentPlayerIndex = (State.CurrentPlayerIndex + 1) % State.Players.Count;
            CurrentTurnPlayerId = State.Players[State.CurrentPlayerIndex].Id;
        }

        // ======================================================
        // PROTOCOL PARSING
        // ======================================================
        public static bool TryParseJoin(string msg, out string name, out string err)
        {
            name = "";
            err = "";

            var parts = msg.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts[0].Equals("JOIN", StringComparison.OrdinalIgnoreCase))
            {
                err = "Format JOIN poruke: JOIN|Ime";
                return false;
            }

            name = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                err = "Ime ne sme biti prazno.";
                return false;
            }

            return true;
        }

        public static bool TryParseHello(string line, out int playerId)
        {
            playerId = 0;
            var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 3
                && parts[0].Equals("HELLO", StringComparison.OrdinalIgnoreCase)
                && parts[1].Equals("PLAYERID", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[2], out playerId);
        }

        public static bool TryParseMove(string line, out Move move, out string err)
        {
            move = new Move();
            err = "";

            var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 || !parts[0].Equals("MOVE", StringComparison.OrdinalIgnoreCase))
            {
                err = "Format: MOVE|playerId|figureIndex|steps|action";
                return false;
            }

            if (!int.TryParse(parts[1], out int pid)) { err = "playerId nije broj"; return false; }
            if (!int.TryParse(parts[2], out int fig)) { err = "figureIndex nije broj"; return false; }
            if (!int.TryParse(parts[3], out int steps)) { err = "steps nije broj"; return false; }
            if (!Enum.TryParse<MoveAction>(parts[4], true, out var action)) { err = "action"; return false; }

            move.PlayerId = pid;
            move.FigureIndex = fig;
            move.Steps = steps;
            move.Action = action;
            return true;
        }

        // ======================================================
        // GAME RULES: SafeHouse + jedenje (abs mapping)
        // Lokalno: 0..39 za svakog igraca
        // Apsolutno polje: (StartOffset + local) % 40
        // ======================================================
        public bool ApplyMoveWithSafeHouseAndEating(Move move, out string detail, out bool gameOver)
        {
            detail = "";
            gameOver = false;

            var player = State.Players.FirstOrDefault(p => p.Id == move.PlayerId);
            if (player == null) { detail = "Nepostojeci igrac."; return false; }

            if (move.FigureIndex < 0 || move.FigureIndex >= player.Figures.Count)
            {
                detail = "Nevalidan indeks figure.";
                return false;
            }

            var fig = player.Figures[move.FigureIndex];

            // ACTIVATE
            if (move.Action == MoveAction.Activate)
            {
                if (move.Steps != 6)
                {
                    detail = "Nije 6. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                if (fig.IsActive)
                {
                    detail = "Figura vec aktivna. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                fig.IsActive = true;
                fig.Position = 0;            // lokalni start
                fig.StepsFromStart = 0;
                fig.DistanceToGoal = 39;
                fig.IsFinished = false;

                int abs = GetAbsolutePos(player, fig.Position);

                // jedenje na startu
                var eaten = EatOpponentsOnAbsolute(player, abs);
                detail = eaten.Count > 0
                    ? $"Aktivirana figura {move.FigureIndex} (ABS={abs}). Pojedeno: {string.Join(",", eaten)}."
                    : $"Aktivirana figura {move.FigureIndex} (ABS={abs}).";

                AfterValidMove(move.Steps);
                return true;
            }

            // DEACTIVATE
            if (move.Action == MoveAction.Deactivate)
            {

                if (!fig.IsActive)
                {
                    detail = "Figura nije aktivna. Nema sta da se deaktivira. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                if (fig.Position < 0 || fig.Position == HOME_POSITION)
                {
                    detail = "Figura je vec u HOME. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                // deaktivacija = vrati figuru u HOME i ugasi je
                fig.IsActive = false;
                fig.Position = HOME_POSITION;
                fig.StepsFromStart = 0;
                fig.DistanceToGoal = 39;   // isto kao kad je neaktivna na startu
                fig.IsFinished = false;

                detail = $"Figura {move.FigureIndex} deaktivirana (vracena u HOME).";
                AfterValidMove(move.Steps);
                return true;
            }

            // MOVE
            if (move.Action == MoveAction.Move)
            {
                if (!fig.IsActive)
                {
                    detail = "Figura nije aktivna. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                if (fig.Position < 0)
                {
                    detail = "Figura je u HOME. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                int nextLocal = fig.Position + move.Steps;

                if (nextLocal > 39)
                {
                    detail = "Preslo bi 39. Potez preskocen.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                if (nextLocal == 39)
                {
                    player.SafeHouse++;

                    // figura ide HOME i postaje neaktivna
                    fig.IsActive = false;
                    fig.Position = HOME_POSITION;
                    fig.StepsFromStart = 0;
                    fig.DistanceToGoal = 0;
                    fig.IsFinished = false;

                    if (player.SafeHouse >= 4)
                    {
                        State.IsFinished = true;
                        gameOver = true;
                        detail = $"{player.Name} pobedjuje! (SafeHouse={player.SafeHouse})";
                        AfterValidMove(move.Steps);
                        return true;
                    }

                    detail = $"Tacno 39 => SafeHouse={player.SafeHouse}. Figura vracena u HOME.";
                    AfterValidMove(move.Steps);
                    return true;
                }

                // normalno pomeranje 0..38
                fig.Position = nextLocal;
                fig.StepsFromStart += move.Steps;
                fig.DistanceToGoal = 39 - fig.Position;

                int moverAbs = GetAbsolutePos(player, fig.Position);

                var eaten2 = EatOpponentsOnAbsolute(player, moverAbs);

                detail = eaten2.Count > 0
                    ? $"Pomjerena figura {move.FigureIndex} na LOCAL={fig.Position} (ABS={moverAbs}). Pojedeno: {string.Join(",", eaten2)}."
                    : $"Pomjerena figura {move.FigureIndex} na LOCAL={fig.Position} (ABS={moverAbs}).";

                AfterValidMove(move.Steps);
                return true;
            }

            detail = "Nepodrzano.";
            return false;
        }


        private void AfterValidMove(int steps)
        {
            AwaitingMove = false;
            LastSteps = steps;
            AwaitingAck = true;
        }

        private int GetAbsolutePos(Player player, int localPos)
        {
            if (localPos < 0) return -1;
            return (player.StartPosition + localPos) % State.BoardSize;
        }

        private List<string> EatOpponentsOnAbsolute(Player mover, int moverAbs)
        {
            var eaten = new List<string>();
            if (moverAbs < 0) return eaten;

            foreach (var other in State.Players)
            {
                if (other.Id == mover.Id) continue;

                for (int i = 0; i < other.Figures.Count; i++)
                {
                    var f = other.Figures[i];
                    if (!f.IsActive) continue;
                    if (f.Position < 0) continue;
                    if (f.Position == 39) continue; // cilj se resava posebnim pravilom

                    int otherAbs = (other.StartPosition + f.Position) % State.BoardSize;
                    if (otherAbs == moverAbs)
                    {
                        f.IsActive = false;
                        f.Position = HOME_POSITION;
                        f.StepsFromStart = 0;
                        f.DistanceToGoal = 0;
                        f.IsFinished = false;

                        eaten.Add($"{other.Name}#{i}");
                    }
                }
            }

            return eaten;
        }

        // ======================================================
        // OUTGOING MESSAGES HELPERS
        // ======================================================
        public string BuildStateSummary()
        {
            var sb = new StringBuilder();

            for (int i = 0; i < State.Players.Count; i++)
            {
                var p = State.Players[i];

                sb.Append($"P{p.Index}(").Append(p.Name).Append(")=");
                sb.Append(string.Join(",", p.Figures.Select(f => f.Position)));
                sb.Append($" SH={p.SafeHouse}/4");

                if (i < State.Players.Count - 1) sb.Append("; ");
            }

            sb.Append($" | TURN=P{State.Players[State.CurrentPlayerIndex].Index}");
            return sb.ToString();
        }

        public List<Player> BuildRanking()
        {
            return State.Players
                .OrderByDescending(p => p.SafeHouse)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string BuildRankMessage(List<Player> ranking)
        {
            var sb = new StringBuilder();
            sb.Append("RANK|COUNT|").Append(ranking.Count);

            for (int i = 0; i < ranking.Count; i++)
            {
                var p = ranking[i];
                sb.Append("|POS|").Append(i + 1)
                  .Append("|NAME|").Append(p.Name)
                  .Append("|SAFEHOUSE|").Append(p.SafeHouse);
            }

            return sb.ToString();
        }

        // ======================================================
        // Validation helpers
        // ======================================================
        private void EnsureGameCreated()
        {
            if (State.BoardSize <= 0)
                throw new InvalidOperationException("Igra nije inicijalizovana. Pozovi CreateGame().");
        }

        private void ValidateConfig(int boardSize, int maxPlayers, int figuresPerPlayer)
        {
            if (boardSize <= 16 || boardSize % 4 != 0)
                throw new ArgumentException("Velicina table mora biti >16 i deljiva sa 4.");

            if (maxPlayers < 2 || maxPlayers > 4)
                throw new ArgumentException("Broj igraca mora biti 2–4.");

            if (figuresPerPlayer <= 0)
                throw new ArgumentException("Broj figura mora biti veci od 0.");
        }

        //Kreiraj izvjestaj
        public string BuildSerializedGameReport()
        {
            var report = new GameReport
            {
                Players = State.Players,
                CurrentPlayerIndex = State.CurrentPlayerIndex,
                IsFinished = State.IsFinished
            };

            return JsonSerializer.Serialize(report);
        }
    }
}
