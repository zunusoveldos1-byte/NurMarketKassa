using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NurMarketKassa.Models
{
    /// <summary>Стандартная обёртка API для списков (results / count).</summary>
    public class ApiListResponse<T>
    {
        [JsonPropertyName("results")]
        public List<T>? Results { get; set; }

        [JsonPropertyName("count")]
        public int? Count { get; set; }
    }

    /// <summary>Упрощённое представление товара из поиска / каталога.</summary>
    public class ProductDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("barcode")]
        public string? Barcode { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("image")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("quantity")]
        public double Quantity { get; set; }

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        [JsonPropertyName("must_weigh")]
        public bool MustWeigh { get; set; }
    }
}