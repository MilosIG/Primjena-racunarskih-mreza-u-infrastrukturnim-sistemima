// See https://aka.ms/new-console-template for more information

namespace Server
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //Ovo je samo test da vidim da li mi radi GameManager kako treba
            //Ovu funkciju je potrebno totalno izmjeniti kasnije po tekstu zadatka!!!

            GameManager gm = new GameManager();

            gm.CreateGame(boardSize: 40, maxPlayers: 4, figuresPerPlayer: 4);

            gm.RegisterPlayer("Milos");
            gm.RegisterPlayer("Ana");
            gm.RegisterPlayer("Marko");
            gm.RegisterPlayer("Jovana");

            gm.AssignStartPositions();

            Console.WriteLine("=== STANJE IGRE ===");
            foreach (var p in gm.State.Players)
            {
                Console.WriteLine(
                    $"Igrac {p.Index}: {p.Name} | ID={p.Id} | Start={p.StartPosition}"
                );

                for (int i = 0; i < p.Figures.Count; i++)
                {
                    Console.WriteLine(
                        $"  Figura {i}: Active={p.Figures[i].IsActive}, Pos={p.Figures[i].Position}"
                    );
                }
            }

            Console.ReadKey();
        }
    }
}