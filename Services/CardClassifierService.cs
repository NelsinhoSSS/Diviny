using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Diviny.Models;

namespace Diviny.Services
{
    public class CardClassifierService
    {
        private static readonly string[] TagsValidas =
        {
            "rampa", "compra", "remocao", "wipe", "tutor", "protecao", "contra",
            "vida", "ataque", "finalizador", "turno_extra", "destruicao_terras"
        };

        private const string Sistema = """
            Você classifica cartas de Magic: The Gathering para análise de decks Commander.
            Baseie-se SOMENTE no tipo e no texto de regras. NUNCA use o nome da carta (ele foi trocado por ~).
            Na dúvida, NÃO coloque a etiqueta. É melhor omitir do que errar.
            Etiquetas permitidas (pode ser nenhuma, uma ou várias):
            - rampa: gera mana extra ou coloca terrenos em jogo (rochas, criaturas de mana, rituais permanentes, busca de terrenos para o campo). Ciclo de terreno (landcycling/plainscycling etc.) NÃO é rampa.
            - compra: compra cartas ou dá vantagem de cartas recorrente (inclui exilar e poder jogar)
            - remocao: remove ou neutraliza uma ameaça específica (destruir, exilar, devolver, dano direcionado)
            - wipe: remove muitas criaturas ou permanentes de uma vez
            - tutor: o texto manda buscar no grimório uma carta que NÃO seja terreno. Ciclo de terreno e busca de terreno NÃO são tutor.
            - protecao: protege suas peças (hexproof, indestructible, proteção, phasing, blink em resposta)
            - contra: anula magias ou habilidades
            - vida: ganha vida de forma relevante (lifelink, ganho direto)
            - ataque: incentiva ou melhora ataques (haste, combate extra, gatilho de ataque, evasão)
            - finalizador: pode vencer o jogo sozinha ou fecha o jogo (vitória alternativa, dreno grande, infinitos)
            - turno_extra: o texto concede um turno extra
            - destruicao_terras: SOMENTE se o texto destrói, sacrifica ou remove vários terrenos de uma vez (Armageddon, Ravages of War, Wildfire). Destruir artefatos ou encantamentos, ciclo de terreno, buscar terrenos ou remover um único terreno NÃO contam.
            Responda SOMENTE com JSON, sem texto fora dele. Use o número da carta no campo "i":
            [{"i":1,"tags":["rampa"]},{"i":2,"tags":[]}]
            """;

        // Só na memória: compartilhado entre as requisições enquanto o site estiver no ar
        private static readonly ConcurrentDictionary<string, string[]> Cache = new(StringComparer.OrdinalIgnoreCase);

        private readonly AiService _ai;
        private readonly ILogger<CardClassifierService> _log;

        public CardClassifierService(AiService ai, ILogger<CardClassifierService> log)
        {
            _ai = ai;
            _log = log;
        }

        public async Task<Dictionary<string, string[]>?> ClassificarAsync(IEnumerable<Carta> cartas)
        {
            if (!_ai.Configurado) return null;

            var unicas = cartas.GroupBy(c => c.Nome).Select(g => g.First()).ToList();

            foreach (var basico in unicas.Where(c => c.EhTerrenoBasico))
                Cache[basico.Nome] = Array.Empty<string>();

            var faltando = unicas.Where(c => !Cache.ContainsKey(c.Nome)).ToList();

            // Lotes pequenos, um de cada vez: respeita o limite do plano gratuito
            foreach (var lote in faltando.Chunk(20))
                await ClassificarLoteAsync(lote);

            var resultado = unicas.Where(c => Cache.ContainsKey(c.Nome))
                                  .ToDictionary(c => c.Nome, c => Cache[c.Nome], StringComparer.OrdinalIgnoreCase);

            // Só vale se TODAS as cartas foram classificadas
            return resultado.Count == unicas.Count ? resultado : null;
        }

        private async Task ClassificarLoteAsync(Carta[] lote)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lote.Length; i++)
            {
                var c = lote[i];
                var texto = c.OracleText.Replace('\n', ' ');

                // Esconde o nome para a classificação ser feita só pelo texto
                texto = texto.Replace(c.Nome, "~");
                var frente = c.Nome.Split(" // ")[0];
                if (frente != c.Nome) texto = texto.Replace(frente, "~");

                if (texto.Length > 200) texto = texto[..200];
                sb.AppendLine($"{i + 1} | {c.TypeLine} | {texto}");
            }

            var resposta = await _ai.PerguntarAsync(_ai.ModeloClassificacao, Sistema, sb.ToString(), 3000);
            if (resposta == null) return;

            var json = AiService.ExtrairJson(resposta, '[', ']');
            if (json == null) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("i", out var idx)) continue;

                    int n;
                    if (idx.ValueKind == JsonValueKind.Number) n = idx.GetInt32();
                    else if (!int.TryParse(idx.GetString(), out n)) continue;

                    if (n < 1 || n > lote.Length) continue;

                    Cache[lote[n - 1].Nome] = item.GetProperty("tags").EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(t => TagsValidas.Contains(t))
                        .Distinct()
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Resposta de classificação em formato inesperado");
            }
        }
    }
}