using Diviny.Models;

namespace Diviny.Services
{
    public class EntradaDeck
    {
        public int Slot { get; set; }
        public string Commander { get; set; } = "";
        public string Lista { get; set; } = "";
    }

    public class PedidoSimulacao
    {
        public List<EntradaDeck> Decks { get; set; } = new();
    }

    public class DeckResultado
    {
        public int Slot { get; set; }
        public string Nome { get; set; } = "";
        public double Vitoria { get; set; }

        public int Forca { get; set; }
        public int Ofensiva { get; set; }
        public int Defesa { get; set; }
        public int Consistencia { get; set; }

        public int Bracket { get; set; }
        public string BracketNome { get; set; } = "";
        public double CmcMedio { get; set; }
        public int Terrenos { get; set; }
        public int Rampa { get; set; }
        public int Compra { get; set; }
        public int CombosFinais { get; set; }
    }

    public class ResultadoSimulacao
    {
        public string? Erro { get; set; }
        public List<string> Avisos { get; set; } = new();
        public List<DeckResultado> Decks { get; set; } = new();
    }

    // Compara decks pelas estatísticas (as mesmas do Analisar) e converte em chance de vitória
    public class SimulationService
    {
        // Peso de cada componente na força do deck (soma 1)
        private const double PesoOfensiva = 0.35;
        private const double PesoDefesa = 0.15;
        private const double PesoConsistencia = 0.20;
        private const double PesoBracket = 0.30;

        // Quanto menor, mais a diferença de força pesa no resultado
        private const double Temperatura = 16;

        private readonly DeckService _decks;
        private readonly DeckStatsService _stats;
        private readonly CommanderSpellbookService _spellbook;

        public SimulationService(DeckService decks, DeckStatsService stats, CommanderSpellbookService spellbook)
        {
            _decks = decks;
            _stats = stats;
            _spellbook = spellbook;
        }

        public async Task<ResultadoSimulacao> SimularAsync(List<EntradaDeck> entradas)
        {
            var res = new ResultadoSimulacao();

            var usadas = entradas
                .Where(e => e.Slot is >= 0 and <= 3)
                .Where(e => !string.IsNullOrWhiteSpace(e.Commander) || !string.IsNullOrWhiteSpace(e.Lista))
                .GroupBy(e => e.Slot).Select(g => g.First())
                .OrderBy(e => e.Slot)
                .ToList();

            foreach (var e in usadas)
            {
                if (string.IsNullOrWhiteSpace(e.Commander) || string.IsNullOrWhiteSpace(e.Lista))
                {
                    res.Erro = $"Deck {e.Slot + 1}: informe o commander e a lista.";
                    return res;
                }

                if (e.Commander.Length > 100 || e.Lista.Length > 12000)
                {
                    res.Erro = $"Deck {e.Slot + 1}: o texto é grande demais.";
                    return res;
                }
            }

            if (usadas.Count < 2)
            {
                res.Erro = "Preencha pelo menos 2 decks para comparar.";
                return res;
            }

            // 1. Lê e valida cada deck (um de cada vez, para respeitar o limite do Scryfall)
            var decks = new List<(EntradaDeck Entrada, Deck Deck)>();

            foreach (var e in usadas)
            {
                var (deck, erro) = await _decks.MontarAsync(e.Commander.Trim(), e.Lista);
                if (deck == null)
                {
                    res.Erro = $"Deck {e.Slot + 1}: {erro}";
                    return res;
                }

                if (deck.NaoEncontradas.Count > 0)
                    res.Avisos.Add($"Deck {e.Slot + 1}: {deck.NaoEncontradas.Count} carta(s) não encontrada(s) foram ignoradas.");
                if (deck.TotalCartas != 99)
                    res.Avisos.Add($"Deck {e.Slot + 1}: tem {deck.TotalCartas} cartas além do commander (o ideal são 99).");

                decks.Add((e, deck));
            }

            // 2. Combos de todos os decks ao mesmo tempo
            var tarefas = decks.Select(d => _spellbook.BuscarAsync(d.Deck)).ToList();
            await Task.WhenAll(tarefas);

            // 3. Estatísticas e força de cada deck
            var forcas = new List<double>();

            for (int i = 0; i < decks.Count; i++)
            {
                var (entrada, deck) = decks[i];
                var combos = tarefas[i].Result;

                if (!combos.Disponivel)
                    res.Avisos.Add($"Deck {entrada.Slot + 1}: não consegui consultar os combos, então ele foi avaliado sem eles.");

                var stats = _stats.Calcular(deck, combos, null);
                var p = Pontuar(stats);

                forcas.Add(p.Forca);
                res.Decks.Add(new DeckResultado
                {
                    Slot = entrada.Slot,
                    Nome = deck.Commander.Nome,
                    Forca = (int)Math.Round(p.Forca),
                    Ofensiva = (int)Math.Round(p.Ofensiva),
                    Defesa = (int)Math.Round(p.Defesa),
                    Consistencia = (int)Math.Round(p.Consistencia),
                    Bracket = stats.Bracket,
                    BracketNome = stats.BracketNome,
                    CmcMedio = stats.CmcMedio,
                    Terrenos = stats.Terrenos,
                    Rampa = stats.Rampa,
                    Compra = stats.Compra,
                    CombosFinais = stats.CombosFinais
                });
            }

            // 4. Força -> chance de vitória (softmax), com a soma fechando em 100%
            var chances = Probabilidades(forcas);
            for (int i = 0; i < res.Decks.Count; i++)
                res.Decks[i].Vitoria = chances[i];

            return res;
        }

        // Componentes de 0 a 100 e a força final
        private static (double Ofensiva, double Defesa, double Consistencia, double Forca) Pontuar(DeckStats s)
        {
            int Medidor(string nome) => s.Perfil.FirstOrDefault(p => p.Nome == nome)?.Valor ?? 0;

            double ofensiva = 0.40 * Medidor("Finalização")
                            + 0.30 * Medidor("Velocidade")
                            + 0.30 * Medidor("Agressividade");

            double defesa = Medidor("Defesa");

            // Quão perto está de 36 terrenos, 10 de rampa e 8 de compra de cartas
            double terrenos = 1 - Math.Min(1, Math.Abs(s.Terrenos - 36) / 8.0);
            double rampa = Math.Min(1, s.Rampa / 10.0);
            double compra = Math.Min(1, s.Compra / 8.0);
            double consistencia = 100 * (0.4 * terrenos + 0.3 * rampa + 0.3 * compra);

            // Bracket 2 = 25 pontos ... bracket 5 = 100
            double bracket = (s.Bracket - 1) / 4.0 * 100;

            double forca = PesoOfensiva * ofensiva
                         + PesoDefesa * defesa
                         + PesoConsistencia * consistencia
                         + PesoBracket * bracket;

            return (ofensiva, defesa, consistencia, forca);
        }

        private static List<double> Probabilidades(List<double> forcas)
        {
            double maior = forcas.Max();   // subtrai o maior para evitar números enormes
            var pesos = forcas.Select(f => Math.Exp((f - maior) / Temperatura)).ToList();
            double soma = pesos.Sum();

            var pct = pesos.Select(p => Math.Round(p / soma * 100, 1)).ToList();

            // Ajusta o arredondamento para a soma ser exatamente 100
            double diferenca = Math.Round(100 - pct.Sum(), 1);
            if (diferenca != 0)
            {
                int i = pct.IndexOf(pct.Max());
                pct[i] = Math.Round(pct[i] + diferenca, 1);
            }

            return pct;
        }
    }
}