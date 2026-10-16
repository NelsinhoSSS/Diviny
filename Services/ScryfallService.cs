using System.Text;
using System.Text.Json;
using Diviny.Models;

namespace Diviny.Services
{
    public class ScryfallService
    {
        private readonly HttpClient _http;
        public ScryfallService(HttpClient http) => _http = http;

        // "Malakir Rebirth // Malakir Mire" -> "Malakir Rebirth"
        public static string Frente(string nome) => nome.Split(" // ")[0].Trim();

        public async Task<Carta?> BuscarPorNomeAsync(string nome)
        {
            var resp = await _http.GetAsync($"cards/named?fuzzy={Uri.EscapeDataString(nome)}");
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return Mapear(doc.RootElement);
        }

        // Carta + rulings oficiais (esclarecimentos de regras publicados para a carta)
        public async Task<(Carta Carta, List<string> Rulings)?> BuscarComRulingsAsync(string nome)
        {
            var resp = await _http.GetAsync($"cards/named?fuzzy={Uri.EscapeDataString(nome)}");
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var carta = Mapear(doc.RootElement);
            var rulings = new List<string>();

            if (doc.RootElement.TryGetProperty("rulings_uri", out var uri) && uri.ValueKind == JsonValueKind.String)
            {
                await Task.Delay(100); // respeita o limite do Scryfall
                var r = await _http.GetAsync(uri.GetString());

                if (r.IsSuccessStatusCode)
                {
                    using var docR = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                    foreach (var item in docR.RootElement.GetProperty("data").EnumerateArray())
                        if (item.TryGetProperty("comment", out var c) && c.ValueKind == JsonValueKind.String)
                            rulings.Add(c.GetString() ?? "");
                }
            }

            return (carta, rulings);
        }

        // aproximar = true: nomes não encontrados são tentados com busca aproximada (erros de digitação)
        public async Task<(List<Carta> Encontradas, List<string> NaoEncontradas)> BuscarVariasAsync(
            IEnumerable<string> nomes, bool aproximar = true)
        {
            var encontradas = new List<Carta>();
            var naoEncontradas = new List<string>();
            var pendentes = new List<string>();

            // Cartas de dois lados: envia só o nome da frente
            var frentes = nomes.Select(Frente).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // A API aceita no máximo 75 cartas por requisição
            foreach (var lote in frentes.Chunk(75))
            {
                var corpo = JsonSerializer.Serialize(new
                {
                    identifiers = lote.Select(n => new { name = n })
                });

                var resp = await _http.PostAsync("cards/collection",
                    new StringContent(corpo, Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                {
                    naoEncontradas.AddRange(lote);
                    continue;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

                foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
                    encontradas.Add(Mapear(c));

                foreach (var nf in doc.RootElement.GetProperty("not_found").EnumerateArray())
                    if (nf.TryGetProperty("name", out var n))
                        pendentes.Add(n.GetString() ?? "");

                await Task.Delay(100); // respeita o limite do Scryfall
            }

            if (!aproximar)
            {
                naoEncontradas.AddRange(pendentes);
                return (encontradas, naoEncontradas);
            }

            // Segunda chance: busca aproximada, uma a uma
            foreach (var nome in pendentes)
            {
                var carta = await BuscarPorNomeAsync(nome);
                await Task.Delay(100);

                if (carta == null)
                    naoEncontradas.Add(nome);
                else if (!encontradas.Any(c => c.Nome == carta.Nome))
                    encontradas.Add(carta);
            }

            return (encontradas, naoEncontradas);
        }

        // Busca com a sintaxe do Scryfall, das cartas mais populares para as menos
        public async Task<List<Carta>> PesquisarAsync(string consulta, int maxPaginas = 2)
        {
            var lista = new List<Carta>();

            for (int pagina = 1; pagina <= maxPaginas; pagina++)
            {
                var url = $"cards/search?q={Uri.EscapeDataString(consulta)}&order=edhrec&unique=cards&page={pagina}";
                var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) break;   // 404 = nenhum resultado

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

                foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
                    lista.Add(Mapear(c));

                await Task.Delay(100);

                if (!(doc.RootElement.TryGetProperty("has_more", out var mais) && mais.ValueKind == JsonValueKind.True))
                    break;
            }

            return lista;
        }

        private static Carta Mapear(JsonElement e)
        {
            var carta = new Carta
            {
                Nome = Texto(e, "name"),
                Cmc = e.TryGetProperty("cmc", out var cmc) ? cmc.GetDouble() : 0,
                ManaCost = Texto(e, "mana_cost"),
                TypeLine = Texto(e, "type_line"),
                OracleText = Texto(e, "oracle_text")
            };

            carta.GameChanger = e.TryGetProperty("game_changer", out var gc) && gc.ValueKind == JsonValueKind.True;

            carta.LegalCommander = e.TryGetProperty("legalities", out var leg) &&
                                   leg.TryGetProperty("commander", out var lc) &&
                                   lc.GetString() == "legal";

            if (e.TryGetProperty("power", out var pw) && pw.ValueKind == JsonValueKind.String
                && int.TryParse(pw.GetString(), out var poder))
                carta.Poder = poder;

            if (e.TryGetProperty("color_identity", out var ci))
                carta.IdentidadeCor = ci.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

            if (e.TryGetProperty("prices", out var precos) &&
                precos.TryGetProperty("usd", out var usd) &&
                usd.ValueKind == JsonValueKind.String &&
                decimal.TryParse(usd.GetString(), System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var p))
                carta.PrecoUsd = p;

            if (e.TryGetProperty("image_uris", out var img) && img.TryGetProperty("normal", out var url))
                carta.ImagemUrl = url.GetString();

            // Cartas de dois lados guardam texto, custo, poder e imagem dentro de card_faces
            if (e.TryGetProperty("card_faces", out var faces) && faces.ValueKind == JsonValueKind.Array)
            {
                var lista = faces.EnumerateArray().ToList();

                if (carta.OracleText == "")
                    carta.OracleText = string.Join("\n", lista.Select(f => Texto(f, "oracle_text")));
                if (carta.ManaCost == "" && lista.Count > 0)
                    carta.ManaCost = Texto(lista[0], "mana_cost");
                if (carta.Poder == 0 && lista.Count > 0 &&
                    lista[0].TryGetProperty("power", out var pw2) && pw2.ValueKind == JsonValueKind.String &&
                    int.TryParse(pw2.GetString(), out var poder2))
                    carta.Poder = poder2;
                if (carta.ImagemUrl == null && lista.Count > 0 &&
                    lista[0].TryGetProperty("image_uris", out var img2) &&
                    img2.TryGetProperty("normal", out var url2))
                    carta.ImagemUrl = url2.GetString();
            }

            return carta;
        }

        private static string Texto(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
    }
}