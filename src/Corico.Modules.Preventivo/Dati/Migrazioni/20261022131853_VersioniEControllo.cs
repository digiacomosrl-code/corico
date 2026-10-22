using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class VersioniEControllo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ObiettivoMarginePercentuale",
                table: "PRV_Preventivi",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PRV_IntegrazioniRicavo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Importo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_IntegrazioniRicavo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_IntegrazioniRicavo_PRV_Pacchetti_PacchettoId",
                        column: x => x.PacchettoId,
                        principalTable: "PRV_Pacchetti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_SpeseGenerali",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Modo = table.Column<int>(type: "INTEGER", nullable: false),
                    Importo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Percentuale = table.Column<decimal>(type: "TEXT", nullable: false),
                    Base = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_SpeseGenerali", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_Versioni",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    Nota = table.Column<string>(type: "TEXT", nullable: false),
                    Fotografia = table.Column<string>(type: "TEXT", nullable: false),
                    RibassoPercentuale = table.Column<decimal>(type: "TEXT", nullable: false),
                    Ricavo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Costo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Mdc = table.Column<decimal>(type: "TEXT", nullable: false),
                    SpeseGenerali = table.Column<decimal>(type: "TEXT", nullable: false),
                    Risultato = table.Column<decimal>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Versioni", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_IntegrazioniRicavo_CommessaId",
                table: "PRV_IntegrazioniRicavo",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_IntegrazioniRicavo_PacchettoId",
                table: "PRV_IntegrazioniRicavo",
                column: "PacchettoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_SpeseGenerali_CommessaId_Ordine",
                table: "PRV_SpeseGenerali",
                columns: new[] { "CommessaId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Versioni_CommessaId_Numero",
                table: "PRV_Versioni",
                columns: new[] { "CommessaId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_IntegrazioniRicavo");

            migrationBuilder.DropTable(
                name: "PRV_SpeseGenerali");

            migrationBuilder.DropTable(
                name: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "ObiettivoMarginePercentuale",
                table: "PRV_Preventivi");
        }
    }
}
