namespace Diviny.Models
{
    public class Pontuacao
    {
        public string Nome { get; set; } = "";
        public int Valor { get; set; }              // valor final (0 a 100)
        public int ValorCodigo { get; set; }        // só o cálculo
        public string Rotulo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public string[] Rotulos { get; set; } = Array.Empty<string>();

        public void Definir(int valor)
        {
            Valor = Math.Clamp(valor, 0, 100);
            Rotulo = Rotulos[Math.Min(Valor / 20, 4)];   // 5 níveis de 20 em 20
        }
    }

    public class DeckStats
    {
        public int TotalCartas { get; set; }
        public int Terrenos { get; set; }
        public int Rampa { get; set; }
        public int Compra { get; set; }
        public int Remocao { get; set; }
        public int Wipes { get; set; }
        public double CmcMedio { get; set; }
        public decimal PrecoTotal { get; set; }
        public Dictionary<int, int> CurvaMana { get; set; } = new();
        public Dictionary<string, int> Tipos { get; set; } = new();
        public Dictionary<string, int> Simbolos { get; set; } = new();

        // Combos
        public int CombosCompletos { get; set; }
        public int CombosFinais { get; set; }       // infinitos ou que ganham o jogo
        public int CombosDoisCartas { get; set; }   // finais com 2 cartas

        // Sinais usados no bracket
        public List<string> GameChangers { get; set; } = new();
        public int Tutores { get; set; }
        public int TurnosExtras { get; set; }
        public int DestruicaoTerras { get; set; }

        // Quais cartas fazem cada função
        public Dictionary<string, List<string>> CartasPorFuncao { get; set; } = new();

        // Perfil
        public int Bracket { get; set; }
        public string BracketNome { get; set; } = "";
        public List<Pontuacao> Perfil { get; set; } = new();
    }
}