using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class RisorseEAnalisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRV_AnalisiAziendali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Stato = table.Column<int>(type: "INTEGER", nullable: false),
                    Fonte = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_AnalisiAziendali", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RisorseVoci",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffertaVoceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Categoria = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Fabbisogno = table.Column<decimal>(type: "TEXT", nullable: false),
                    SfridoPercentuale = table.Column<decimal>(type: "TEXT", nullable: false),
                    Prezzo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Fonte = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    AnalisiId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RisorseVoci", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RisorseVoci_PRV_OfferteVoci_OffertaVoceId",
                        column: x => x.OffertaVoceId,
                        principalTable: "PRV_OfferteVoci",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheAnalisiAziendali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnalisiId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Categoria = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Fabbisogno = table.Column<decimal>(type: "TEXT", nullable: false),
                    SfridoPercentuale = table.Column<decimal>(type: "TEXT", nullable: false),
                    Prezzo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Fonte = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheAnalisiAziendali", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheAnalisiAziendali_PRV_AnalisiAziendali_AnalisiId",
                        column: x => x.AnalisiId,
                        principalTable: "PRV_AnalisiAziendali",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_AnalisiAziendali_Codice",
                table: "PRV_AnalisiAziendali",
                column: "Codice");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheAnalisiAziendali_AnalisiId_Ordine",
                table: "PRV_RigheAnalisiAziendali",
                columns: new[] { "AnalisiId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RisorseVoci_CommessaId",
                table: "PRV_RisorseVoci",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RisorseVoci_OffertaVoceId_Ordine",
                table: "PRV_RisorseVoci",
                columns: new[] { "OffertaVoceId", "Ordine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_RigheAnalisiAziendali");

            migrationBuilder.DropTable(
                name: "PRV_RisorseVoci");

            migrationBuilder.DropTable(
                name: "PRV_AnalisiAziendali");
        }
    }
}
