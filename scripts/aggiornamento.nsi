; File unico di aggiornamento di Corico: Corico-<versione>-aggiorna.exe.
; Con un doppio clic estrae il pacchetto in una cartella temporanea ed esegue la procedura guidata
; di sempre (aggiorna.cmd -> aggiorna.ps1: controllo dei test, copia di sicurezza verificata,
; installazione senza domanda di conferma, controlli, ritorno automatico alla versione precedente in caso di errore).
; Se Corico non è installato sul PC propone l'installazione (installa.cmd).
; Alla chiusura la cartella temporanea viene eliminata.
;
; Si crea con scripts/pubblica.sh oppure scripts\pubblica.ps1; a mano:
;   makensis -DVERSIONE=1.2.3 -DORIGINE=publish -DUSCITA=Corico-1.2.3-aggiorna.exe scripts/aggiornamento.nsi
; ORIGINE è la cartella che contiene Corico\ (con rilascio.json), aggiorna.* e installa.*.

Unicode true
Target amd64-unicode

!ifndef VERSIONE | ORIGINE | USCITA
  !error "Indicare -DVERSIONE=x.y.z -DORIGINE=<cartella del pacchetto> -DUSCITA=<file .exe>"
!endif

!include MUI2.nsh
!include LogicLib.nsh

Name "Corico ${VERSIONE}"
Caption "Aggiornamento di Corico ${VERSIONE}"
OutFile "${USCITA}"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
; Controllo di integrità del file: un download incompleto o danneggiato viene rifiutato prima di toccare il PC.
CRCCheck force
BrandingText "Corico ${VERSIONE}"
ShowInstDetails show
AutoCloseWindow true

VIProductVersion "${VERSIONE}.0"
VIFileVersion "${VERSIONE}.0"
VIAddVersionKey /LANG=1040 "ProductName" "Corico"
VIAddVersionKey /LANG=1040 "ProductVersion" "${VERSIONE}"
VIAddVersionKey /LANG=1040 "FileVersion" "${VERSIONE}"
VIAddVersionKey /LANG=1040 "FileDescription" "Aggiornamento di Corico ${VERSIONE}"
VIAddVersionKey /LANG=1040 "CompanyName" "Corico"
VIAddVersionKey /LANG=1040 "LegalCopyright" "Corico"

!define MUI_INSTFILESPAGE_FINISHHEADER_TEXT "Procedura conclusa"
!define MUI_INSTFILESPAGE_FINISHHEADER_SUBTEXT "Il risultato è nella finestra della procedura guidata."
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Italian"

Function .onInit
  ; Un solo aggiornamento alla volta.
  System::Call 'kernel32::CreateMutex(p 0, i 0, t "CoricoAggiornamento") p .r0 ?e'
  Pop $1
  ${If} $1 == 183
    MessageBox MB_OK|MB_ICONEXCLAMATION "Un aggiornamento di Corico è già in corso."
    Abort
  ${EndIf}
FunctionEnd

Section "Corico"
  InitPluginsDir
  DetailPrint "Preparazione dei file di Corico ${VERSIONE}..."
  SetOutPath "$PLUGINSDIR\pacchetto"
  File /r "${ORIGINE}\*"

  ${If} ${FileExists} "$LOCALAPPDATA\Programs\Corico\appsettings.json"
    ; Il doppio clic sul file vale come conferma: nessuna domanda durante la procedura.
    StrCpy $0 "aggiorna.cmd"
    StrCpy $2 " -SenzaConferma"
  ${Else}
    MessageBox MB_YESNO|MB_ICONQUESTION "Corico non risulta installato su questo PC.$\r$\n$\r$\nVuoi installare Corico ${VERSIONE}?" IDYES +3
      SetOutPath "$TEMP"
      Quit
    StrCpy $0 "installa.cmd"
    StrCpy $2 ""
  ${EndIf}

  DetailPrint "Procedura guidata in corso nella finestra nera: segui le indicazioni."
  HideWindow
  ExecWait '"$SYSDIR\cmd.exe" /c ""$PLUGINSDIR\pacchetto\$0"$2"' $1
  ; Uscita dalla cartella temporanea, che NSIS elimina alla chiusura.
  SetOutPath "$TEMP"
  SetErrorLevel $1
SectionEnd
