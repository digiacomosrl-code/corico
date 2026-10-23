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

| Modulo | Progetto | Tabelle |
|---|---|---|
| Preventivo (Moduli 1 e 2) | `Corico.Modules.Preventivo` | `PRV_` |
| Documenti e AI (Modulo 3) | `Corico.Modules.Documenti` | `DOC_` |

## Modulo Documenti e AI (rilascio 3A)

- Lettura del contenuto dei documenti del Core in sottofondo (`CodaLettura`), uno alla volta: PDF (PdfPig),
  Word .docx e Excel (Open XML SDK), mail .eml (MimeKit) e .msg (MsgReader), immagini.
- Le pagine PDF senza testo e le immagini si leggono con l'OCR locale **Tesseract** (modello `ita`), avviato come
  programma separato: si cerca in `Corico:Ocr:Percorso`, poi nella cartella `ocr\` accanto a Corico, poi nel PATH.
  Le pagine si trasformano in immagine a 300 dpi con PDFium (PDFtoImage). Senza Tesseract le scansioni restano
  archiviate senza testo e la lettura è segnata come parziale.
- Ricerca nel contenuto: tabella virtuale FTS5 `DOC_Ricerca` (maiuscole e accenti ignorati), usata solo da
  `IndiceRicerca`, l'unico punto con SQL specifico di SQLite; si ricostruisce dal testo (`DOC_Pagine`) se manca.
- Tipo di documento proposto con regole sul testo (`Classificatore`), applicato solo dopo conferma.
- Duplicati simili con firma MinHash su sequenze di 5 parole (`Somiglianza`), soglia 80%.
- Gli allegati delle mail diventano documenti del Core nella stessa commessa (`DOC_AllegatiMail`).
- Nessun servizio esterno: l'AI esterna arriva con il rilascio 3B.

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
