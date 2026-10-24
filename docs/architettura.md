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
  archiviate senza testo e la lettura è segnata come parziale. Il pacchetto Windows include Tesseract 5.4 (build
  UB-Mannheim, solo `tesseract.exe` e le DLL necessarie) con `tessdata\ita.traineddata`: `scripts\pubblica.ps1` lo
  copia da `strumenti\ocr\`, cartella esclusa dal repository.
- Ricerca nel contenuto: tabella virtuale FTS5 `DOC_Ricerca` (maiuscole e accenti ignorati), usata solo da
  `IndiceRicerca`, l'unico punto con SQL specifico di SQLite; si ricostruisce dal testo (`DOC_Pagine`) se manca.
- Tipo di documento proposto con regole sul testo (`Classificatore`), applicato solo dopo conferma.
- Duplicati simili con firma MinHash su sequenze di 5 parole (`Somiglianza`), soglia 80%.
- Gli allegati delle mail diventano documenti del Core nella stessa commessa (`DOC_AllegatiMail`).
- Nessun servizio esterno: l'AI esterna arriva con il rilascio 3B.

## Accesso all'AI esterna (Core/Ai)

- Unico punto verso il fornitore AI: `IServizioAi` (`Corico.Core.Ai`). I moduli chiamano `PreparaAsync` (nessuna chiamata
  esterna: testo da inviare, modello, costo massimo stimato, motivo se non consentito), mostrano l'anteprima all'utente e,
  dopo la conferma, `InviaAsync`. Con uno schema JSON la risposta è vincolata allo schema (output strutturato).
- Fornitore: Anthropic (`ClienteAnthropic`, SDK ufficiale, streaming, fallback lato server se il modello declina);
  sostituibile implementando `IClienteAi`. Modello predefinito `claude-opus-5-5`, scelto dall'amministratore.
- Controlli in `ServizioAi`: interruttore generale, chiave presente, utente non di consultazione, tetto di spesa mensile
  in euro (il costo massimo delle richieste in corso è impegnato sul tetto). Ogni invio, anche non riuscito, va in
  `CORE_RichiesteAi` con utente, funzione, commessa, testo inviato, token e costo.
- Chiave d'accesso cifrata con DPAPI (ambito macchina) in `Config\ai-chiave.bin`, mai nel database né nel codice.
- Configurazione e registro nella pagina «Intelligenza artificiale» (solo amministratore).

## Attori della commessa (Core, 1.2.5)

- Archivio unico `CORE_Attori` (sezione «Attori» del menu): nominativo, titolo, ente o società, indirizzo, email, PEC, telefono.
- Figure della commessa in `CORE_IncarichiCommessa`: commessa, attore, `RuoloCommessa` (RUP, DL, CSE, direttore di cantiere,
  capocantiere; valori salvati, si aggiungono solo in coda), date «Dal»/«Al» e note. Più soggetti per figura; la sostituzione
  in corso d'opera chiude l'incarico il giorno prima del subentro e ne apre uno nuovo, quindi lo storico resta completo.
- Per comunicazioni e lettere usare `AttoriService.FigureInCaricaAsync(commessa, data, ruolo)`: restituisce le figure in carica
  alla data con tutti i recapiti dell'attore.

## Qualificazione dei fornitori e comunicazione a DL, RUP e CSE (modulo Preventivo, 1.2.6)

- Scheda «Qualifiche fornitori» della commessa (`PRV_Qualifiche`, `PRV_DocumentiQualifica`): fornitore dall'anagrafica, tipologia
  (fornitore, impresa esecutrice, subappaltatore, sub-affidatario), oggetto delle prestazioni, documenti verificati con file
  dell'archivio e spunta «riservato», prescrizioni. L'esito (positivo o negativo) si registra con data e Direttore di cantiere
  (dagli Attori o scritto a mano); dopo l'esito cambia solo lo stato dell'autorizzazione collegata, gestita a parte.
- L'esito positivo genera la bozza della comunicazione (`PRV_ComunicazioniQualifica`, destinatari e allegati) con DL, RUP e CSE
  in carica; il CSE è proposto per chi opera in cantiere. Stati: bozza, PDF definitivo (numero `CQ AAAA/NNN`, archiviato nei
  documenti della commessa), approvata per l'invio (amministratore o operatore abilitato agli ordini), inviata (data, modalità,
  destinatari effettivi, protocollo), ricevuta acquisita; annullabile con motivo. La generazione del PDF non vale come invio.
- Allegati: attestato ed elenco dei documenti verificati generati nel PDF; documenti dell'archivio scelti a mano, mai i riservati.
- Fascicolo del fornitore: pagina `/fornitori/{id}/fascicolo` con qualificazioni, documenti e comunicazioni di tutte le commesse.
- Sostituisce, in questo processo, la comunicazione alla Prefettura, che non è prevista.

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
