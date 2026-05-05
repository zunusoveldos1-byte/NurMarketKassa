namespace NurMarketKassa.Models.Pos
{
    public sealed class CartLineRow
    {
        public string ItemId { get; set; } = "";
        public double Qty { get; set; }
        public bool WeighedLine { get; set; }
        public string Title { get; set; } = "";
        public string SubLine { get; set; } = "";
        public string LineTotal { get; set; } = "";
        public string PricePerKgHint { get; set; } = "";
    }
}