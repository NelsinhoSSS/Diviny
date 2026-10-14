using System.Text;
using System.Text.Json;
using Diviny.Models;

namespace Diviny.Services
{
    public class DeckAnalysisService
    {
        private const string Sistema = """
            Você é um especialista em Magic: The Gathering, formato Commander.
            Analise o deck usando APENAS os dados fornecidos. Não invente cartas, efeitos ou combos que não estejam nos dados.
            Cada carta vem com etiquetas de função (rampa, compra, remocao, wipe, tutor, protecao, contra, vida, ataque,
            finalizador, turno_extra, destruicao_terras) ou com um trecho do texto.
            Brackets: 1 Exhibition (temático, sem foco em vencer), 2 Core (precon/casual), 3 Upgraded,
            4 Optimized (alto poder), 5 cEDH (competitivo).
            Notas de 0 a 100, onde 50 é um deck mediano de mesa casual bem montada, 80 ou mais é muito acima da média
            e 20 ou menos é muito abaixo:
            - agressividade: quanto o deck pressiona os oponentes com dano
            - defesa: quanto resiste, interage e se protege
            - velocidade: quão cedo desenvolve o plano
            - finalizacao: quão fácil é fechar o jogo
            - dificuldade: quão difícil é pilotar bem o deck

            REGRA IMPORTANTE SOBRE CARTAS: cada ponto forte, ponto fraco e sugestão é um objeto com "texto" e "cartas".
            Sempre que mencionar um efeito, estratégia, combo ou categoria (ex: destruição de terrenos, tutores, rampa,
            wipes, combos), liste no campo "cartas" os nomes EXATOS das cartas do deck que o sustentam, copiados
            dos dados fornecidos. Nunca cite um efeito sem dizer quais cartas. Se não houver cartas específicas, deixe a lista vazia.
            Nas sugestões, "cartas" pode trazer cartas do deck que poderiam ser trocadas ou reforçadas; não invente cartas novas.

            Escreva em português do Brasil, de forma objetiva, com no máximo 4 itens por lista.
            Os campos de nota e bracket devem ser NÚMEROS inteiros, sem aspas.
            Responda SOMENTE com JSON, sem texto fora dele, neste formato:
            {"estrategia":"","resumo":"",
             "pontosFortes":[{"texto":"","cartas":[]}],
             "pontosFracos":[{"texto":"","cartas":[]}],
             "sugestoes":[{"texto":"","cartas":[]}],
             "bracket":0,"bracketJustificativa":"",
             "agressividade":0,"defesa":0,"velocidade":0,"finalizacao":0,"dificuldade":0}
            """;

        private readonly AiService _ai;
        private readonly ILogger<DeckAnalysisService> _log;

        public DeckAnalysisService(AiService ai, ILogger<DeckAnalysisService> log)
        {
            _ai = ai;
            _log = log;
        }

        public async Task<AnaliseIA?> AnalisarAsync(Deck deck, DeckStats s, ComboResultado? combos,
                                                    Dictionary<string, string[]>? tags = null)
        {
            if (!_ai.Configurado) return null;

            var sb = new StringBuilder();
            sb.AppendLine($"COMMANDER: {deck.Commander.Nome} | {deck.Commander.TypeLine} | {Curto(deck.Commander.OracleText, 300)}");
            sb.AppendLine();
            sb.AppendLine("CARTAS (qtd nome | cmc | tipo | funções ou texto):");

            foreach (var c in deck.Cartas.Where(c => !c.EhTerrenoBasico))
            {
                string detalhe;
                if (tags != null && tags.TryGetValue(c.Nome, out var t))
                    detalhe = t.Length > 0 ? string.Join(",", t) : "-";
                else
                    detalhe = Curto(c.OracleText, 60);

                sb.AppendLine($"{c.Quantidade}x {c.Nome} | {c.Cmc} | {c.TypeLine} | {detalhe}");
            }

            sb.AppendLine($"Terrenos básicos: {deck.Cartas.Where(c => c.EhTerrenoBasico).Sum(c => c.Quantidade)}");

            sb.AppendLine();
            sb.AppendLine("CARTAS POR FUNÇÃO (use estes nomes ao citar):");
            foreach (var par in s.CartasPorFuncao)
                sb.AppendLine($"{par.Key}: {string.Join("; ", par.Value.Take(15))}");

            sb.AppendLine();
            sb.AppendLine("ESTATÍSTICAS CALCULADAS:");
            sb.AppendLine($"Terrenos: {s.Terrenos} | CMC médio: {s.CmcMedio} | Rampa: {s.Rampa} | Compra: {s.Compra}");
            sb.AppendLine($"Remoção: {s.Remocao} | Wipes: {s.Wipes} | Tutores: {s.Tutores} | Turnos extras: {s.TurnosExtras}");
            sb.AppendLine($"Game Changers: {(s.GameChangers.Count == 0 ? "nenhum" : string.Join(", ", s.GameChangers))}");
            sb.AppendLine($"Combos completos: {s.CombosCompletos} (finais: {s.CombosFinais}, de 2 cartas: {s.CombosDoisCartas})");
            sb.AppendLine($"Bracket calculado pelo sistema: {s.Bracket} ({s.BracketNome})");

            if (combos is { Disponivel: true })
                foreach (var c in combos.Incluidos.Take(8))
                    sb.AppendLine($"Combo: {string.Join(" + ", c.Cartas)} => {string.Join(", ", c.Resultados)}");

            var resposta = await _ai.PerguntarAsync(_ai.ModeloAnalise, Sistema, sb.ToString(), 2500);
            if (resposta == null) return null;

            var json = AiService.ExtrairJson(resposta, '{', '}');
            if (json == null) return null;

            try
            {
                var analise = JsonSerializer.Deserialize<AnaliseIA>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                });

                if (analise != null) ValidarCartas(analise, deck);
                return analise;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Resposta da análise em formato inesperado");
                return null;
            }
        }

        // Descarta nomes de cartas que a análise citou mas que não existem no deck
        private static void ValidarCartas(AnaliseIA analise, Deck deck)
        {
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in deck.Cartas.Append(deck.Commander))
            {
                nomes.Add(c.Nome);
                nomes.Add(ScryfallService.Frente(c.Nome));
            }

            foreach (var ponto in analise.PontosFortes.Concat(analise.PontosFracos).Concat(analise.Sugestoes))
                ponto.Cartas = ponto.Cartas
                    .Where(n => nomes.Contains(n.Trim()))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        private static string Curto(string texto, int max)
        {
            texto = texto.Replace('\n', ' ');
            return texto.Length > max ? texto[..max] + "..." : texto;
        }
    }
}