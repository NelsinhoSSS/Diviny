using Diviny.Models;

namespace Diviny.Services
{
    public class DeckService
    {
        private readonly ScryfallService _scryfall;
        public DeckService(ScryfallService scryfall) => _scryfall = scryfall;

        public async Task<(Deck? Deck, string? Erro)> MontarAsync(string commanderNome, string listaTexto)
        {
            // 1. Commander
            var commander = await _scryfall.BuscarPorNomeAsync(commanderNome);
            if (commander == null)
                return (null, $"Não encontrei o commander \"{commanderNome}\". Confira o nome.");

            var deck = new Deck { Commander = commander };

            if (!(commander.TypeLine.Contains("Legendary") && commander.TypeLine.Contains("Creature"))
                && !commander.OracleText.Contains("can be your commander"))
                deck.Avisos.Add($"{commander.Nome} normalmente não pode ser commander.");

            // 2. Lista (o commander é removido, caso o usuário tenha colado as 100 cartas)
            var frenteCommander = ScryfallService.Frente(commander.Nome);
            var linhas = DeckParser.Ler(listaTexto)
                .Where(l => !ScryfallService.Frente(l.Nome).Equals(frenteCommander, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (linhas.Count == 0)
                return (null, "A lista está vazia ou em formato não reconhecido. Use o formato \"1 Sol Ring\".");

            // Agrupa pelo nome da frente: "A // B" e "A" são a mesma carta
            var quantidades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (qtd, nome) in linhas)
            {
                var frente = ScryfallService.Frente(nome);
                quantidades[frente] = quantidades.GetValueOrDefault(frente) + qtd;
            }

            // 3. Busca no Scryfall
            var (encontradas, naoEncontradas) = await _scryfall.BuscarVariasAsync(quantidades.Keys);
            deck.NaoEncontradas = naoEncontradas;

            foreach (var carta in encontradas)
            {
                if (quantidades.TryGetValue(ScryfallService.Frente(carta.Nome), out var q))
                    carta.Quantidade = q;

                deck.Cartas.Add(carta);
            }

            // 4. Validações
            if (deck.TotalCartas != 99)
                deck.Avisos.Add($"O deck tem {deck.TotalCartas} cartas além do commander (o ideal são 99).");

            foreach (var c in deck.Cartas.Where(c => c.Quantidade > 1 && !c.EhTerrenoBasico
                                                  && !c.OracleText.Contains("any number of cards named")))
                deck.Avisos.Add($"{c.Nome} aparece {c.Quantidade}x (Commander é singleton).");

            foreach (var c in deck.Cartas.Where(c => c.IdentidadeCor.Except(commander.IdentidadeCor).Any()))
                deck.Avisos.Add($"{c.Nome} está fora da identidade de cor do commander.");

            return (deck, null);
        }
    }
}