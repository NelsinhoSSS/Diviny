using System.Text.RegularExpressions;
using Diviny.Models;

namespace Diviny.Services
{
    public class DeckStatsService
    {
        private const RegexOptions Op = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        private static readonly Regex RampaRx = new(@"add \{|search your library for (a|up to \w+) (basic )?land|put a land card", Op);
        private static readonly Regex CompraRx = new(@"draws? (a|two|three|four|x|that many) cards?|draw cards", Op);
        private static readonly Regex RemocaoRx = new(@"(destroy|exile) target|deals? \d+ damage to (any target|target)|return target .* to (its|their) owner's hand", Op);
        private static readonly Regex WipeRx = new(@"(destroy|exile) all|each creature", Op);
        private static readonly Regex TutorRx = new(@"search your library for [^.]*", Op);
        private static readonly Regex TurnoExtraRx = new(@"extra turns?", Op);
        private static readonly Regex TerrasRx = new(@"(destroy|exile) all [^.]*\blands?\b|sacrifices? all [^.]*\blands?\b|each player sacrifices (all|two|three|four|five|x|half)[^.]*\blands?\b", Op);
        private static readonly Regex ContraRx = new(@"counter target", Op);
        private static readonly Regex ProtecaoRx = new(@"hexproof|indestructible|protection from|phase out", Op);
        private static readonly Regex VidaRx = new(@"you gain \d+ life|lifelink", Op);
        private static readonly Regex AtaqueRx = new(@"whenever [^.]*attacks|haste|double strike|trample|additional combat phase", Op);
        private static readonly Regex VitoriaRx = new(@"you win the game|each opponent loses (\d+|x) life|deals? (\d+|x) damage to each opponent", Op);
        private static readonly Regex GatilhoRx = new(@"\bwhenever\b|at the beginning of", Op);

        private static readonly string[] OrdemTipos =
            { "Land", "Creature", "Planeswalker", "Battle", "Instant", "Sorcery", "Artifact", "Enchantment" };

        private static readonly string[] NomesBracket =
            { "", "Exhibition", "Core", "Upgraded", "Optimized", "cEDH" };

        private static bool TutorPorTexto(string texto) =>
            TutorRx.Matches(texto).Any(m => !m.Value.Contains("land", StringComparison.OrdinalIgnoreCase));

        private static readonly Dictionary<string, Func<string, bool>> Verificadores = new()
        {
            ["destruicao_terras"] = t => TerrasRx.IsMatch(t),
            ["turno_extra"] = t => TurnoExtraRx.IsMatch(t),
            ["tutor"] = TutorPorTexto
        };

        public DeckStats Calcular(Deck deck, ComboResultado? combos = null, Dictionary<string, string[]>? tags = null)
        {
            var s = new DeckStats
            {
                TotalCartas = deck.TotalCartas,
                ClassificadoPorIA = tags != null && tags.Count > 0
            };

            bool Tem(Carta c, string tag, Func<string, bool> porTexto)
            {
                if (tags != null && tags.TryGetValue(c.Nome, out var t))
                {
                    if (!t.Contains(tag)) return false;
                    return !Verificadores.TryGetValue(tag, out var verifica) || verifica(c.OracleText);
                }
                return porTexto(c.OracleText);
            }

            void Marca(string funcao, Carta c)
            {
                if (!s.CartasPorFuncao.TryGetValue(funcao, out var lista))
                    s.CartasPorFuncao[funcao] = lista = new List<string>();
                if (!lista.Contains(c.Nome)) lista.Add(c.Nome);
            }

            bool Det(Carta c, string tag, Func<string, bool> porTexto, string funcao)
            {
                var achou = Tem(c, tag, porTexto);
                if (achou) Marca(funcao, c);
                return achou;
            }

            for (int i = 0; i <= 7; i++) s.CurvaMana[i] = 0;
            foreach (var t in OrdemTipos) s.Tipos[t] = 0;
            foreach (var cor in new[] { "W", "U", "B", "R", "G" }) s.Simbolos[cor] = 0;

            double somaCmc = 0;
            int naoTerrenos = 0, criaturas = 0, somaPoder = 0;
            int ataque = 0, contra = 0, protecao = 0, vida = 0, vitoria = 0;
            int baratas = 0, manaRapida = 0, gatilhos = 0;
            long somaTexto = 0;

            foreach (var c in deck.Cartas.Append(deck.Commander).Where(c => c.GameChanger))
                if (!s.GameChangers.Contains(c.Nome)) s.GameChangers.Add(c.Nome);

            foreach (var c in deck.Cartas)
            {
                int q = c.Quantidade;
                var texto = c.OracleText;

                var tipo = OrdemTipos.FirstOrDefault(t => c.TypeLine.Contains(t));
                if (tipo != null) s.Tipos[tipo] += q;

                s.PrecoTotal += (c.PrecoUsd ?? 0) * q;

                if (c.EhTerreno) { s.Terrenos += q; continue; }

                naoTerrenos += q;
                somaCmc += c.Cmc * q;
                somaTexto += texto.Length * q;
                s.CurvaMana[Math.Min((int)c.Cmc, 7)] += q;

                foreach (var cor in s.Simbolos.Keys.ToList())
                    s.Simbolos[cor] += Regex.Matches(c.ManaCost, "\\{" + cor + "\\}").Count * q;

                bool rampa = Det(c, "rampa", RampaRx.IsMatch, "Rampa");
                if (rampa) s.Rampa += q;
                if (Det(c, "compra", CompraRx.IsMatch, "Compra de cartas")) s.Compra += q;
                if (Det(c, "remocao", RemocaoRx.IsMatch, "Remoção")) s.Remocao += q;
                if (Det(c, "wipe", WipeRx.IsMatch, "Wipes")) s.Wipes += q;

                if (c.TypeLine.Contains("Creature")) { criaturas += q; somaPoder += c.Poder * q; }
                if (Det(c, "ataque", AtaqueRx.IsMatch, "Ataque")) ataque += q;
                if (Det(c, "contra", ContraRx.IsMatch, "Counters")) contra += q;
                if (Det(c, "protecao", ProtecaoRx.IsMatch, "Proteção")) protecao += q;
                if (Det(c, "vida", VidaRx.IsMatch, "Ganho de vida")) vida += q;
                if (Det(c, "finalizador", VitoriaRx.IsMatch, "Finalizadores")) vitoria += q;
                if (GatilhoRx.IsMatch(texto)) gatilhos += q;

                if (c.Cmc <= 2) baratas += q;
                if (rampa && c.Cmc <= 2 && texto.Contains("add", StringComparison.OrdinalIgnoreCase)) manaRapida += q;

                if (Det(c, "tutor", TutorPorTexto, "Tutores")) s.Tutores += q;
                if (Det(c, "turno_extra", TurnoExtraRx.IsMatch, "Turnos extras")) s.TurnosExtras += q;
                if (Det(c, "destruicao_terras", TerrasRx.IsMatch, "Destruição de terrenos")) s.DestruicaoTerras += q;
            }

            s.CmcMedio = naoTerrenos > 0 ? Math.Round(somaCmc / naoTerrenos, 2) : 0;
            s.PrecoTotal += deck.Commander.PrecoUsd ?? 0;

            if (s.GameChangers.Count > 0)
                s.CartasPorFuncao["Game Changers"] = s.GameChangers;

            // Combos
            if (combos is { Disponivel: true })
            {
                s.CombosCompletos = combos.Incluidos.Count;
                s.CombosQuase = combos.QuaseIncluidos.Count;

                var finais = combos.Incluidos.Where(c => c.Resultados.Any(r =>
                    r.Contains("infinite", StringComparison.OrdinalIgnoreCase) ||
                    r.Contains("win the game", StringComparison.OrdinalIgnoreCase))).ToList();

                s.CombosFinais = finais.Count;
                s.CombosDoisCartas = finais.Count(c => c.Cartas.Count <= 2);
            }

            // Perfil
            double pctCriaturas = naoTerrenos > 0 ? (double)criaturas / naoTerrenos : 0;
            double poderMedio = criaturas > 0 ? (double)somaPoder / criaturas : 0;
            double pctGatilhos = naoTerrenos > 0 ? (double)gatilhos / naoTerrenos : 0;
            double textoMedio = naoTerrenos > 0 ? (double)somaTexto / naoTerrenos : 0;

            int agressividade = Pontos(
                0.35 * Escala(pctCriaturas, 0.10, 0.45) +
                0.25 * Escala(poderMedio, 1, 4) +
                0.40 * Escala(ataque, 0, 12));

            int defesa = Pontos(
                0.30 * Escala(s.Remocao, 2, 10) +
                0.20 * Escala(s.Wipes, 0, 3) +
                0.20 * Escala(contra, 0, 8) +
                0.15 * Escala(protecao, 0, 6) +
                0.15 * Escala(vida, 0, 5));

            int velocidade = Pontos(
                0.30 * Escala(s.CmcMedio, 4.0, 2.0) +
                0.15 * Escala(s.Rampa, 4, 14) +
                0.10 * Escala(manaRapida, 0, 6) +
                0.20 * Escala(baratas, 8, 22) +
                0.15 * Escala(s.Tutores, 0, 6) +
                0.10 * Escala(s.CombosDoisCartas, 0, 3));

            int finalizacao = Pontos(
                0.35 * Escala(s.CombosFinais, 0, 3) +
                0.20 * Escala(s.CombosDoisCartas, 0, 2) +
                0.20 * Escala(vitoria, 0, 4) +
                0.15 * Escala(s.Tutores, 0, 6) +
                0.10 * (agressividade / 100.0));

            int dificuldade = Pontos(
                0.30 * Escala(s.Tutores, 0, 6) +
                0.25 * Escala(textoMedio, 80, 200) +
                0.20 * Escala(pctGatilhos, 0.20, 0.60) +
                0.15 * Escala(s.CombosFinais, 0, 3) +
                0.10 * Escala(contra, 0, 8));

            s.Perfil = new List<Pontuacao>
            {
                Item("Agressividade", agressividade,
                    new[] { "Muito baixa", "Baixa", "Média", "Alta", "Muito alta" },
                    "Criaturas, poder e gatilhos de ataque"),

                Item("Defesa", defesa,
                    new[] { "Muito baixa", "Baixa", "Média", "Alta", "Muito alta" },
                    "Remoção, wipes, counters, proteção e vida"),

                Item("Velocidade", velocidade,
                    new[] { "Muito lenta", "Lenta", "Média", "Rápida", "Muito rápida" },
                    "CMC baixo, rampa, mana rápida e tutores"),

                Item("Finalização", finalizacao,
                    new[] { "Muito difícil", "Difícil", "Média", "Fácil", "Muito fácil" },
                    "Combos, condições de vitória e tutores"),

                Item("Dificuldade de pilotar", dificuldade,
                    new[] { "Muito fácil", "Fácil", "Moderada", "Difícil", "Muito difícil" },
                    "Tutores, textos longos, gatilhos e combos")
            };

            CalcularBracket(s, velocidade);
            return s;
        }

        // Mistura: 60% cálculo, 40% IA
        public void AplicarIA(DeckStats s, AnaliseIA ia)
        {
            var notas = new Dictionary<string, int>
            {
                ["Agressividade"] = ia.Agressividade,
                ["Defesa"] = ia.Defesa,
                ["Velocidade"] = ia.Velocidade,
                ["Finalização"] = ia.Finalizacao,
                ["Dificuldade de pilotar"] = ia.Dificuldade
            };

            foreach (var p in s.Perfil)
            {
                if (!notas.TryGetValue(p.Nome, out var nota)) continue;
                nota = Math.Clamp(nota, 0, 100);
                p.ValorIA = nota;
                p.Definir((int)Math.Round(0.6 * p.ValorCodigo + 0.4 * nota));
            }
        }

        private static void CalcularBracket(DeckStats s, int velocidade)
        {
            int b = 2;
            var m = s.BracketMotivos;

            string Nomes(string funcao) =>
                s.CartasPorFuncao.TryGetValue(funcao, out var l) ? string.Join(", ", l.Take(8)) : "";

            void Subir(int minimo, string motivo)
            {
                b = Math.Max(b, minimo);
                m.Add(motivo);
            }

            if (s.GameChangers.Count > 0) Subir(3, $"{s.GameChangers.Count} Game Changer(s): {string.Join(", ", s.GameChangers.Take(6))}");
            if (s.GameChangers.Count > 3) Subir(4, "Mais de 3 Game Changers");
            if (s.DestruicaoTerras > 0) Subir(4, $"Destruição em massa de terrenos: {Nomes("Destruição de terrenos")}");
            if (s.TurnosExtras >= 2) Subir(3, $"{s.TurnosExtras} cartas de turno extra: {Nomes("Turnos extras")}");
            if (s.TurnosExtras >= 4) Subir(4, "Muitos turnos extras");
            if (s.Tutores >= 3) Subir(3, $"{s.Tutores} tutores: {Nomes("Tutores")}");
            if (s.Tutores >= 6) Subir(4, "Muitos tutores");
            if (s.CombosDoisCartas >= 1) Subir(4, $"{s.CombosDoisCartas} combo(s) de 2 cartas que vencem ou são infinitos");
            else if (s.CombosFinais >= 1) Subir(3, $"{s.CombosFinais} combo(s) finais");

            if (b >= 4 && s.Tutores >= 6 && s.CombosDoisCartas >= 2 && s.CmcMedio <= 2.6 && velocidade >= 70)
                Subir(5, "Perfil compatível com cEDH (rápido, muitos tutores e combos de 2 cartas)");

            if (m.Count == 0) m.Add("Nenhum sinal de alto poder encontrado");

            s.Bracket = b;
            s.BracketNome = NomesBracket[b];
        }

        private static double Escala(double v, double min, double max) =>
            Math.Clamp((v - min) / (max - min), 0, 1);

        private static int Pontos(double fracao) => (int)Math.Round(Math.Clamp(fracao, 0, 1) * 100);

        private static Pontuacao Item(string nome, int valor, string[] rotulos, string desc)
        {
            var p = new Pontuacao
            {
                Nome = nome,
                ValorCodigo = valor,
                Rotulos = rotulos,
                Descricao = desc
            };
            p.Definir(valor);
            return p;
        }
    }
}