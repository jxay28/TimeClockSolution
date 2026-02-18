# Fase 2.4 — Validazione regressioni regole business

Questa fase valida i 5 scenari principali dopo il refactor T2.1/T2.2/T2.3.

## Obiettivo

Confermare che la policy unica sia coerente su:

1. giornata normale
2. giornata spezzata
3. turno notturno cross-day
4. giornata festiva
5. giornata con deficit e recupero a blocchi

## Script di check

Percorso:

- `tools/worktime_policy_check.py`

Esecuzione:

```bash
python3 tools/worktime_policy_check.py
```

## Policy validata

- entrata arrotondata **in su**
- uscita arrotondata **in giù**
- straordinario conteggiato a blocchi dopo soglia
- deficit con recupero arrotondato al blocco superiore
- pairing cross-day formalizzato

## Nota tecnica

Sul server attuale non è disponibile `dotnet`, quindi la validazione runtime è stata eseguita tramite script di regressione policy. Quando `dotnet` sarà disponibile, aggiungere test unitari .NET equivalenti su `TimeClock.Core`.
