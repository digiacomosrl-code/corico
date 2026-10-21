# Architettura di Corico

Riferimento: prompt strategico v2.1, sezioni 3, 4, 6, 9, 11, 18.

## Monolite modulare

- Un'unica applicazione e un unico database SQLite (`Dati\corico.db`).
- Il **Core** non conosce i moduli. Ogni modulo implementa `ICoricoModulo` e viene registrato all'avvio.
- Ogni modulo ha il proprio `DbContext`, le proprie tabelle con prefisso (es. `PRV_`) e la propria tabella
  di storico delle migrazioni (`__Migrazioni_<CODICE>`). Il Core usa `__Migrazioni_CORE`.
- I DbContext dei moduli derivano da `CoricoDbContextBase`, che gestisce versione, dati di creazione/modifica
  e storico delle modifiche; la tabella `CORE_Storico` è del Core ed è esclusa dalle migrazioni dei moduli.
- Le pagine non contengono logica di business: usano i servizi del Core o del modulo.

## Regole trasversali

| Regola | Implementazione |
|---|---|
| Identificativi indipendenti dal database | `Guid` generato dall'applicazione |
| Controllo ottimistico (due schede aperte) | campo `Versione` incrementato a ogni salvataggio; conflitto → messaggio e invito a ricaricare |
| Storico delle modifiche | `CORE_Storico`: utente, data/ora, operazione, campi prima/dopo, nota |
| Importi | sempre `decimal`; arrotondamento a 2 decimali solo in visualizzazione (`Importi.Arrotonda`, metà per eccesso come Excel) |
| Percentuali con denominatore zero | `null`, mostrato come "n.d." |
| Codice commessa | `AAAA-NNN`, progressivo per anno; il registro `Config\codici-commessa-assegnati.json` impedisce il riuso anche dopo un ripristino |
| Eliminazione | solo archiviazione reversibile nelle funzioni ordinarie |

## Backup

| Quando | Cosa |
|---|---|
| Primo avvio della giornata, ogni 2 ore di utilizzo effettivo, chiusura | backup del database con API nativa SQLite, verifica `integrity_check`, copia incrementale dei documenti |
| Su richiesta (Amministrazione) | backup manuale; backup su disco esterno registrato con verifica delle impronte |
| Prima di un ripristino | backup automatico dello stato attuale |

Conservazione locale: tutto nelle ultime 48 ore, uno al giorno per 30 giorni, uno al mese per 12 mesi.
Avviso in evidenza se l'ultimo backup esterno ha più di 7 giorni o se l'ultimo backup automatico è fallito.

## Sicurezza

- Kestrel ascolta solo su `127.0.0.1`: l'applicazione non è raggiungibile da altri dispositivi.
- Nessuna credenziale nel codice o nei file di configurazione.
- Cifratura: BitLocker sul disco dati e BitLocker To Go sui dischi di backup esterni (configurazione di Windows).
