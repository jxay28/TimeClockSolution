using System.Collections.Generic;
using System;

public class ParametriStraordinari
{
    public bool UsaFestivitaNazionali { get; set; } = true;
    public int SogliaMinutiStraordinario { get; set; } = 15;
    public List<DayOfWeek> GiorniSempreFestivi { get; set; } = new();
    public List<(int Mese, int Giorno)> FestivitaRicorrenti { get; set; } = new();
    public List<DateTime> FestivitaAggiuntive { get; set; } = new();
}
