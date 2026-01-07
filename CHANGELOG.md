# Changelog

Tutte le modifiche rilevanti al progetto TimeClock sono documentate in questo file.

Il formato segue uno stile semplificato ispirato a "Keep a Changelog".

---

## [Unreleased]
### Added
### Changed
### Fixed

---

## [0.3.0] - 2025-02-XX
### Added
- Gestione logica a blocchi temporali (0 / 15 / 30 minuti)
- Calcolo ore ordinarie e straordinarie basato su soglia minuti

### Fixed
- Allineamento calcolo ore nei casi di ritardo e recupero

## [Unreleased]

### Changed
- Rimossa la voce di menu "Festività".
- Aggiunta la voce di menu "Dashboard" come prima voce.
- Impostata la Dashboard come vista iniziale all'avvio dell'applicazione.

## [Unreleased]

### Fixed
- Rimosso l’uso del metodo inesistente `UserExtrasRepository.Get`.
- Corretto il flusso di aggiornamento di `utenti_extras.json` usando solo `Set()` e `Save()`.

### Changed
- Il salvataggio dalla DataGrid utenti è ora immediato e sicuro.
- Ogni modifica alle celle aggiorna automaticamente:
  - `utenti.json` per i dati anagrafici ed economici,
  - `utenti_extras.json` per i dati operativi collegati all’utente.

### Notes
- `Id` e `SequenceNumber` restano non modificabili.
- Nessun pulsante “Salva”: la UI è la fonte di verità.
