using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Core.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Iniziale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CORE_Clienti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Denominazione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PartitaIva = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CodiceFiscale = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Indirizzo = table.Column<string>(type: "TEXT", nullable: true),
                    Comune = table.Column<string>(type: "TEXT", nullable: true),
                    Provincia = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Telefono = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Archiviato = table.Column<bool>(type: "INTEGER", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_Clienti", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CORE_ContatoriCodice",
                columns: table => new
                {
                    Anno = table.Column<int>(type: "INTEGER", nullable: false),
                    Ultimo = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_ContatoriCodice", x => x.Anno);
                });

            migrationBuilder.CreateTable(
                name: "CORE_Impostazioni",
                columns: table => new
                {
                    Chiave = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Valore = table.Column<string>(type: "TEXT", nullable: true),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_Impostazioni", x => x.Chiave);
                });

            migrationBuilder.CreateTable(
                name: "CORE_RegistroBackup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataOra = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", nullable: false),
                    Riuscito = table.Column<bool>(type: "INTEGER", nullable: false),
                    Percorso = table.Column<string>(type: "TEXT", nullable: true),
                    Dimensione = table.Column<long>(type: "INTEGER", nullable: true),
                    Impronta = table.Column<string>(type: "TEXT", nullable: true),
                    DocumentiCopiati = table.Column<int>(type: "INTEGER", nullable: true),
                    Messaggio = table.Column<string>(type: "TEXT", nullable: true),
                    Utente = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_RegistroBackup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CORE_Storico",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataOra = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Utente = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Operazione = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Entita = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntitaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ValoriPrecedenti = table.Column<string>(type: "TEXT", nullable: true),
                    ValoriSuccessivi = table.Column<string>(type: "TEXT", nullable: true),
                    Nota = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_Storico", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CORE_Commesse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Stato = table.Column<int>(type: "INTEGER", nullable: false),
                    Archiviata = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClienteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CodiceEsterno = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IndirizzoCantiere = table.Column<string>(type: "TEXT", nullable: true),
                    ComuneCantiere = table.Column<string>(type: "TEXT", nullable: true),
                    ProvinciaCantiere = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    Responsabile = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DataInizioPrevista = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DataFinePrevista = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ValoreContrattuale = table.Column<decimal>(type: "TEXT", nullable: true),
                    ContrattoDocumentoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContrattoNumero = table.Column<string>(type: "TEXT", nullable: true),
                    ContrattoData = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_Commesse", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CORE_Commesse_CORE_Clienti_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "CORE_Clienti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CORE_Documenti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NomeOriginale = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Estensione = table.Column<string>(type: "TEXT", nullable: false),
                    TipoContenuto = table.Column<string>(type: "TEXT", nullable: false),
                    PercorsoInterno = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Dimensione = table.Column<long>(type: "INTEGER", nullable: false),
                    Impronta = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: true),
                    DataDocumento = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: true),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Archiviato = table.Column<bool>(type: "INTEGER", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_Documenti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CORE_Documenti_CORE_Commesse_CommessaId",
                        column: x => x.CommessaId,
                        principalTable: "CORE_Commesse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Clienti_CodiceFiscale",
                table: "CORE_Clienti",
                column: "CodiceFiscale");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Clienti_PartitaIva",
                table: "CORE_Clienti",
                column: "PartitaIva");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Commesse_ClienteId",
                table: "CORE_Commesse",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Commesse_Codice",
                table: "CORE_Commesse",
                column: "Codice",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Commesse_ContrattoDocumentoId",
                table: "CORE_Commesse",
                column: "ContrattoDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Documenti_CommessaId",
                table: "CORE_Documenti",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Documenti_Impronta",
                table: "CORE_Documenti",
                column: "Impronta");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_RegistroBackup_DataOra",
                table: "CORE_RegistroBackup",
                column: "DataOra");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Storico_CommessaId",
                table: "CORE_Storico",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Storico_Entita_EntitaId",
                table: "CORE_Storico",
                columns: new[] { "Entita", "EntitaId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CORE_Commesse_CORE_Documenti_ContrattoDocumentoId",
                table: "CORE_Commesse",
                column: "ContrattoDocumentoId",
                principalTable: "CORE_Documenti",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CORE_Commesse_CORE_Clienti_ClienteId",
                table: "CORE_Commesse");

            migrationBuilder.DropForeignKey(
                name: "FK_CORE_Commesse_CORE_Documenti_ContrattoDocumentoId",
                table: "CORE_Commesse");

            migrationBuilder.DropTable(
                name: "CORE_ContatoriCodice");

            migrationBuilder.DropTable(
                name: "CORE_Impostazioni");

            migrationBuilder.DropTable(
                name: "CORE_RegistroBackup");

            migrationBuilder.DropTable(
                name: "CORE_Storico");

            migrationBuilder.DropTable(
                name: "CORE_Clienti");

            migrationBuilder.DropTable(
                name: "CORE_Documenti");

            migrationBuilder.DropTable(
                name: "CORE_Commesse");
        }
    }
}
