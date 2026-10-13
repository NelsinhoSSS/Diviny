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
        public int Poder { get; set; }
        public bool EhTerreno => TypeLine.Split(" // ")[0].Contains("Land");
        public bool EhTerrenoBasico => TypeLine.Contains("Basic Land");
    }
}