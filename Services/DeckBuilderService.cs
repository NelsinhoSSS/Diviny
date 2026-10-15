using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Diviny.Models;

namespace Diviny.Services
{
    public class PedidoDeck
    {
        public string CommanderNome { get; set; } = "";
        public int? Bracket { get; set; }
        // Chaves: Creature, Artifact, Enchantment, Instant, Sorcery, Planeswalker, Land
        public Dictionary<string, int> Quantidades { get; set; } = new();
        public int? Basicos { get; set; }
    }

    public class LinhaLista
    {
        public int Quantidade { get; set; }
        public string Nome { get; set; } = "";
    }

    public class GrupoLista
    {
        public string Titulo { get; set; } = "";
        public List<LinhaLista> Linhas { get; set; } = new();
        public int Total => Linhas.Sum(l => l.Quantidade);
    }

    public class ResultadoCriacao
    {
        public string? Erro { get; set; }
        public Carta? Commander { get; set; }
        public List<GrupoLista> Grupos { get; set; } = new();
        public string ListaTexto { get; set; } = "";
        public List<string> Avisos { get; set; } = new();
    }

    public class DeckBuilderService
    {
        private const int TotalDeck = 99;

        private static readonly string[] Ordem =
            { "Creature", "Artifact", "Enchantment", "Instant", "Sorcery", "Planeswalker", "Land" };

        // Proporção usada para o que o usuário não definiu (soma 99)
        private static readonly Dictionary<string, int> Modelo = new()
        {
            ["Land"] = 36,
            ["Creature"] = 25,
            ["Artifact"] = 12,
            ["Enchantment"] = 7,
            ["Instant"] = 9,
            ["Sorcery"] = 8,
            ["Planeswalker"] = 2
        };

        private static readonly Dictionary<string, string> Titulos = new()
        {
            ["Creature"] = "Criaturas",
            ["Artifact"] = "Artefatos",
            ["Enchantment"] = "Encantamentos",
            ["Instant"] = "Instants",
            ["Sorcery"] = "Sorceries",
            ["Planeswalker"] = "Planeswalkers",
            ["Land"] = "Terrenos"
        };

        private static readonly Dictionary<string, string> ChavesJson = new()
        {
            ["Creature"] = "criaturas",
            ["Artifact"] = "artefatos",
            ["Enchantment"] = "encantamentos",
            ["Instant"] = "instants",
            ["Sorcery"] = "sorceries",
            ["Planeswalker"] = "planeswalkers",
            ["Land"] = "terrenos"
        };

        // Consultas do Scryfall para completar cada tipo (o filtro final é feito no código)
        private static readonly Dictionary<string, string> Consultas = new()
        {
            ["Creature"] = "t:creature -t:land",
            ["Artifact"] = "t:artifact -t:creature -t:land",
            ["Enchantment"] = "t:enchantment -t:creature -t:artifact -t:land",
            ["Instant"] = "t:instant",
            ["Sorcery"] = "t:sorcery",
            ["Planeswalker"] = "t:planeswalker -t:creature -t:land",
            ["Land"] = "t:land -t:basic"
        };

        private static readonly Dictionary<string, string> NomeBasico = new()
        {
            ["W"] = "Plains",
            ["U"] = "Island",
            ["B"] = "Swamp",
            ["R"] = "Mountain",
            ["G"] = "Forest"
        };

        private static readonly string[] DescricaoBracket =
        {
            "",
            "Exhibition: deck temático e descontraído, sem Game Changers, sem tutores, sem combos que vençam o jogo, sem turnos extras e sem destruição de terrenos em massa; cartas de baixo poder.",
            "Core: nível de um deck pré-montado melhorado; sem Game Changers, poucos tutores, sem combos de 2 cartas, sem turnos extras em sequência e sem destruição de terrenos em massa.",
            "Upgraded: deck bem otimizado, no máximo 3 Game Changers, sem destruição de terrenos em massa, sem turnos extras em sequência e sem combos de 2 cartas que vençam nas primeiras rodadas.",
            "Optimized: alto poder, sem restrições além da lista de banidas; tutores, mana rápida e combos são bem-vindos.",
            "cEDH: competitivo; maximize velocidade e consistência com mana rápida, tutores, interação barata e combos de vitória."
        };

        private const string Sistema = """
            Você é um especialista em Magic: The Gathering, formato Commander, e monta decks.
            Escolha cartas que tenham sinergia com a estratégia do commander informado.
            Regras obrigatórias:
            - Use SOMENTE cartas reais, com o nome oficial em inglês, exatamente como no Scryfall.
            - Todas as cartas devem ser legais no Commander e respeitar a identidade de cor do commander.
            - Nenhuma carta repetida, em nenhuma seção. NÃO inclua o commander nem terrenos básicos.
            - Cada carta vai na seção do seu tipo principal: "criaturas" (inclui artefatos-criatura), "artefatos" (artefatos que não são criaturas),
              "encantamentos" (que não são criaturas nem artefatos), "instants", "sorceries", "planeswalkers" e "terrenos" (apenas terrenos NÃO básicos).
            - Respeite o bracket pedido.
            - Preencha somente as seções pedidas, da carta mais importante para a menos importante.
            Responda SOMENTE com JSON, sem texto fora dele, neste formato:
            {"criaturas":[],"artefatos":[],"encantamentos":[],"instants":[],"sorceries":[],"planeswalkers":[],"terrenos":[]}
            """;

        private readonly ScryfallService _scryfall;
        private readonly AiService _ai;
        private readonly ILogger<DeckBuilderService> _log;

        public DeckBuilderService(ScryfallService scryfall, AiService ai, ILogger<DeckBuilderService> log)
        {
            _scryfall = scryfall;
            _ai = ai;
            _log = log;
        }

        public async Task<ResultadoCriacao> CriarAsync(PedidoDeck pedido)
        {
            var res = new ResultadoCriacao();

            // 1. Validações que não precisam de rede
            int soma = pedido.Quantidades.Values.Sum();
            if (soma > TotalDeck)
            {
                res.Erro = $"As quantidades escolhidas somam {soma} cartas, mas o deck tem {TotalDeck} além do commander. " +
                           $"O deck não foi criado: reduza {soma - TotalDeck} carta(s).";
                return res;
            }

            if (pedido.Quantidades.TryGetValue("Land", out var landsPedidos) &&
                pedido.Basicos is int basicosPedidos && basicosPedidos > landsPedidos)
            {
                res.Erro = $"Você pediu {basicosPedidos} terrenos básicos, mas o total de Lands é {landsPedidos}.";
                return res;
            }

            // 2. Commander
            var commander = await _scryfall.BuscarPorNomeAsync(pedido.CommanderNome);
            if (commander == null)
            {
                res.Erro = $"Não encontrei o commander \"{pedido.CommanderNome}\". Confira o nome.";
                return res;
            }
            res.Commander = commander;

            if (!(commander.TypeLine.Contains("Legendary") && commander.TypeLine.Contains("Creature"))
                && !commander.OracleText.Contains("can be your commander"))
                res.Avisos.Add($"{commander.Nome} normalmente não pode ser commander.");
            else if (!commander.LegalCommander)
                res.Avisos.Add($"{commander.Nome} não é legal no formato Commander.");

            // 3. Plano: quantas cartas de cada tipo
            var plano = CalcularPlano(pedido.Quantidades);

            int terrenosTotal = plano["Land"];
            int basicosQtd = Math.Min(pedido.Basicos ?? (int)Math.Round(terrenosTotal * 0.45), terrenosTotal);
            var alvo = new Dictionary<string, int>(plano) { ["Land"] = terrenosTotal - basicosQtd };

            if (pedido.Quantidades.Count > 0 && soma < TotalDeck)
                res.Avisos.Add($"Você escolheu {soma} cartas; as outras {TotalDeck - soma} foram escolhidas automaticamente.");

            // 4. Regras de escolha
            var identidade = new HashSet<string>(commander.IdentidadeCor);
            int bracket = pedido.Bracket ?? 3;
            int limiteGc = bracket <= 2 ? 0 : bracket == 3 ? 3 : int.MaxValue;

            var escolhidas = Ordem.ToDictionary(c => c, _ => new List<Carta>());
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScryfallService.Frente(commander.Nome) };
            int gcUsados = 0;

            bool Tentar(Carta c)
            {
                var cat = c.Categoria;
                if (!alvo.ContainsKey(cat) || escolhidas[cat].Count >= alvo[cat]) return false;
                if (!c.LegalCommander || c.EhTerrenoBasico) return false;
                if (!c.IdentidadeCor.All(identidade.Contains)) return false;

                var chave = ScryfallService.Frente(c.Nome);
                if (usados.Contains(chave)) return false;
                if (c.GameChanger && gcUsados >= limiteGc) return false;

                usados.Add(chave);
                if (c.GameChanger) gcUsados++;
                c.Quantidade = 1;
                escolhidas[cat].Add(c);
                return true;
            }

            // 5. Sugestões com sinergia, conferidas no Scryfall
            var sugestoes = await PedirSugestoesAsync(commander, bracket, alvo);
            if (sugestoes != null)
            {
                var (achadas, _) = await _scryfall.BuscarVariasAsync(sugestoes, aproximar: false);

                var porNome = new Dictionary<string, Carta>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in achadas) porNome.TryAdd(ScryfallService.Frente(c.Nome), c);

                foreach (var nome in sugestoes)
                    if (porNome.TryGetValue(ScryfallService.Frente(nome), out var carta))
                        Tentar(carta);
            }
            else
            {
                res.Avisos.Add("Não foi possível personalizar o deck pelas sinergias do commander desta vez; " +
                               "a lista usa as cartas mais populares das cores dele.");
            }

            // 6. Completa o que faltou com as cartas mais populares das cores do commander
            string filtroCor = identidade.Count == 0 ? "id:c" : "id<=" + string.Join("", identidade).ToLowerInvariant();

            foreach (var cat in Ordem)
            {
                if (escolhidas[cat].Count >= alvo[cat]) continue;

                var candidatas = await _scryfall.PesquisarAsync($"{Consultas[cat]} legal:commander {filtroCor}", 3);
                foreach (var c in candidatas)
                {
                    Tentar(c);
                    if (escolhidas[cat].Count >= alvo[cat]) break;
                }
            }

            // 7. Terrenos básicos, divididos pelas cores mais usadas
            var naoTerrenos = escolhidas.Where(p => p.Key != "Land").SelectMany(p => p.Value).ToList();
            var basicas = new List<(string Nome, int Qtd)>();

            if (basicosQtd > 0)
            {
                if (identidade.Count == 0)
                {
                    basicas.Add(("Wastes", basicosQtd));
                }
                else
                {
                    var pesos = identidade.ToDictionary(
                        cor => cor,
                        cor => 1 + naoTerrenos.Sum(c => Regex.Matches(c.ManaCost, @"\{[^}]*" + cor + @"[^}]*\}").Count));

                    foreach (var par in Distribuir(basicosQtd, pesos)
                                 .Where(p => p.Value > 0)
                                 .OrderBy(p => "WUBRG".IndexOf(p.Key)))
                        basicas.Add((NomeBasico[par.Key], par.Value));
                }
            }

            // 8. Monta o resultado
            int total = escolhidas.Values.Sum(l => l.Count) + basicas.Sum(b => b.Qtd);
            if (total < TotalDeck)
                res.Avisos.Add($"Não consegui completar o deck: faltam {TotalDeck - total} carta(s).");

            foreach (var cat in Ordem)
            {
                var linhas = escolhidas[cat]
                    .OrderBy(c => c.Nome)
                    .Select(c => new LinhaLista { Quantidade = 1, Nome = c.Nome })
                    .ToList();

                if (cat == "Land")
                    linhas.AddRange(basicas.Select(b => new LinhaLista { Quantidade = b.Qtd, Nome = b.Nome }));

                if (linhas.Count > 0)
                    res.Grupos.Add(new GrupoLista { Titulo = Titulos[cat], Linhas = linhas });
            }

            // Texto para copiar: commander na primeira linha, depois "quantidade nome"
            var texto = new StringBuilder();
            texto.AppendLine($"1 {commander.Nome}");
            foreach (var g in res.Grupos)
                foreach (var l in g.Linhas)
                    texto.AppendLine($"{l.Quantidade} {l.Nome}");
            res.ListaTexto = texto.ToString().TrimEnd();

            return res;
        }

        // Quantas cartas de cada tipo, completando o que o usuário não definiu
        private static Dictionary<string, int> CalcularPlano(Dictionary<string, int> pedidas)
        {
            var plano = Ordem.ToDictionary(c => c, _ => 0);
            foreach (var par in pedidas) plano[par.Key] = par.Value;

            int resto = TotalDeck - pedidas.Values.Sum();
            if (resto > 0)
            {
                var livres = Ordem.Where(c => !pedidas.ContainsKey(c)).ToList();
                if (livres.Count == 0) livres = Ordem.Where(c => c != "Land").ToList();

                var extra = Distribuir(resto, livres.ToDictionary(c => c, c => Modelo[c]));
                foreach (var par in extra) plano[par.Key] += par.Value;
            }

            return plano;
        }

        // Divide um total em partes inteiras proporcionais aos pesos (a soma fecha exata)
        private static Dictionary<string, int> Distribuir(int total, Dictionary<string, int> pesos)
        {
            double somaPesos = pesos.Values.Sum();
            var bruto = pesos.ToDictionary(p => p.Key, p => total * p.Value / somaPesos);
            var resultado = bruto.ToDictionary(p => p.Key, p => (int)Math.Floor(p.Value));

            int faltam = total - resultado.Values.Sum();
            foreach (var p in bruto.OrderByDescending(p => p.Value - Math.Floor(p.Value)).Take(faltam))
                resultado[p.Key]++;

            return resultado;
        }

        private async Task<List<string>?> PedirSugestoesAsync(Carta commander, int bracket, Dictionary<string, int> alvo)
        {
            if (!_ai.Configurado) return null;

            var identidade = commander.IdentidadeCor.Count == 0 ? "incolor" : string.Join("", commander.IdentidadeCor);

            var sb = new StringBuilder();
            sb.AppendLine($"COMMANDER: {commander.Nome}");
            sb.AppendLine($"TIPO: {commander.TypeLine}");
            sb.AppendLine($"TEXTO: {Curto(commander.OracleText, 500)}");
            sb.AppendLine($"IDENTIDADE DE COR: {identidade}");
            sb.AppendLine($"BRACKET {bracket}: {DescricaoBracket[bracket]}");
            sb.AppendLine("SEÇÕES PEDIDAS (liste cerca de 20% a mais do que o número indicado):");
            foreach (var cat in Ordem.Where(c => alvo[c] > 0))
                sb.AppendLine($"- {ChavesJson[cat]}: {(int)Math.Ceiling(alvo[cat] * 1.2)}");

            var resposta = await _ai.PerguntarAsync(_ai.ModeloAnalise, Sistema, sb.ToString(), 4000);
            if (resposta == null) return null;

            var json = AiService.ExtrairJson(resposta, '{', '}');
            if (json == null) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var nomes = new List<string>();

                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        foreach (var n in prop.Value.EnumerateArray())
                            if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                                nomes.Add(n.GetString()!.Trim());

                return nomes.Count > 0 ? nomes : null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Resposta de sugestões em formato inesperado");
                return null;
            }
        }

        private static string Curto(string texto, int max)
        {
            texto = texto.Replace('\n', ' ');
            return texto.Length > max ? texto[..max] + "..." : texto;
        }
    }
}