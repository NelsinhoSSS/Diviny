namespace Diviny.Models
{
    public class AnaliseCompleta
    {
        public DeckStats Stats { get; set; } = new();
        public AnaliseIA? Analise { get; set; }
        public ComboResultado Combos { get; set; } = new();
    }
}