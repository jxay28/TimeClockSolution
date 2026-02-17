using System.Collections.Generic;
using System;

public class ParametriStraordinari
{
    public int SogliaMinutiStraordinario { get; set; } = 15;
    public List<DayOfWeek> GiorniSempreFestivi { get; set; } = new();
    public List<(int Mese, int Giorno)> FestivitaRicorrenti { get; set; } = new()
    {
        (1, 1),   // Capodanno
        (1, 6),   // Epifania
        (4, 25),  // Liberazione
        (5, 1),   // Festa del Lavoro
        (6, 2),   // Festa della Repubblica
        (8, 15),  // Ferragosto
        (11, 1),  // Ognissanti
        (12, 8),  // Immacolata Concezione
        (12, 25), // Natale
        (12, 26)  // Santo Stefano
    };
    public List<DateTime> FestivitaAggiuntive { get; set; } = new();
    public Dictionary<string, string> NomiFestivitaAggiuntive { get; set; } = new();
}
