using System.Text;
using System.Text.Json;
using Diviny.Models;

namespace Diviny.Services
{
    public class CommanderSpellbookService
    {
        private readonly HttpClient _http;
        private readonly ILogger<CommanderSpellbookService> _log;

        public CommanderSpellbookService(HttpClient http, ILogger<CommanderSpellbookService> log)
        {
            _http = http;
            _log = log;
        }

        public async Task<ComboResultado> BuscarAsync(Deck deck)
        {
            var resultado = new ComboResultado();

            try
            {
                var corpo = JsonSerializer.Serialize(new
                {
                    commanders = new[] { new { card = deck.Commander.Nome, quantity = 1 } },
                    main = deck.Cartas.Select(c => new { card = c.Nome, quantity = c.Quantidade })
                });

                var resp = await _http.PostAsync("find-my-combos",
                    new StringContent(corpo, Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                {
                    _log.LogWarning("Commander Spellbook respondeu {Status}", resp.StatusCode);
                    return resultado;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var raiz = doc.RootElement;

                // A resposta pode vir embrulhada em "results"
                if (raiz.TryGetProperty("results", out var r) && r.ValueKind == JsonValueKind.Object)
                    raiz = r;

                resultado.Incluidos = LerLista(raiz, "included");
                resultado.QuaseIncluidos = LerLista(raiz, "almostIncluded");
                resultado.Disponivel = true;
            }
            catch (Exception ex)
            {
                // API externa: nunca derruba a página
                _log.LogWarning(ex, "Falha ao consultar o Commander Spellbook");
            }

            return resultado;
        }

        private static List<Combo> LerLista(JsonElement raiz, string propriedade)
        {
            var lista = new List<Combo>();
            if (!raiz.TryGetProperty(propriedade, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return lista;

            foreach (var v in arr.EnumerateArray())
            {
                var combo = new Combo
                {
                    Id = v.TryGetProperty("id", out var id) ? id.ToString() : ""
                };

                if (v.TryGetProperty("popularity", out var pop) &&
                    pop.ValueKind == JsonValueKind.Number && pop.TryGetInt64(out var popValor))
                    combo.Popularidade = popValor;

                if (v.TryGetProperty("uses", out var usos) && usos.ValueKind == JsonValueKind.Array)
                    foreach (var u in usos.EnumerateArray())
                        if (u.TryGetProperty("card", out var c) && c.TryGetProperty("name", out var n))
                            combo.Cartas.Add(n.GetString() ?? "");

                if (v.TryGetProperty("produces", out var prods) && prods.ValueKind == JsonValueKind.Array)
                    foreach (var p in prods.EnumerateArray())
                        if (p.TryGetProperty("feature", out var f) && f.TryGetProperty("name", out var n))
                            combo.Resultados.Add(n.GetString() ?? "");

                lista.Add(combo);
            }

            return lista;
        }
    }
}