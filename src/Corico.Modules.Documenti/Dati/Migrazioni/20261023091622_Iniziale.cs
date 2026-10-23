using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Documenti.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Iniziale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DOC_AllegatiMail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MailDocumentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllegatoDocumentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NomeFile = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOC_AllegatiMail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DOC_Testi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Esito = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Metodo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    NumeroPagine = table.Column<int>(type: "INTEGER", nullable: false),
                    PagineOcr = table.Column<int>(type: "INTEGER", nullable: false),
                    Messaggio = table.Column<string>(type: "TEXT", nullable: true),
                    TipoProposto = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MotivoTipo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TipoEsaminato = table.Column<bool>(type: "INTEGER", nullable: false),
                    Firma = table.Column<string>(type: "TEXT", nullable: true),
                    MailMittente = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MailOggetto = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MailData = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOC_Testi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DOC_Pagine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestoDocumentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    Testo = table.Column<string>(type: "TEXT", nullable: false),
                    DaOcr = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOC_Pagine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DOC_Pagine_DOC_Testi_TestoDocumentoId",
                        column: x => x.TestoDocumentoId,
                        principalTable: "DOC_Testi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DOC_AllegatiMail_AllegatoDocumentoId",
                table: "DOC_AllegatiMail",
                column: "AllegatoDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_DOC_AllegatiMail_MailDocumentoId",
                table: "DOC_AllegatiMail",
                column: "MailDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_DOC_Pagine_TestoDocumentoId_Numero",
                table: "DOC_Pagine",
                columns: new[] { "TestoDocumentoId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DOC_Testi_DocumentoId",
                table: "DOC_Testi",
                column: "DocumentoId",
                unique: true);

            // Indice di ricerca nel contenuto (FTS5 di SQLite), usato solo da IndiceRicerca.
            migrationBuilder.Sql("CREATE VIRTUAL TABLE IF NOT EXISTS DOC_Ricerca USING fts5(testo, documento UNINDEXED, pagina UNINDEXED, tokenize = 'unicode61 remove_diacritics 2');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS DOC_Ricerca;");

            migrationBuilder.DropTable(
                name: "DOC_AllegatiMail");

            migrationBuilder.DropTable(
                name: "DOC_Pagine");

            migrationBuilder.DropTable(
                name: "DOC_Testi");
        }
    }
}
