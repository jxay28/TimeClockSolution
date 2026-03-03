# TimeClock Solution 🕒

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/download)

**TimeClock Solution** è un sistema enterprise leggero per la gestione delle presenze e il calcolo degli straordinari. Progettato per piccole e medie imprese, il sistema automatizza il tracciamento dei dipendenti utilizzando un'architettura Client-Server basata su file system condiviso (CSV/JSON).

## 🚀 Caratteristiche Principali

- **Client di Timbratura Intuitivo**: Interfaccia WPF moderna con tastierino numerico per selezione rapida e feedback in tempo reale.
- **Server di Gestione**: Dashboard per il monitoraggio in tempo reale dei presenti, gestione anagrafica e configurazione parametri.
- **Motore di Calcolo Avanzato**: 
  - Arrotondamenti intelligenti (Entrata in eccesso, Uscita in difetto).
  - Gestione automatica dei turni notturni (Cross-day pairing).
  - Calcolo straordinari basato su soglie configurabili.
- **Reportistica Fiscale**: Esportazione automatica di report per il consulente del lavoro e il gestionale paghe.
- **Resilienza Dati**: Sistema di scrittura atomica e log di audit per prevenire la corruzione dei file e le manomissioni.

## 🛠 Architettura Tecnica

Il progetto è diviso in tre componenti principali:
1. **TimeClock.Core**: Libreria condivisa contenente la logica business, i modelli e i motori di calcolo.
2. **TimeClock.Client**: Applicazione WPF per i dipendenti (punto di timbratura).
3. **TimeClock.Server**: Centro di controllo per HR e amministrazione.

### Flusso Dati
Il sistema utilizza una **cartella condivisa** come database decentralizzato:
- `utenti.json`: Master dell'anagrafica dipendenti.
- `[GUID].csv`: Registro individuale delle timbrature per ogni utente.
- `parametri_straordinari.json`: Regole globali di calcolo.
- `audit_log.csv`: Tracciamento di ogni operazione critica.

## 📋 Requisiti

- .NET 9.0 Runtime
- Windows 10/11 (per le interfacce WPF)
- Una cartella di rete condivisa (LAN o Cloud Drive sincronizzato)

## 🔧 Installazione e Configurazione

1. **Setup Cartella**: Crea una cartella accessibile a tutti i PC.
2. **Configurazione Server**: Avvia `TimeClock.Server`, seleziona la cartella e aggiungi i dipendenti.
3. **Distribuzione Client**: Avvia `TimeClock.Client` sui terminali e punta alla stessa cartella condivisa.

## 🧪 Test e Validazione

La logica di calcolo è validata tramite test unitari e script di regressione che coprono scenari critici:
- Giornate spezzate.
- Turni notturni (es. 22:00 - 06:00).
- Gestione festività nazionali e personalizzate.

## 🔐 Sistema Licenze Client

`TimeClock.Client` ora richiede una licenza firmata digitalmente (RSA) e vincolata alla macchina.

### Come funziona
- Il client mostra il `Machine ID` nella finestra di attivazione.
- Una licenza valida contiene: cliente, prodotto, scadenza, machine id.
- Il token licenza e' firmato con chiave privata e verificato dal client con chiave pubblica.
- La licenza attivata viene salvata in locale cifrata con DPAPI (`CurrentUser`).

### Setup rapido
1. Genera coppia chiavi:
   `dotnet run --project tools/TimeClock.LicensingTool -- keygen --out-dir .\\license_keys`
2. Copia `license_public_key.pem` accanto all'eseguibile di `TimeClock.Client` (o imposta `LicensePublicKeyPem` nelle Settings utente).
3. Avvia il client e copia il `Machine ID` mostrato.
4. Emetti token:
   `dotnet run --project tools/TimeClock.LicensingTool -- issue --private-key .\\license_keys\\license_private_key.pem --machine-id <MACHINE_ID> --customer "<NOME AZIENDA>" --days 365`
5. Incolla il token nella finestra "Attivazione Licenza".
