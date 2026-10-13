using System.Net;
using System.Text;
using System.Text.Json;

namespace Diviny.Services
{
    public class AiService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _cfg;
        private readonly ILogger<AiService> _log;

        public AiService(HttpClient http, IConfiguration cfg, ILogger<AiService> log)
        {
            _http = http;
            _cfg = cfg;
            _log = log;
        }

        public bool Configurado => !string.IsNullOrWhiteSpace(_cfg["Groq:ApiKey"]);
        public string ModeloClassificacao => _cfg["Groq:ModelClassificacao"] ?? "openai/gpt-oss-20b";
        public string ModeloAnalise => _cfg["Groq:ModelAnalise"] ?? "openai/gpt-oss-120b";

        public async Task<string?> PerguntarAsync(string modelo, string sistema, string usuario, int maxTokens = 2000)
        {
            if (!Configurado) return null;

            var dados = new Dictionary<string, object>
            {
                ["model"] = modelo,
                ["max_tokens"] = maxTokens,
                ["temperature"] = 0,
                ["messages"] = new object[]
                {
                    new { role = "system", content = sistema },
                    new { role = "user", content = usuario }
                }
            };

            // Modelos gpt-oss raciocinam antes de responder: esforço baixo economiza tokens
            if (modelo.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase))
                dados["reasoning_effort"] = "low";

            var corpo = JsonSerializer.Serialize(dados);

            try
            {
                // Até 4 tentativas: o plano gratuito devolve 429 ao passar do limite por minuto
                for (int tentativa = 1; tentativa <= 4; tentativa++)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
                    {
                        Content = new StringContent(corpo, Encoding.UTF8, "application/json")
                    };
                    req.Headers.Add("Authorization", "Bearer " + _cfg["Groq:ApiKey"]);

                    var resp = await _http.SendAsync(req);
                    var texto = await resp.Content.ReadAsStringAsync();

                    if (resp.StatusCode == HttpStatusCode.TooManyRequests && tentativa < 4)
                    {
                        var espera = 10.0;
                        if (resp.Headers.RetryAfter?.Delta is TimeSpan d) espera = d.TotalSeconds;
                        espera = Math.Clamp(espera, 1, 60);

                        _log.LogWarning("Limite do Groq atingido, aguardando {S}s", espera);
                        await Task.Delay(TimeSpan.FromSeconds(espera));
                        continue;
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        _log.LogWarning("Groq respondeu {Status}: {Corpo}", resp.StatusCode, texto);
                        return null;
                    }

                    using var doc = JsonDocument.Parse(texto);
                    return doc.RootElement.GetProperty("choices")[0]
                              .GetProperty("message").GetProperty("content").GetString();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao chamar a API de IA");
            }

            return null;
        }

        // Pega só o trecho JSON, caso o modelo escreva algo antes ou depois
        public static string? ExtrairJson(string texto, char abre, char fecha)
        {
            int i = texto.IndexOf(abre);
            int j = texto.LastIndexOf(fecha);
            return i >= 0 && j > i ? texto[i..(j + 1)] : null;
        }
    }
}