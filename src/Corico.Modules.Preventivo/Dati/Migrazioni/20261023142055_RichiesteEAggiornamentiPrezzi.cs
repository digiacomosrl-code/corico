using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class RichiesteEAggiornamentiPrezzi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OffertaSceltaDa",
                table: "PRV_Pacchetti",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OffertaSceltaIl",
                table: "PRV_Pacchetti",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PRV_AggiornamentiPrezzi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Nota = table.Column<string>(type: "TEXT", nullable: true),
                    NumeroRighe = table.Column<int>(type: "INTEGER", nullable: false),
                    DifferenzaCosto = table.Column<decimal>(type: "TEXT", nullable: false),
                    MdcPrima = table.Column<decimal>(type: "TEXT", nullable: false),
                    MdcDopo = table.Column<decimal>(type: "TEXT", nullable: false),
                    AnnullatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnnullatoDa = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_AggiornamentiPrezzi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RichiesteOfferta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Anno = table.Column<int>(type: "INTEGER", nullable: false),
                    Progressivo = table.Column<int>(type: "INTEGER", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Oggetto = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Scadenza = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LuogoLavori = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RichiesteOfferta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_StimeEsclusioni",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffertaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Importo = table.Column<decimal>(type: "TEXT", nullable: false),
                    CostoACorpoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_StimeEsclusioni", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_StimeEsclusioni_PRV_Offerte_OffertaId",
                        column: x => x.OffertaId,
                        principalTable: "PRV_Offerte",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheAggiornamento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggiornamentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fonte = table.Column<int>(type: "INTEGER", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Elemento = table.Column<int>(type: "INTEGER", nullable: false),
                    ElementoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Campo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PrezzoPrima = table.Column<decimal>(type: "TEXT", nullable: false),
                    PrezzoDopo = table.Column<decimal>(type: "TEXT", nullable: false),
                    DifferenzaCosto = table.Column<decimal>(type: "TEXT", nullable: false),
                    Prima = table.Column<string>(type: "TEXT", nullable: false),
                    Dopo = table.Column<string>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheAggiornamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheAggiornamento_PRV_AggiornamentiPrezzi_AggiornamentoId",
                        column: x => x.AggiornamentoId,
                        principalTable: "PRV_AggiornamentiPrezzi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_DestinatariRichiesta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RichiestaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FornitoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Fornitore = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Stato = table.Column<int>(type: "INTEGER", nullable: false),
                    InviataIl = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RispostaIl = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OffertaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_DestinatariRichiesta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_DestinatariRichiesta_PRV_RichiesteOfferta_RichiestaId",
                        column: x => x.RichiestaId,
                        principalTable: "PRV_RichiesteOfferta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheRichiesta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RichiestaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    PacchettoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoceComputoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Chiavi = table.Column<string>(type: "TEXT", nullable: true),
                    Codice = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheRichiesta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheRichiesta_PRV_RichiesteOfferta_RichiestaId",
                        column: x => x.RichiestaId,
                        principalTable: "PRV_RichiesteOfferta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_AggiornamentiPrezzi_CommessaId",
                table: "PRV_AggiornamentiPrezzi",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_DestinatariRichiesta_RichiestaId",
                table: "PRV_DestinatariRichiesta",
                column: "RichiestaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RichiesteOfferta_Anno_Progressivo",
                table: "PRV_RichiesteOfferta",
                columns: new[] { "Anno", "Progressivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RichiesteOfferta_CommessaId",
                table: "PRV_RichiesteOfferta",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheAggiornamento_AggiornamentoId",
                table: "PRV_RigheAggiornamento",
                column: "AggiornamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheRichiesta_RichiestaId_Ordine",
                table: "PRV_RigheRichiesta",
                columns: new[] { "RichiestaId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_StimeEsclusioni_OffertaId_Ordine",
                table: "PRV_StimeEsclusioni",
                columns: new[] { "OffertaId", "Ordine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_DestinatariRichiesta");

            migrationBuilder.DropTable(
                name: "PRV_RigheAggiornamento");

            migrationBuilder.DropTable(
                name: "PRV_RigheRichiesta");

            migrationBuilder.DropTable(
                name: "PRV_StimeEsclusioni");

            migrationBuilder.DropTable(
                name: "PRV_AggiornamentiPrezzi");

            migrationBuilder.DropTable(
                name: "PRV_RichiesteOfferta");

            migrationBuilder.DropColumn(
                name: "OffertaSceltaDa",
                table: "PRV_Pacchetti");

            migrationBuilder.DropColumn(
                name: "OffertaSceltaIl",
                table: "PRV_Pacchetti");
        }
    }
}
