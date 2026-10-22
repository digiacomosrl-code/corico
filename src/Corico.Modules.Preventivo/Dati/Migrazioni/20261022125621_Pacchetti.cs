using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Pacchetti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRV_Pacchetti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    RiferimentoOda = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StatoCosto = table.Column<int>(type: "INTEGER", nullable: false),
                    OffertaSceltaId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_PRV_Pacchetti", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_NotePacchetti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Iniziali = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Testo = table.Column<string>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_NotePacchetti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_NotePacchetti_PRV_Pacchetti_PacchettoId",
                        column: x => x.PacchettoId,
                        principalTable: "PRV_Pacchetti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_Offerte",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FornitoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NomeFornitore = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", nullable: true),
                    Revisione = table.Column<string>(type: "TEXT", nullable: true),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ValiditaGiorni = table.Column<int>(type: "INTEGER", nullable: true),
                    Scadenza = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Pagamento = table.Column<string>(type: "TEXT", nullable: true),
                    Consegna = table.Column<string>(type: "TEXT", nullable: true),
                    Garanzia = table.Column<string>(type: "TEXT", nullable: true),
                    DocumentoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Esclusioni = table.Column<string>(type: "TEXT", nullable: true),
                    TotaleDichiarato = table.Column<decimal>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Origine = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Offerte", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_Offerte_PRV_Pacchetti_PacchettoId",
                        column: x => x.PacchettoId,
                        principalTable: "PRV_Pacchetti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_PacchettiVoci",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoceComputoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_PacchettiVoci", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_PacchettiVoci_PRV_Pacchetti_PacchettoId",
                        column: x => x.PacchettoId,
                        principalTable: "PRV_Pacchetti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PRV_PacchettiVoci_PRV_VociComputo_VoceComputoId",
                        column: x => x.VoceComputoId,
                        principalTable: "PRV_VociComputo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_CostiACorpo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffertaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: false),
                    Importo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Incluso = table.Column<bool>(type: "INTEGER", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_CostiACorpo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_CostiACorpo_PRV_Offerte_OffertaId",
                        column: x => x.OffertaId,
                        principalTable: "PRV_Offerte",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_OfferteVoci",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffertaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoceComputoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantitaPianificazione = table.Column<decimal>(type: "TEXT", nullable: true),
                    NotaQuantita = table.Column<string>(type: "TEXT", nullable: true),
                    Fornitura = table.Column<decimal>(type: "TEXT", nullable: false),
                    FornituraEPosa = table.Column<decimal>(type: "TEXT", nullable: false),
                    ManodoperaENoli = table.Column<decimal>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_OfferteVoci", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_OfferteVoci_PRV_Offerte_OffertaId",
                        column: x => x.OffertaId,
                        principalTable: "PRV_Offerte",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheOfferta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffertaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    CodiceArticolo = table.Column<string>(type: "TEXT", nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: false),
                    Posizione = table.Column<string>(type: "TEXT", nullable: true),
                    Unita = table.Column<string>(type: "TEXT", nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: false),
                    PrezzoListino = table.Column<decimal>(type: "TEXT", nullable: false),
                    ScontoPercentuale = table.Column<decimal>(type: "TEXT", nullable: false),
                    VoceComputoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheOfferta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheOfferta_PRV_Offerte_OffertaId",
                        column: x => x.OffertaId,
                        principalTable: "PRV_Offerte",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_CostiACorpo_OffertaId",
                table: "PRV_CostiACorpo",
                column: "OffertaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_NotePacchetti_PacchettoId",
                table: "PRV_NotePacchetti",
                column: "PacchettoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Offerte_PacchettoId",
                table: "PRV_Offerte",
                column: "PacchettoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_OfferteVoci_OffertaId_VoceComputoId",
                table: "PRV_OfferteVoci",
                columns: new[] { "OffertaId", "VoceComputoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Pacchetti_CommessaId_Ordine",
                table: "PRV_Pacchetti",
                columns: new[] { "CommessaId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_PacchettiVoci_PacchettoId",
                table: "PRV_PacchettiVoci",
                column: "PacchettoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_PacchettiVoci_VoceComputoId",
                table: "PRV_PacchettiVoci",
                column: "VoceComputoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheOfferta_OffertaId_Ordine",
                table: "PRV_RigheOfferta",
                columns: new[] { "OffertaId", "Ordine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_CostiACorpo");

            migrationBuilder.DropTable(
                name: "PRV_NotePacchetti");

            migrationBuilder.DropTable(
                name: "PRV_OfferteVoci");

            migrationBuilder.DropTable(
                name: "PRV_PacchettiVoci");

            migrationBuilder.DropTable(
                name: "PRV_RigheOfferta");

            migrationBuilder.DropTable(
                name: "PRV_Offerte");

            migrationBuilder.DropTable(
                name: "PRV_Pacchetti");
        }
    }
}
