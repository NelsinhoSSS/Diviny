namespace Diviny.Models
{
    public class AnalisarViewModel
    {
        public string CommanderNome { get; set; } = "";
        public string ListaTexto { get; set; } = "";
        public Deck? Deck { get; set; }
        public DeckStats? Stats { get; set; }
        public string? Erro { get; set; }
        public ComboResultado? Combos { get; set; }
        public AnaliseIA? Analise { get; set; }
    }
}