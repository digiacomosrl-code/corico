using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class CodiciRisorse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRV_CodiciRisorse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Categoria = table.Column<int>(type: "INTEGER", nullable: false),
                    Chiave = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_CodiciRisorse", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_CodiciRisorse_CommessaId_Categoria_Chiave",
                table: "PRV_CodiciRisorse",
                columns: new[] { "CommessaId", "Categoria", "Chiave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_CodiciRisorse_CommessaId_Codice",
                table: "PRV_CodiciRisorse",
                columns: new[] { "CommessaId", "Codice" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_CodiciRisorse");
        }
    }
}
