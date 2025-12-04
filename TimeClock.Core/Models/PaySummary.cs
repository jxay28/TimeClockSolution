namespace TimeClock.Core.Models
{
    /// <summary>
    /// Riepilogo del periodo di paga con ore e compensi.
    /// </summary>
    public class PaySummary
    {
        public string UserId { get; set; } = string.Empty;
        public double OreOrdinarie { get; set; }
        public double OreStraordinarie { get; set; }
        public decimal CompensoOrdinarie { get; set; }
        public decimal CompensoStraordinarie { get; set; }
    }
}