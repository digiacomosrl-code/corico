using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Core.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class RichiesteAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CORE_RichiesteAi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DataOra = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Utente = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Funzione = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Modello = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Contenuto = table.Column<string>(type: "TEXT", nullable: false),
                    CaratteriInviati = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenIngresso = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenUscita = table.Column<int>(type: "INTEGER", nullable: false),
                    Costo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Esito = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Messaggio = table.Column<string>(type: "TEXT", nullable: true),
                    DurataSecondi = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_RichiesteAi", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CORE_RichiesteAi_CommessaId",
                table: "CORE_RichiesteAi",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_RichiesteAi_DataOra",
                table: "CORE_RichiesteAi",
                column: "DataOra");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CORE_RichiesteAi");
        }
    }
}
