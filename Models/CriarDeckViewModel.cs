using Diviny.Services;

namespace Diviny.Models
{
    public class CriarDeckViewModel
    {
        // O que o usuário preenche
        public string CommanderNome { get; set; } = "";
        public bool EscolherEspecificacoes { get; set; }

        public bool UsarBracket { get; set; }
        public int? Bracket { get; set; }

        public bool UsarCriaturas { get; set; }
        public int? Criaturas { get; set; }

        public bool UsarArtefatos { get; set; }
        public int? Artefatos { get; set; }

        public bool UsarEncantamentos { get; set; }
        public int? Encantamentos { get; set; }

        public bool UsarInstants { get; set; }
        public int? Instants { get; set; }

        public bool UsarSorceries { get; set; }
        public int? Sorceries { get; set; }

        public bool UsarLands { get; set; }
        public int? Lands { get; set; }
        public int? LandsBasicos { get; set; }

        public bool UsarPlaneswalkers { get; set; }
        public int? Planeswalkers { get; set; }

        // O que a página mostra
        public string? Erro { get; set; }
        public ResultadoCriacao? Resultado { get; set; }
    }
}