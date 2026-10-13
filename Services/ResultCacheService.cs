using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Diviny.Models;

namespace Diviny.Services
{
    // Guarda o resultado completo de cada lista: mesma lista = mesmo resultado
    public class ResultCacheService
    {
        // Mude este valor quando alterar fórmulas ou regras, para descartar resultados antigos
        private const string Versao = "v2";

        private readonly string _pasta;
        private readonly ILogger<ResultCacheService> _log;

        public ResultCacheService(IWebHostEnvironment env, ILogger<ResultCacheService> log)
        {
            _pasta = Path.Combine(env.ContentRootPath, "App_Data", "resultados");
            _log = log;
        }

        public static string Chave(Deck deck)
        {
            var cartas = deck.Cartas
                .Select(c => $"{c.Quantidade}x{ScryfallService.Frente(c.Nome)}".ToLowerInvariant())
                .OrderBy(x => x, StringComparer.Ordinal);

            var texto = Versao + "|" + ScryfallService.Frente(deck.Commander.Nome).ToLowerInvariant()
                        + "|" + string.Join("|", cartas);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..32];
        }

        public async Task<AnaliseCompleta?> LerAsync(string chave)
        {
            try
            {
                var caminho = Path.Combine(_pasta, chave + ".json");
                if (!File.Exists(caminho)) return null;

                return JsonSerializer.Deserialize<AnaliseCompleta>(await File.ReadAllTextAsync(caminho));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Não consegui ler o resultado guardado");
                return null;
            }
        }

        public async Task SalvarAsync(string chave, AnaliseCompleta resultado)
        {
            try
            {
                Directory.CreateDirectory(_pasta);
                await File.WriteAllTextAsync(Path.Combine(_pasta, chave + ".json"),
                    JsonSerializer.Serialize(resultado));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Não consegui guardar o resultado");
            }
        }
    }
}