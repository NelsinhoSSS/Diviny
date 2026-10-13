using Diviny.Models;
using Diviny.Services;
using Microsoft.AspNetCore.Mvc;

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

        public HomeController(DeckService deckService, DeckStatsService statsService,
                              CommanderSpellbookService spellbook,
                              CardClassifierService classificador, DeckAnalysisService analise,
                              ResultCacheService cache)
        {
            _deckService = deckService;
            _statsService = statsService;
            _spellbook = spellbook;
            _classificador = classificador;
            _analise = analise;
            _cache = cache;
        }

        public IActionResult Index() => View();

        [HttpGet]
        public IActionResult Analisar() => View(new AnalisarViewModel());

        [HttpPost]
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

        public IActionResult CriarDeck() => View();
        public IActionResult Regras() => View();
        public IActionResult Colecoes() => View();
        public IActionResult Simulacao() => View();
    }
}