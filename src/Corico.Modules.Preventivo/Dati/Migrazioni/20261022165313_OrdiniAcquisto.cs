using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class OrdiniAcquisto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRV_OrdiniAcquisto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffertaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Anno = table.Column<int>(type: "INTEGER", nullable: false),
                    Progressivo = table.Column<int>(type: "INTEGER", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FornitoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Fornitore = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DatiFornitore = table.Column<string>(type: "TEXT", nullable: true),
                    Oggetto = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RiferimentoOfferta = table.Column<string>(type: "TEXT", nullable: true),
                    Pagamento = table.Column<string>(type: "TEXT", nullable: true),
                    Consegna = table.Column<string>(type: "TEXT", nullable: true),
                    LuogoConsegna = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Condizioni = table.Column<string>(type: "TEXT", nullable: true),
                    Totale = table.Column<decimal>(type: "TEXT", nullable: false),
                    Stato = table.Column<int>(type: "INTEGER", nullable: false),
                    MotivoAnnullamento = table.Column<string>(type: "TEXT", nullable: true),
                    AnnullatoIl = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_OrdiniAcquisto", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheOrdine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrdineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: false),
                    Prezzo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Importo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheOrdine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheOrdine_PRV_OrdiniAcquisto_OrdineId",
                        column: x => x.OrdineId,
                        principalTable: "PRV_OrdiniAcquisto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_OrdiniAcquisto_Anno_Progressivo",
                table: "PRV_OrdiniAcquisto",
                columns: new[] { "Anno", "Progressivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_OrdiniAcquisto_CommessaId",
                table: "PRV_OrdiniAcquisto",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_OrdiniAcquisto_PacchettoId",
                table: "PRV_OrdiniAcquisto",
                column: "PacchettoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheOrdine_OrdineId_Ordine",
                table: "PRV_RigheOrdine",
                columns: new[] { "OrdineId", "Ordine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_RigheOrdine");

            migrationBuilder.DropTable(
                name: "PRV_OrdiniAcquisto");
        }
    }
}
