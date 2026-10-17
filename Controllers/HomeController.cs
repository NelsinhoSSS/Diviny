using System.Diagnostics;
using Diviny.Models;
using Diviny.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Diviny.Controllers
{
    public class HomeController : Controller
    {
        private readonly DeckService _deckService;
        private readonly DeckStatsService _statsService;
        private readonly CommanderSpellbookService _spellbook;
        private readonly CardClassifierService _classificador;
        private readonly DeckAnalysisService _analise;
        private readonly ResultCacheService _cache;
        private readonly DeckBuilderService _construtor;
        private readonly RulesChatService _regrasChat;
        private readonly CollectionService _colecoes;

        public HomeController(DeckService deckService, DeckStatsService statsService,
                              CommanderSpellbookService spellbook,
                              CardClassifierService classificador, DeckAnalysisService analise,
                              ResultCacheService cache, DeckBuilderService construtor,
                              RulesChatService regrasChat, CollectionService colecoes)
        {
            _deckService = deckService;
            _statsService = statsService;
            _spellbook = spellbook;
            _classificador = classificador;
            _analise = analise;
            _cache = cache;
            _construtor = construtor;
            _regrasChat = regrasChat;
            _colecoes = colecoes;
        }

        public IActionResult Index() => View();

        // ===== Analisar =====

        [HttpGet]
        public IActionResult Analisar() => View(new AnalisarViewModel());

        [HttpPost]
        [EnableRateLimiting("analise")]
        public async Task<IActionResult> Analisar(AnalisarViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.CommanderNome))
            {
                model.Erro = "Informe quem é o commander.";
                return View(model);
            }

            var (deck, erro) = await _deckService.MontarAsync(model.CommanderNome, model.ListaTexto ?? "");

            if (deck == null)
            {
                model.Erro = erro;
                return View(model);
            }

            model.Deck = deck;

            // Mesma lista: devolve o resultado guardado
            var chave = ResultCacheService.Chave(deck);
            var guardado = await _cache.LerAsync(chave);
            if (guardado != null)
            {
                model.Stats = guardado.Stats;
                model.Analise = guardado.Analise;
                model.Combos = guardado.Combos;
                return View(model);
            }

            // Combos e classificação rodam ao mesmo tempo
            var tarefaCombos = _spellbook.BuscarAsync(deck);
            var tarefaTags = _classificador.ClassificarAsync(deck.Cartas);
            await Task.WhenAll(tarefaCombos, tarefaTags);

            var tags = tarefaTags.Result;   // null se a classificação ficou incompleta
            model.Combos = tarefaCombos.Result;
            model.Stats = _statsService.Calcular(deck, model.Combos, tags);

            model.Analise = await _analise.AnalisarAsync(deck, model.Stats, model.Combos, tags);
            if (model.Analise != null)
                _statsService.AplicarIA(model.Stats, model.Analise);

            // Escolhe os combos mais relevantes (máx. 7) para mostrar na tela
            ComboRanker.Escolher(model.Combos, deck, model.Analise);

            // Só guarda quando tudo deu certo, para o resultado ser sempre o mesmo
            if (tags != null && model.Analise != null && model.Combos.Disponivel)
            {
                await _cache.SalvarAsync(chave, new AnaliseCompleta
                {
                    Stats = model.Stats,
                    Analise = model.Analise,
                    Combos = model.Combos
                });
            }

            return View(model);
        }

        // ===== Criar deck =====

        [HttpGet]
        public IActionResult CriarDeck() => View(new CriarDeckViewModel());

        [HttpPost]
        [EnableRateLimiting("analise")]
        public async Task<IActionResult> CriarDeck(CriarDeckViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.CommanderNome))
            {
                model.Erro = "Informe quem é o commander.";
                return View(model);
            }

            var pedido = new PedidoDeck { CommanderNome = model.CommanderNome.Trim() };

            // As opções só valem se "Escolher especificações" estiver marcado
            if (model.EscolherEspecificacoes)
            {
                var erroPedido = MontarPedido(model, pedido);
                if (erroPedido != null)
                {
                    model.Erro = erroPedido;
                    return View(model);
                }
            }

            var resultado = await _construtor.CriarAsync(pedido);
            if (resultado.Erro != null)
            {
                model.Erro = resultado.Erro;
                return View(model);
            }

            model.Resultado = resultado;
            return View(model);
        }

        private static string? MontarPedido(CriarDeckViewModel m, PedidoDeck pedido)
        {
            if (m.UsarBracket)
            {
                if (m.Bracket is >= 1 and <= 5) pedido.Bracket = m.Bracket;
                else return "Escolha um bracket de 1 a 5.";
            }

            var itens = new (bool Usar, int? Valor, string Chave, string Nome)[]
            {
                (m.UsarCriaturas,     m.Criaturas,     "Creature",     "Criaturas"),
                (m.UsarArtefatos,     m.Artefatos,     "Artifact",     "Artefatos"),
                (m.UsarEncantamentos, m.Encantamentos, "Enchantment",  "Encantamentos"),
                (m.UsarInstants,      m.Instants,      "Instant",      "Instants"),
                (m.UsarSorceries,     m.Sorceries,     "Sorcery",      "Sorcery"),
                (m.UsarLands,         m.Lands,         "Land",         "Lands"),
                (m.UsarPlaneswalkers, m.Planeswalkers, "Planeswalker", "Planeswalkers")
            };

            foreach (var (usar, valor, chave, nome) in itens)
            {
                if (!usar) continue;

                if (valor is null or < 0 or > 999)
                    return $"Informe uma quantidade válida de {nome} (ou desmarque a opção).";

                pedido.Quantidades[chave] = valor.Value;
            }

            if (m.UsarLands && m.LandsBasicos is int basicos)
            {
                if (basicos < 0 || basicos > 999)
                    return "Informe uma quantidade válida de terrenos básicos.";

                pedido.Basicos = basicos;
            }

            return null;
        }

        // ===== Regras (chat) =====

        public IActionResult Regras() => View();

        [HttpPost]
        [EnableRateLimiting("chat")]
        public async Task<IActionResult> PerguntarRegras([FromBody] PerguntaRegras p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Pergunta) || p.Pergunta.Length > 600)
                return BadRequest(new { resposta = "Escreva uma pergunta de até 600 caracteres." });

            var resposta = await _regrasChat.ResponderAsync(p.Pergunta.Trim(), p.Historico ?? new());
            return Json(new { resposta });
        }

        // ===== Coleções =====

        [HttpGet]
        [EnableRateLimiting("colecoes")]
        public async Task<IActionResult> Colecoes(string? q)
        {
            var consulta = (q ?? "").Trim();

            if (consulta.Length == 0)
                return View(new ResultadoColecao());

            if (consulta.Length > 80)
                return View(new ResultadoColecao { Consulta = consulta[..80], Erro = "Nome muito longo." });

            return View(await _colecoes.BuscarAsync(consulta));
        }

        // Sugestões de nomes enquanto a pessoa digita
        [HttpGet]
        public async Task<IActionResult> SugerirColecoes(string? q)
        {
            var termo = (q ?? "").Trim();
            if (termo.Length > 80) return Json(Array.Empty<string>());

            return Json(await _colecoes.SugerirAsync(termo));
        }

        // ===== Outras páginas =====

        public IActionResult Simulacao() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}