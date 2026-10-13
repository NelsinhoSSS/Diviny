namespace Diviny.Models
{
    public class Deck
    {
        public Carta Commander { get; set; } = new();
        public List<Carta> Cartas { get; set; } = new();   // as 99 cartas
        public List<string> Avisos { get; set; } = new();
        public List<string> NaoEncontradas { get; set; } = new();

        public int TotalCartas => Cartas.Sum(c => c.Quantidade);
    }
}