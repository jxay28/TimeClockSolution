using System;
using System.Collections.Generic;

namespace TimeClock.Core.Models
{
    public class ParametriStraordinari
    {
        public int SogliaMinutiStraordinario { get; set; } = 15;
        public List<DayOfWeek> GiorniSempreFestivi { get; set; } = new List<DayOfWeek>();

        // Qui usiamo la classe GiornoMese, non le tuple
        public List<GiornoMese> FestivitaRicorrenti { get; set; } = new List<GiornoMese>();

        // Questa è la proprietà che il compilatore ti diceva che mancava
        public List<DateTime> FestivitaAggiuntive { get; set; } = new List<DateTime>();
    }

    public class GiornoMese
    {
        public int Mese { get; set; }
        public int Giorno { get; set; }
    }
}