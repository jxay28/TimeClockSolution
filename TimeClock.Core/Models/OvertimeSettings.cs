namespace TimeClock.Core.Models
{
    /// <summary>
    /// Parametri per il calcolo degli straordinari.
    /// </summary>
    public class OvertimeSettings
    {
        /// <summary>
        /// Soglia minima in minuti per l'arrotondamento degli straordinari.
        /// </summary>
        public int SogliaMinuti { get; set; }

        /// <summary>
        /// Unità di arrotondamento in minuti (es. 15 o 30).
        /// </summary>
        public int UnitaArrotondamentoMinuti { get; set; }
    }
}