using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Core.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Fornitori : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CORE_Fornitori",
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
                    Categorie = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_CORE_Fornitori", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Fornitori_PartitaIva",
                table: "CORE_Fornitori",
                column: "PartitaIva");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CORE_Fornitori");
        }
    }
}
