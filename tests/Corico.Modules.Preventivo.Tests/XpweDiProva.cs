using System.Text;

namespace Corico.Modules.Preventivo.Tests;

/// <summary>Computo XPWE minimo con le stesse particolarità del campione reale (carattere 0x03, voce senza elenco, sicurezza).</summary>
internal static class XpweDiProva
{
    public const string Testo = """
        <?xml version="1.0" encoding="utf-8"?>
        <PweDocumento>
          <PweDatiGenerali>
            <PweDGDatiGenerali>
              <Oggetto>Lavori di prova</Oggetto>
              <Comune>Caltagirone</Comune>
              <Provincia>CT</Provincia>
              <Committente>Comune di prova</Committente>
              <Impresa>Impresa di prova</Impresa>
            </PweDGDatiGenerali>
            <PweDGCapitoliCategorie>
              <PweDGSuperCategorie>
                <DGSuperCategorieItem ID="1"><DesSintetica>OPERE EDILI</DesSintetica></DGSuperCategorieItem>
                <DGSuperCategorieItem ID="2"><DesSintetica>SICUREZZA</DesSintetica></DGSuperCategorieItem>
              </PweDGSuperCategorie>
            </PweDGCapitoliCategorie>
          </PweDatiGenerali>
          <PweMisurazioni>
            <PweElencoPrezzi>
              <EPItem ID="10"><Tariffa>E.01</Tariffa><DesBreve>Intonaco civile</DesBreve><UnMisura>m2</UnMisura><Prezzo1>25.40</Prezzo1><IncSIC>0</IncSIC><IncMDO>45.5</IncMDO></EPItem>
              <EPItem ID="11"><Tariffa>S.01</Tariffa><DesBreve>Ponteggio metallico</DesBreve><UnMisura>cadauno</UnMisura><Prezzo1>1000</Prezzo1><IncSIC>100</IncSIC></EPItem>
            </PweElencoPrezzi>
            <PweVociComputo>
              <VCItem ID="1"><IDEP>10</IDEP><Quantita>120.5</Quantita><IDSpCat>1</IDSpCat>
                <PweVCMisure>
                  <RGItem><Descrizione>Parete nord</Descrizione><PartiUguali>2</PartiUguali><Lunghezza>10</Lunghezza><HPeso>3</HPeso><Quantita>60</Quantita></RGItem>
                  <RGItem><Descrizione>Parete sud</Descrizione><Quantita>60.5</Quantita></RGItem>
                </PweVCMisure>
              </VCItem>
              <VCItem ID="2"><IDEP>11</IDEP><Quantita>2</Quantita><IDSpCat>2</IDSpCat><ConfQuantita>\u0003</ConfQuantita></VCItem>
              <VCItem ID="3"><IDEP>0</IDEP><Quantita>5</Quantita><IDSpCat>1</IDSpCat>
                <PweVCMisure><RGItem><Descrizione>Opere extra da definire</Descrizione><Quantita>5</Quantita></RGItem></PweVCMisure>
              </VCItem>
            </PweVociComputo>
          </PweMisurazioni>
        </PweDocumento>
        """;

    public const decimal TotaleAtteso = 120.5m * 25.40m + 2m * 1000m;

    public static byte[] Contenuto => Encoding.UTF8.GetBytes(Testo.Replace("\\u0003", "\u0003"));

    public static MemoryStream Flusso() => new(Contenuto);
}
