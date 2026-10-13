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

        public async Task<(List<Carta> Encontradas, List<string> NaoEncontradas)> BuscarVariasAsync(IEnumerable<string> nomes)
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