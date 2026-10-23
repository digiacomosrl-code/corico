using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Core.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Utenti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CORE_Utenti",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NomeUtente = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NomeCompleto = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HashPassword = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Ruolo = table.Column<int>(type: "INTEGER", nullable: false),
                    Attivo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CambioPasswordObbligatorio = table.Column<bool>(type: "INTEGER", nullable: false),
                    UltimoAccesso = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_Utenti", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Utenti_NomeUtente",
                table: "CORE_Utenti",
                column: "NomeUtente",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CORE_Utenti");
        }
    }
}
