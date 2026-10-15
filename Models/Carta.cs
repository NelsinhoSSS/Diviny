namespace Diviny.Models
{
    public class Carta
    {
        public string Nome { get; set; } = "";
        public double Cmc { get; set; }
        public string ManaCost { get; set; } = "";
        public string TypeLine { get; set; } = "";
        public string OracleText { get; set; } = "";
        public List<string> IdentidadeCor { get; set; } = new();
        public decimal? PrecoUsd { get; set; }
        public string? ImagemUrl { get; set; }
        public int Quantidade { get; set; } = 1;
        public bool GameChanger { get; set; }
        public bool LegalCommander { get; set; }
        public int Poder { get; set; }
        public bool EhTerreno => TypeLine.Split(" // ")[0].Contains("Land");
        public bool EhTerrenoBasico => TypeLine.Contains("Basic") && TypeLine.Contains("Land");

        // Tipo principal da carta (cada carta conta uma vez só)
        public string Categoria
        {
            get
            {
                var frente = TypeLine.Split(" // ")[0];
                if (frente.Contains("Land")) return "Land";
                if (frente.Contains("Creature")) return "Creature";
                if (frente.Contains("Planeswalker")) return "Planeswalker";
                if (frente.Contains("Artifact")) return "Artifact";
                if (frente.Contains("Enchantment")) return "Enchantment";
                if (frente.Contains("Instant")) return "Instant";
                if (frente.Contains("Sorcery")) return "Sorcery";
                return "Outro";
            }
        }
    }
}