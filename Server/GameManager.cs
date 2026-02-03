using CovjeceNeLjutiSe.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{

    //Ideja ove klase je da napravi novu igru
    //Da registruje sve igrace
    //Dodijeli startne pozicije
    internal class GameManager
    {
        public const int HOME_POSITION = -1;

        public GameState State { get; private set; } = new GameState();

        private int _maxPlayers;
        private int _nextPlayerId = 1;

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
                Index = State.Players.Count + 1, // redni broj 1..N
                StartPosition = 0,               // dodeljuje se kasnije
                GoalPosition = 0,
                Figures = new List<Figure>()
            };

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
        // 3. AssignStartPositions – raspodjela start polja
        // ======================================================
        public void AssignStartPositions()
        {
            EnsureGameCreated();

            int step = State.BoardSize / 4;

            for (int i = 0; i < State.Players.Count; i++)
            {
                var player = State.Players[i];
                player.StartPosition = i * step;
                player.GoalPosition = player.StartPosition;
            }

            State.CurrentPlayerIndex = 0;
            State.IsFinished = false;
        }

        // ======================================================
        // Pomocne funkcije
        // ======================================================
        private void EnsureGameCreated()
        {
            if (State.BoardSize <= 0)
                throw new InvalidOperationException("Igra nije inicijalizovana. Pozovi CreateGame().");
        }

        //Ove je sam uslov za velicinu table, broj igraca i broj figura po igracu
        private void ValidateConfig(int boardSize, int maxPlayers, int figuresPerPlayer)
        {
            if (boardSize <= 16 || boardSize % 4 != 0)
                throw new ArgumentException("Velicina table mora biti >16 i deljiva sa 4.");

            if (maxPlayers < 2 || maxPlayers > 4)
                throw new ArgumentException("Broj igraca mora biti 2–4.");

            if (figuresPerPlayer <= 0)
                throw new ArgumentException("Broj figura mora biti veci od 0.");
        }
    }
}
