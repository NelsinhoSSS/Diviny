using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Diviny.Services
{
    public class SetInfo
    {
        public string Codigo { get; set; } = "";
        public string Nome { get; set; } = "";
        public string NomeNorm { get; set; } = "";
        public string Lancamento { get; set; } = "";
        public int TotalCartas { get; set; }
        public string? Icone { get; set; }
    }

    public class CartaColecao
    {
        public string Nome { get; set; } = "";
        public string? Imagem { get; set; }
        public string? Url { get; set; }
        public List<string> Cores { get; set; } = new();
        public string Categoria { get; set; } = "Outro";
    }

    public class ResultadoColecao
    {
        public string Consulta { get; set; } = "";
        public string? Erro { get; set; }
        public SetInfo? Colecao { get; set; }
        public List<CartaColecao> Cartas { get; set; } = new();
        public List<SetInfo> Sugestoes { get; set; } = new();
        public bool Truncado { get; set; }
    }

    // Busca coleções (sets) e as cartas de cada uma no Scryfall
    public class CollectionService
    {
        private const int MaxPaginas = 8;   // 175 cartas por página

        private static readonly string[] TiposIgnorados = { "token", "memorabilia", "minigame" };

        private static List<SetInfo>? _sets;
        private static DateTime _setsEm = DateTime.MinValue;
        private static readonly SemaphoreSlim Trava = new(1, 1);

        private static readonly ConcurrentDictionary<string, (DateTime Quando, List<CartaColecao> Cartas, bool Truncado)> Cache = new();
        private static readonly Regex NaoAlfanumerico = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly ILogger<CollectionService> _log;

        public CollectionService(HttpClient http, ILogger<CollectionService> log)
        {
            _http = http;
            _log = log;
        }

        public async Task<ResultadoColecao> BuscarAsync(string consulta)
        {
            var res = new ResultadoColecao { Consulta = consulta };

            var sets = await CarregarSetsAsync();
            if (sets == null)
            {
                res.Erro = "Não consegui consultar a lista de coleções agora. Tente novamente em instantes.";
                return res;
            }

            var q = Normalizar(consulta);
            if (q.Length == 0)
            {
                res.Erro = "Digite o nome ou o código de uma coleção.";
                return res;
            }

            // 1. Código exato (ex.: mh3), 2. nome exato, 3. nomes que contêm todas as palavras
            SetInfo? alvo = null;
            if (q.Length <= 5 && !q.Contains(' '))
                alvo = sets.FirstOrDefault(s => s.Codigo == q);
            alvo ??= sets.FirstOrDefault(s => s.NomeNorm == q);

            if (alvo == null)
            {
                var candidatos = Candidatos(sets, q);

                if (candidatos.Count == 0)
                {
                    res.Erro = $"Não encontrei nenhuma coleção com \"{consulta}\". " +
                               "Os nomes das coleções são em inglês; tente o nome ou o código (ex.: mh3).";
                    return res;
                }

                if (candidatos.Count == 1)
                {
                    alvo = candidatos[0];
                }
                else
                {
                    res.Sugestoes = candidatos.Take(20).ToList();
                    return res;
                }
            }

            res.Colecao = alvo;

            var (cartas, truncado) = await CarregarCartasAsync(alvo.Codigo);
            if (cartas == null)
            {
                res.Erro = "Não consegui carregar as cartas desta coleção agora. Tente novamente em instantes.";
                return res;
            }

            if (cartas.Count == 0)
            {
                res.Erro = "Esta coleção não tem cartas listadas.";
                return res;
            }

            res.Cartas = cartas;
            res.Truncado = truncado;
            return res;
        }

        // Nomes de coleções para a barra de sugestões
        public async Task<List<string>> SugerirAsync(string termo)
        {
            var sets = await CarregarSetsAsync();
            var q = Normalizar(termo);
            if (sets == null || q.Length < 2) return new();

            return Candidatos(sets, q).Take(10).Select(s => s.Nome).ToList();
        }

        private static List<SetInfo> Candidatos(List<SetInfo> sets, string q)
        {
            var palavras = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return sets.Where(s => palavras.All(p => s.NomeNorm.Contains(p, StringComparison.Ordinal)))
                       .OrderByDescending(s => s.NomeNorm.StartsWith(q, StringComparison.Ordinal))
                       .ThenByDescending(s => s.Lancamento)
                       .ToList();
        }

        private async Task<List<SetInfo>?> CarregarSetsAsync()
        {
            if (_sets != null && DateTime.UtcNow - _setsEm < TimeSpan.FromHours(24)) return _sets;

            await Trava.WaitAsync();
            try
            {
                if (_sets != null && DateTime.UtcNow - _setsEm < TimeSpan.FromHours(24)) return _sets;

                var resp = await _http.GetAsync("sets");
                if (!resp.IsSuccessStatusCode)
                {
                    _log.LogWarning("Scryfall /sets respondeu {Status}", resp.StatusCode);
                    return _sets;   // se já tinha uma lista antiga, continua usando
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var lista = new List<SetInfo>();

                foreach (var e in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var tipo = Texto(e, "set_type");
                    if (TiposIgnorados.Contains(tipo)) continue;

                    var nome = Texto(e, "name");
                    lista.Add(new SetInfo
                    {
                        Codigo = Texto(e, "code"),
                        Nome = nome,
                        NomeNorm = Normalizar(nome),
                        Lancamento = Texto(e, "released_at"),
                        TotalCartas = e.TryGetProperty("card_count", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0,
                        Icone = Texto(e, "icon_svg_uri") is { Length: > 0 } ic ? ic : null
                    });
                }

                _sets = lista.OrderByDescending(s => s.Lancamento).ToList();
                _setsEm = DateTime.UtcNow;
                return _sets;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao carregar a lista de coleções");
                return _sets;
            }
            finally { Trava.Release(); }
        }

        private async Task<(List<CartaColecao>? Cartas, bool Truncado)> CarregarCartasAsync(string codigo)
        {
            if (Cache.TryGetValue(codigo, out var guardado) && DateTime.UtcNow - guardado.Quando < TimeSpan.FromHours(6))
                return (guardado.Cartas, guardado.Truncado);

            var lista = new List<CartaColecao>();
            bool truncado = false;

            try
            {
                for (int pagina = 1; pagina <= MaxPaginas; pagina++)
                {
                    var url = $"cards/search?q={Uri.EscapeDataString("set:" + codigo)}&order=set&unique=cards&page={pagina}";
                    var resp = await _http.GetAsync(url);

                    if (resp.StatusCode == HttpStatusCode.NotFound) break;   // nenhum resultado

                    if (!resp.IsSuccessStatusCode)
                    {
                        _log.LogWarning("Scryfall search respondeu {Status} (coleção {Codigo})", resp.StatusCode, codigo);
                        if (pagina == 1) return (null, false);
                        truncado = true;
                        break;
                    }

                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

                    foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
                        lista.Add(Mapear(c));

                    var mais = doc.RootElement.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
                    if (!mais) break;

                    if (pagina == MaxPaginas) { truncado = true; break; }

                    await Task.Delay(100);   // respeita o limite do Scryfall
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao carregar as cartas da coleção {Codigo}", codigo);
                return (null, false);
            }

            if (Cache.Count >= 30) Cache.Clear();
            Cache[codigo] = (DateTime.UtcNow, lista, truncado);

            return (lista, truncado);
        }

        private static CartaColecao Mapear(JsonElement e)
        {
            JsonElement? frente = null;
            if (e.TryGetProperty("card_faces", out var faces) && faces.ValueKind == JsonValueKind.Array && faces.GetArrayLength() > 0)
                frente = faces[0];

            var carta = new CartaColecao
            {
                Nome = Texto(e, "name"),
                Url = Texto(e, "scryfall_uri") is { Length: > 0 } u ? u : null,
                Imagem = ImagemPequena(e) ?? (frente != null ? ImagemPequena(frente.Value) : null),
                Categoria = CategoriaDe(Texto(e, "type_line"))
            };

            // Cartas de dois lados guardam as cores dentro de card_faces
            JsonElement? cores = null;
            if (e.TryGetProperty("colors", out var c1) && c1.ValueKind == JsonValueKind.Array) cores = c1;
            else if (frente != null && frente.Value.TryGetProperty("colors", out var c2) && c2.ValueKind == JsonValueKind.Array) cores = c2;

            if (cores != null)
                carta.Cores = cores.Value.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList();

            return carta;
        }

        private static string? ImagemPequena(JsonElement e) =>
            e.TryGetProperty("image_uris", out var img) && img.TryGetProperty("small", out var s) ? s.GetString() : null;

        // Tipo principal da carta (só a frente, e só a parte antes do "—")
        private static string CategoriaDe(string typeLine)
        {
            var tipos = typeLine.Split(" // ")[0].Split('—')[0];

            if (tipos.Contains("Land")) return "Land";
            if (tipos.Contains("Creature")) return "Creature";
            if (tipos.Contains("Planeswalker")) return "Planeswalker";
            if (tipos.Contains("Battle")) return "Battle";
            if (tipos.Contains("Artifact")) return "Artifact";
            if (tipos.Contains("Enchantment")) return "Enchantment";
            if (tipos.Contains("Instant")) return "Instant";
            if (tipos.Contains("Sorcery")) return "Sorcery";
            return "Outro";
        }

        // Minúsculas, sem acentos nem pontuação ("Urza's Saga" -> "urzas saga")
        private static string Normalizar(string s)
        {
            var d = s.Replace("'", "").Replace("’", "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(c));

            return NaoAlfanumerico.Replace(sb.ToString(), " ").Trim();
        }

        private static string Texto(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}