using System.Text.RegularExpressions;

namespace Diviny.Services
{
    public static class DeckParser
    {
        // Aceita: "1 Sol Ring", "1x Sol Ring", "1 Sol Ring (CMM) 123"
        private static readonly Regex Linha = new(
            @"^(\d+)\s*x?\s+(.+?)(?:\s+\([A-Za-z0-9]+\)\s*\S*)?\s*$",
            RegexOptions.Compiled);

        public static List<(int Quantidade, string Nome)> Ler(string texto)
        {
            var resultado = new List<(int, string)>();

            foreach (var bruta in texto.Split('\n'))
            {
                var linha = bruta.Trim();
                if (linha.Length == 0 || linha.StartsWith("//") || linha.StartsWith("#"))
                    continue;

                // Para quando chegar no sideboard
                if (linha.Equals("Sideboard", StringComparison.OrdinalIgnoreCase)) break;

                var m = Linha.Match(linha);
                if (!m.Success) continue; // ignora títulos como "Commander" ou "Deck"

                resultado.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim()));
            }

            return resultado;
        }
    }
}