# Corico

Applicazione locale e modulare per la pianificazione economica (ricavi, costi e margini) delle commesse edili.
Gira su un unico PC Windows, si usa dal browser all'indirizzo `http://127.0.0.1:5080` e non richiede server,
servizi cloud o connessione Internet.

> Questo repository contiene **solo il codice sorgente**. Database, documenti aziendali, backup e credenziali
> non devono mai essere aggiunti (vedi `.gitignore`).

## Struttura

| Cartella | Contenuto |
|---|---|
| `src/Corico.Core` | Nucleo comune: commesse, clienti, archivio documenti, storico delle modifiche, backup e ripristino, regole di calcolo e arrotondamento. Tabelle `CORE_`. |
| `src/Corico.Modules.Preventivo` | Modulo 1 — Preventivo economico di commessa. Tabelle `PRV_`. |
| `src/Corico.Web` | Applicazione web locale (ASP.NET Core, Blazor) che ospita Core e moduli. |
| `tests/` | Test automatici del Core e dei moduli. |
| `scripts/` | Pubblicazione e installazione su Windows. |
| `docs/` | Documentazione tecnica. |

Tecnologie: .NET 10 (LTS), ASP.NET Core con Blazor (rendering interattivo lato server), Entity Framework Core con SQLite.

## Sviluppo

```bash
dotnet test                                  # test automatici
dotnet run --project src/Corico.Web          # avvio su http://127.0.0.1:5081 con dati in ./dati-sviluppo
```

In sviluppo i dati vengono creati nella cartella `dati-sviluppo/` (esclusa dal repository).

Nuova migrazione del Core:

```bash
dotnet ef migrations add NomeMigrazione -p src/Corico.Core -s src/Corico.Core -o Dati/Migrazioni
```

## Installazione sul PC

1. `powershell -ExecutionPolicy Bypass -File scripts\pubblica.ps1` — esegue i test e crea il pacchetto autonomo in `publish\Corico`.
2. `powershell -ExecutionPolicy Bypass -File scripts\installa.ps1 -Pacchetto publish\Corico -CartellaDati D:\CORICO`
   — installa il programma, imposta l'avvio automatico e crea il collegamento sul desktop.

La cartella dati (`D:\CORICO`) contiene `Dati\`, `Documenti\`, `Backup\`, `Log\` e `Config\`.
Reinstallare o aggiornare il programma non modifica mai la cartella dati.

## Aggiungere un modulo

1. Creare un progetto Razor Class Library `src/Corico.Modules.<Nome>` che referenzia `Corico.Core`.
2. Implementare `ICoricoModulo` (codice/prefisso tabelle, voci di menu, schede commessa, servizi, migrazioni).
3. Aggiungere il modulo all'elenco `moduliDisponibili` in `src/Corico.Web/Program.cs`.

Un modulo si disattiva da `appsettings.json` (`"Corico": { "Moduli": { "PRV": false } }`) senza effetti sugli altri.
