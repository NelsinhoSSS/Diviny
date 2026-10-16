using System.Text;
using System.Text.RegularExpressions;

namespace Diviny.Services
{
    public class RegraEntrada
    {
        public string Numero { get; set; } = "";      // "702.2a" ou, no glossário, o termo
        public string Texto { get; set; } = "";
        public string TextoLower { get; set; } = "";
        public bool Glossario { get; set; }
    }

    // Lê as Comprehensive Rules (arquivo em Data/ComprehensiveRules.txt) e procura trechos por termo
    public class RulesService
    {
        private static List<RegraEntrada>? _regras;
        private static readonly SemaphoreSlim Trava = new(1, 1);

        private static readonly Regex LinhaRegra = new(@"^(\d{3}\.\d+[a-z]?)\.?\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex Cabecalho = new(@"^\d{1,3}\.\s", RegexOptions.Compiled);
        private static readonly Regex Titulo = new(@"^\d{3}\.\d+$", RegexOptions.Compiled);
        private static readonly Regex TituloGlossario = new(@"^Glossary\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex TituloCreditos = new(@"^Credits\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

        // Termos centrais -> seções das regras que merecem prioridade na busca
        private static readonly Dictionary<string, string[]> SecoesPorTermo = new()
        {
            ["stack"] = new[] { "405." },
            ["priority"] = new[] { "117." },
            ["triggered ability"] = new[] { "603." },
            ["activated ability"] = new[] { "602." },
            ["replacement effect"] = new[] { "614." },
            ["layer"] = new[] { "613." },
            ["combat damage"] = new[] { "510." },
            ["state-based actions"] = new[] { "704." },
            ["commander"] = new[] { "903." }
        };

        private readonly string _arquivo;
        private readonly ILogger<RulesService> _log;

        public RulesService(IWebHostEnvironment env, ILogger<RulesService> log)
        {
            _arquivo = Path.Combine(env.ContentRootPath, "Data", "ComprehensiveRules.txt");
            _log = log;
        }

        // Carrega na primeira pergunta e guarda na memória
        public async Task<bool> GarantirCarregadoAsync()
        {
            if (_regras != null) return true;

            await Trava.WaitAsync();
            try
            {
                if (_regras != null) return true;

                if (!File.Exists(_arquivo))
                {
                    _log.LogWarning("Arquivo das regras não encontrado: {Arquivo}", _arquivo);
                    return false;
                }

                var lista = Interpretar(await File.ReadAllTextAsync(_arquivo, Encoding.UTF8));

                if (lista.Count < 500)
                {
                    _log.LogWarning("Arquivo das regras em formato inesperado ({N} entradas)", lista.Count);
                    return false;
                }

                _regras = lista;
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao carregar as Comprehensive Rules");
                return false;
            }
            finally { Trava.Release(); }
        }

        private static List<RegraEntrada> Interpretar(string bruto)
        {
            var texto = bruto.Replace("\r\n", "\n").Replace('\r', '\n');

            // Onde começam o glossário e os créditos (as últimas ocorrências são as do corpo; as primeiras são do sumário)
            int glos = -1, cred = -1;
            var mg = TituloGlossario.Matches(texto);
            if (mg.Count > 0) glos = mg[^1].Index;
            var mc = TituloCreditos.Matches(texto);
            if (mc.Count > 0) cred = mc[^1].Index;

            // Lê as regras antes do glossário; se não render nada, tenta o texto todo
            var lista = LerRegras(glos > 0 ? texto[..glos] : texto);
            if (lista.Count < 500 && glos > 0)
                lista = LerRegras(texto);

            // Glossário: termo na primeira linha, definição nas seguintes
            if (glos > 0)
            {
                var fim = cred > glos ? cred : texto.Length;
                foreach (var item in Regex.Split(texto[glos..fim], @"\n\s*\n"))
                {
                    var linhas = item.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.Trim())
                                     .Where(l => l.Length > 0)
                                     .ToList();
                    if (linhas.Count < 2) continue;

                    lista.Add(new RegraEntrada
                    {
                        Numero = linhas[0],
                        Texto = string.Join(" ", linhas.Skip(1)),
                        Glossario = true
                    });
                }
            }

            foreach (var r in lista) r.TextoLower = r.Texto.ToLowerInvariant();
            return lista;
        }

        private static List<RegraEntrada> LerRegras(string corpo)
        {
            var lista = new List<RegraEntrada>();
            RegraEntrada? atual = null;

            foreach (var bruta in corpo.Split('\n'))
            {
                var linha = bruta.Trim();
                if (linha.Length == 0) continue;

                var m = LinhaRegra.Match(linha);
                if (m.Success)
                {
                    atual = new RegraEntrada { Numero = m.Groups[1].Value, Texto = m.Groups[2].Value };
                    lista.Add(atual);
                }
                else if (Cabecalho.IsMatch(linha))
                {
                    atual = null;   // título de seção, como "702. Keyword Abilities"
                }
                else if (atual != null)
                {
                    atual.Texto += " " + linha;   // exemplo ou continuação da regra
                }
            }

            return lista;
        }

        // Devolve as regras mais relevantes para os termos (no máximo ~4000 caracteres)
        public List<RegraEntrada> Buscar(IEnumerable<string> termos, int maxEntradas = 8, int maxChars = 4000)
        {
            var regras = _regras;
            if (regras == null) return new();

            var ts = termos.Select(Normalizar)
                           .Where(t => t.Length >= 3)
                           .Distinct()
                           .Take(8)
                           .ToList();
            if (ts.Count == 0) return new();

            var pontuadas = new List<(int Indice, double Pontos)>();
            for (int i = 0; i < regras.Count; i++)
            {
                var r = regras[i];
                double p = 0;

                foreach (var t in ts)
                {
                    if (r.Glossario && r.Numero.Equals(t, StringComparison.OrdinalIgnoreCase)) p += 15;
                    else if (!r.Glossario && r.TextoLower.StartsWith(t, StringComparison.Ordinal)) p += 12;

                    p += Math.Min(Contar(r.TextoLower, t), 3);

                    // Reforço para a seção central do assunto (ex.: "stack" -> regras 405.x)
                    if (!r.Glossario && SecoesPorTermo.TryGetValue(t, out var prefixos) &&
                        prefixos.Any(pref => r.Numero.StartsWith(pref, StringComparison.Ordinal)))
                        p += 8;
                }

                if (p > 0) pontuadas.Add((i, p));
            }

            var indices = new List<int>();
            var vistos = new HashSet<int>();

            foreach (var (indice, _) in pontuadas.OrderByDescending(x => x.Pontos).Take(maxEntradas))
            {
                if (vistos.Add(indice)) indices.Add(indice);

                // Se for o título de uma regra (ex.: 702.2), traz também as subregras (702.2a, 702.2b...)
                var rBase = regras[indice];
                if (!rBase.Glossario && Titulo.IsMatch(rBase.Numero))
                {
                    int extras = 0;
                    for (int j = indice + 1; j < regras.Count && extras < 4; j++)
                    {
                        var n = regras[j].Numero;
                        if (n.StartsWith(rBase.Numero, StringComparison.Ordinal) && n.Length > rBase.Numero.Length)
                        {
                            if (vistos.Add(j)) indices.Add(j);
                            extras++;
                        }
                        else break;
                    }
                }
            }

            var resultado = new List<RegraEntrada>();
            int total = 0;
            foreach (var i in indices)
            {
                var r = regras[i];
                var texto = r.Texto.Length > 600 ? r.Texto[..600] + "..." : r.Texto;
                if (total + texto.Length > maxChars) break;

                total += texto.Length;
                resultado.Add(new RegraEntrada { Numero = r.Numero, Texto = texto, Glossario = r.Glossario });
            }

            return resultado;
        }

        private static string Normalizar(string termo)
        {
            var t = termo.Trim().ToLowerInvariant();
            return t.StartsWith("the ") ? t[4..] : t;
        }

        private static int Contar(string texto, string termo)
        {
            int n = 0, pos = 0;
            while ((pos = texto.IndexOf(termo, pos, StringComparison.Ordinal)) >= 0)
            {
                n++;
                pos += termo.Length;
            }
            return n;
        }
    }
}