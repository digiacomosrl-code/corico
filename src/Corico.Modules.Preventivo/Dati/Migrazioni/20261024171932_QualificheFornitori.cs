using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class QualificheFornitori : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRV_ComunicazioniQualifica",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QualificaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Anno = table.Column<int>(type: "INTEGER", nullable: false),
                    Progressivo = table.Column<int>(type: "INTEGER", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    DataEmissione = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Oggetto = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Prescrizioni = table.Column<string>(type: "TEXT", nullable: true),
                    AllegaAttestato = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllegaElencoDocumenti = table.Column<bool>(type: "INTEGER", nullable: false),
                    Stato = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GenerataIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GenerataDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovataIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovataDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    InviataIl = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Modalita = table.Column<int>(type: "INTEGER", nullable: true),
                    DestinatariEffettivi = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RiferimentoInvio = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    InvioRegistratoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RicevutaIl = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RiferimentoRicevuta = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RicevutaDocumentoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AnnullataIl = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    MotivoAnnullamento = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_ComunicazioniQualifica", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_Qualifiche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FornitoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fornitore = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OggettoPrestazioni = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RiferimentoFascicolo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Prescrizioni = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Stato = table.Column<int>(type: "INTEGER", nullable: false),
                    DataConclusione = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DirettoreCantiereId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DirettoreCantiere = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ApprovataDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApprovataIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Autorizzazione = table.Column<int>(type: "INTEGER", nullable: false),
                    RiferimentoAutorizzazione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DataAutorizzazione = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Qualifiche", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_AllegatiComunicazione",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComunicazioneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_AllegatiComunicazione", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_AllegatiComunicazione_PRV_ComunicazioniQualifica_ComunicazioneId",
                        column: x => x.ComunicazioneId,
                        principalTable: "PRV_ComunicazioniQualifica",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_DestinatariComunicazione",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComunicazioneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ruolo = table.Column<int>(type: "INTEGER", nullable: false),
                    AttoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Selezionato = table.Column<bool>(type: "INTEGER", nullable: false),
                    Titolo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Nominativo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Ente = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Indirizzo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Pec = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_DestinatariComunicazione", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_DestinatariComunicazione_PRV_ComunicazioniQualifica_ComunicazioneId",
                        column: x => x.ComunicazioneId,
                        principalTable: "PRV_ComunicazioniQualifica",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_DocumentiQualifica",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QualificaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Verificato = table.Column<bool>(type: "INTEGER", nullable: false),
                    Scadenza = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DocumentoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Riservato = table.Column<bool>(type: "INTEGER", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_DocumentiQualifica", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_DocumentiQualifica_PRV_Qualifiche_QualificaId",
                        column: x => x.QualificaId,
                        principalTable: "PRV_Qualifiche",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_AllegatiComunicazione_ComunicazioneId_Ordine",
                table: "PRV_AllegatiComunicazione",
                columns: new[] { "ComunicazioneId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_ComunicazioniQualifica_Anno_Progressivo",
                table: "PRV_ComunicazioniQualifica",
                columns: new[] { "Anno", "Progressivo" },
                unique: true,
                filter: "\"Progressivo\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_ComunicazioniQualifica_CommessaId",
                table: "PRV_ComunicazioniQualifica",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_ComunicazioniQualifica_QualificaId",
                table: "PRV_ComunicazioniQualifica",
                column: "QualificaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_DestinatariComunicazione_ComunicazioneId",
                table: "PRV_DestinatariComunicazione",
                column: "ComunicazioneId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_DocumentiQualifica_QualificaId_Ordine",
                table: "PRV_DocumentiQualifica",
                columns: new[] { "QualificaId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Qualifiche_CommessaId",
                table: "PRV_Qualifiche",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Qualifiche_FornitoreId",
                table: "PRV_Qualifiche",
                column: "FornitoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_AllegatiComunicazione");

            migrationBuilder.DropTable(
                name: "PRV_DestinatariComunicazione");

            migrationBuilder.DropTable(
                name: "PRV_DocumentiQualifica");

            migrationBuilder.DropTable(
                name: "PRV_ComunicazioniQualifica");

            migrationBuilder.DropTable(
                name: "PRV_Qualifiche");
        }
    }
}
