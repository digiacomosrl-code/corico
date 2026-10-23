using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Wbs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Wbs",
                table: "PRV_RigheOrdine",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PRV_Wbs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Wbs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Wbs_CommessaId_Codice",
                table: "PRV_Wbs",
                columns: new[] { "CommessaId", "Codice" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_Wbs");

            migrationBuilder.DropColumn(
                name: "Wbs",
                table: "PRV_RigheOrdine");
        }
    }
}
