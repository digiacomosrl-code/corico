using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class BudgetEOrdiniInBozza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PRV_RigheOrdine_PRV_OrdiniAcquisto_OrdineId",
                table: "PRV_RigheOrdine");

            migrationBuilder.DropIndex(
                name: "IX_PRV_OrdiniAcquisto_Anno_Progressivo",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.AddColumn<string>(
                name: "Avvisi",
                table: "PRV_Versioni",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EsitoDa",
                table: "PRV_Versioni",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EsitoIl",
                table: "PRV_Versioni",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoRifiuto",
                table: "PRV_Versioni",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NumeroBudget",
                table: "PRV_Versioni",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatoBudget",
                table: "PRV_Versioni",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Categoria",
                table: "PRV_RigheOrdine",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotaScostamento",
                table: "PRV_RigheOrdine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PacchettoId",
                table: "PRV_RigheOrdine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrezzoPianificato",
                table: "PRV_RigheOrdine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PacchettoId",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "OffertaId",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "ConfermaDocumentoId",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ConfermatoIl",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmessoDa",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FornitoreDaOfferta",
                table: "PRV_OrdiniAcquisto",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InviatoIl",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PRV_RigheOrdineOrigini",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RigaOrdineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrdineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Chiave = table.Column<string>(type: "TEXT", maxLength: 800, nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoceComputoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheOrdineOrigini", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheOrdineOrigini_PRV_RigheOrdine_RigaOrdineId",
                        column: x => x.RigaOrdineId,
                        principalTable: "PRV_RigheOrdine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_OrdiniAcquisto_Anno_Progressivo",
                table: "PRV_OrdiniAcquisto",
                columns: new[] { "Anno", "Progressivo" },
                unique: true,
                filter: "\"Progressivo\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheOrdineOrigini_CommessaId_Chiave",
                table: "PRV_RigheOrdineOrigini",
                columns: new[] { "CommessaId", "Chiave" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheOrdineOrigini_OrdineId",
                table: "PRV_RigheOrdineOrigini",
                column: "OrdineId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheOrdineOrigini_RigaOrdineId",
                table: "PRV_RigheOrdineOrigini",
                column: "RigaOrdineId");

            migrationBuilder.AddForeignKey(
                name: "FK_PRV_RigheOrdine_PRV_OrdiniAcquisto_OrdineId",
                table: "PRV_RigheOrdine",
                column: "OrdineId",
                principalTable: "PRV_OrdiniAcquisto",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PRV_RigheOrdine_PRV_OrdiniAcquisto_OrdineId",
                table: "PRV_RigheOrdine");

            migrationBuilder.DropTable(
                name: "PRV_RigheOrdineOrigini");

            migrationBuilder.DropIndex(
                name: "IX_PRV_OrdiniAcquisto_Anno_Progressivo",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.DropColumn(
                name: "Avvisi",
                table: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "EsitoDa",
                table: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "EsitoIl",
                table: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "MotivoRifiuto",
                table: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "NumeroBudget",
                table: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "StatoBudget",
                table: "PRV_Versioni");

            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "PRV_RigheOrdine");

            migrationBuilder.DropColumn(
                name: "NotaScostamento",
                table: "PRV_RigheOrdine");

            migrationBuilder.DropColumn(
                name: "PacchettoId",
                table: "PRV_RigheOrdine");

            migrationBuilder.DropColumn(
                name: "PrezzoPianificato",
                table: "PRV_RigheOrdine");

            migrationBuilder.DropColumn(
                name: "ConfermaDocumentoId",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.DropColumn(
                name: "ConfermatoIl",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.DropColumn(
                name: "EmessoDa",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.DropColumn(
                name: "FornitoreDaOfferta",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.DropColumn(
                name: "InviatoIl",
                table: "PRV_OrdiniAcquisto");

            migrationBuilder.AlterColumn<Guid>(
                name: "PacchettoId",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OffertaId",
                table: "PRV_OrdiniAcquisto",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_OrdiniAcquisto_Anno_Progressivo",
                table: "PRV_OrdiniAcquisto",
                columns: new[] { "Anno", "Progressivo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PRV_RigheOrdine_PRV_OrdiniAcquisto_OrdineId",
                table: "PRV_RigheOrdine",
                column: "OrdineId",
                principalTable: "PRV_OrdiniAcquisto",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
