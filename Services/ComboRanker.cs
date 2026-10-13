using Diviny.Models;

namespace Diviny.Services
{
    public static class ComboRanker
    {
        public static void Escolher(ComboResultado combos, Deck deck, AnaliseIA? analise, int max = 7)
        {
            if (!combos.Disponivel) return;

            var commander = ScryfallService.Frente(deck.Commander.Nome);

            // Cartas que a análise citou como base da estratégia do deck
            var cartasChave = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (analise != null)
                foreach (var ponto in analise.PontosFortes)
                    foreach (var carta in ponto.Cartas)
                        cartasChave.Add(ScryfallService.Frente(carta));

            double Pontos(Combo c)
            {
                double p = 0;

                if (c.Cartas.Any(n => ScryfallService.Frente(n)
                        .Equals(commander, StringComparison.OrdinalIgnoreCase)))
                    p += 50;

                p += 15 * c.Cartas.Count(n => cartasChave.Contains(ScryfallService.Frente(n)));

                if (c.Resultados.Any(r =>
                        r.Contains("infinite", StringComparison.OrdinalIgnoreCase) ||
                        r.Contains("win the game", StringComparison.OrdinalIgnoreCase)))
                    p += 20;

                p += (6 - Math.Min(c.Cartas.Count, 6)) * 3;

                if (c.Popularidade > 0)
                    p += Math.Log10(c.Popularidade + 1) * 5;

                return p;
            }

            var completos = combos.Incluidos
                .OrderByDescending(Pontos)
                .Take(max)
                .ToList();

            var vagas = max - completos.Count;

            combos.IncluidosExibidos = completos;
            combos.QuaseExibidos = vagas > 0
                ? combos.QuaseIncluidos.OrderByDescending(Pontos).Take(vagas).ToList()
                : new List<Combo>();
        }
    }
}