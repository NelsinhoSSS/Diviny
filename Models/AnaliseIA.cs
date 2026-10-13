using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diviny.Models
{
    [JsonConverter(typeof(PontoIAConverter))]
    public class PontoIA
    {
        public string Texto { get; set; } = "";
        public List<string> Cartas { get; set; } = new();
    }

    // Aceita tanto {"texto":"...","cartas":[...]} quanto uma string simples
    public class PontoIAConverter : JsonConverter<PontoIA>
    {
        public override PontoIA Read(ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opcoes)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new PontoIA { Texto = reader.GetString() ?? "" };

            using var doc = JsonDocument.ParseValue(ref reader);
            var el = doc.RootElement;
            var ponto = new PontoIA();

            if (el.TryGetProperty("texto", out var t) && t.ValueKind == JsonValueKind.String)
                ponto.Texto = t.GetString() ?? "";

            if (el.TryGetProperty("cartas", out var c) && c.ValueKind == JsonValueKind.Array)
                ponto.Cartas = c.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .Where(x => x != "")
                    .ToList();

            return ponto;
        }

        public override void Write(Utf8JsonWriter writer, PontoIA valor, JsonSerializerOptions opcoes)
        {
            writer.WriteStartObject();
            writer.WriteString("texto", valor.Texto);
            writer.WriteStartArray("cartas");
            foreach (var c in valor.Cartas) writer.WriteStringValue(c);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    public class AnaliseIA
    {
        public string Estrategia { get; set; } = "";
        public string Resumo { get; set; } = "";
        public List<PontoIA> PontosFortes { get; set; } = new();
        public List<PontoIA> PontosFracos { get; set; } = new();
        public List<PontoIA> Sugestoes { get; set; } = new();
        public int Bracket { get; set; }
        public string BracketJustificativa { get; set; } = "";

        public int Agressividade { get; set; }
        public int Defesa { get; set; }
        public int Velocidade { get; set; }
        public int Finalizacao { get; set; }
        public int Dificuldade { get; set; }
    }
}