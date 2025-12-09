using System;
using System.Collections.Generic;

namespace TimeClock.Core.Models
{
    public class ParametriStraordinari
    {
        // Soglia minuti per conteggiare straordinario
        public int SogliaMinutiStraordinario { get; set; } = 15;

        // Giorni della settimana considerati festivi
        public List<DayOfWeek> GiorniSempreFestivi { get; set; } = new();

        // Festività ricorrenti (ogni anno alla stessa data, es. 25 dicembre)
        public List<(int Mese, int Giorno)> FestivitaRicorrenti { get; set; } = new();

        // Festività specifiche per anno (Pasqua, ponti…)
        public List<DateTime> FestivitaAggiuntive { get; set; } = new();
    }
}
