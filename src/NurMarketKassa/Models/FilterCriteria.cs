using System;

namespace NurMarketKassa.Models
{
    public class FilterCriteria
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Category { get; set; }
        public string? Brand { get; set; }
        public string? Client { get; set; }
        public string? Status { get; set; }
        public string? HotkeyGroup { get; set; }
        public bool OnlyWeight { get; set; }
        public bool OnlyInStock { get; set; }
        public bool OnlyFavorite { get; set; }

        /// <summary>Поисковый запрос (если нужен).</summary>
        public string? SearchQuery { get; set; }
    }
}