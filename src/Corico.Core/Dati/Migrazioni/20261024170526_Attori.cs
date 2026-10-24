using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Core.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Attori : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CORE_Attori",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nominativo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Titolo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Ente = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Indirizzo = table.Column<string>(type: "TEXT", nullable: true),
                    Cap = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Comune = table.Column<string>(type: "TEXT", nullable: true),
                    Provincia = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Pec = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_CORE_Attori", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CORE_IncarichiCommessa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ruolo = table.Column<int>(type: "INTEGER", nullable: false),
                    Dal = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Al = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CORE_IncarichiCommessa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CORE_IncarichiCommessa_CORE_Attori_AttoreId",
                        column: x => x.AttoreId,
                        principalTable: "CORE_Attori",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CORE_IncarichiCommessa_CORE_Commesse_CommessaId",
                        column: x => x.CommessaId,
                        principalTable: "CORE_Commesse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CORE_Attori_Nominativo",
                table: "CORE_Attori",
                column: "Nominativo");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_IncarichiCommessa_AttoreId",
                table: "CORE_IncarichiCommessa",
                column: "AttoreId");

            migrationBuilder.CreateIndex(
                name: "IX_CORE_IncarichiCommessa_CommessaId_Ruolo",
                table: "CORE_IncarichiCommessa",
                columns: new[] { "CommessaId", "Ruolo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CORE_IncarichiCommessa");

            migrationBuilder.DropTable(
                name: "CORE_Attori");
        }
    }
}
