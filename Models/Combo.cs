namespace Diviny.Models
{
    public class Combo
    {
        public string Id { get; set; } = "";
        public List<string> Cartas { get; set; } = new();
        public List<string> Resultados { get; set; } = new();
        public string Descricao { get; set; } = "";
        public long Popularidade { get; set; }
        public string Url => $"https://commanderspellbook.com/combo/{Id}";
    }

    public class ComboResultado
    {
        public bool Disponivel { get; set; }
        public List<Combo> Incluidos { get; set; } = new();
        public List<Combo> QuaseIncluidos { get; set; } = new();
        public List<Combo> IncluidosExibidos { get; set; } = new();
        public List<Combo> QuaseExibidos { get; set; } = new();
    }
}