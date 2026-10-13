using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Diviny.Models;

namespace Diviny.Services
{
    // Guarda o resultado completo de cada lista, só na memória:
    // mesma lista = mesmo resultado, enquanto o site estiver no ar
    public class ResultCacheService
    {
        // Mude este valor quando alterar fórmulas ou regras, para descartar resultados antigos
        private const string Versao = "v2";

        // Limite para a memória não crescer sem controle
        private const int MaximoGuardado = 200;

        private static readonly ConcurrentDictionary<string, AnaliseCompleta> Guardados = new();

        public static string Chave(Deck deck)
        {
            var cartas = deck.Cartas
                .Select(c => $"{c.Quantidade}x{ScryfallService.Frente(c.Nome)}".ToLowerInvariant())
                .OrderBy(x => x, StringComparer.Ordinal);

            var texto = Versao + "|" + ScryfallService.Frente(deck.Commander.Nome).ToLowerInvariant()
                        + "|" + string.Join("|", cartas);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..32];
        }

        public Task<AnaliseCompleta?> LerAsync(string chave) =>
            Task.FromResult(Guardados.TryGetValue(chave, out var r) ? r : null);

        public Task SalvarAsync(string chave, AnaliseCompleta resultado)
        {
            if (Guardados.Count >= MaximoGuardado)
                Guardados.Clear();

            Guardados[chave] = resultado;
            return Task.CompletedTask;
        }
    }
}