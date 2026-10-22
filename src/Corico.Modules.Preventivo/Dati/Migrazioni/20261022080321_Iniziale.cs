using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Modules.Preventivo.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class Iniziale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRV_Preventivi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RibassoPercentuale = table.Column<decimal>(type: "TEXT", precision: 9, scale: 6, nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Preventivi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRV_Computi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreventivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Origine = table.Column<int>(type: "INTEGER", nullable: false),
                    Attivo = table.Column<bool>(type: "INTEGER", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NomeFile = table.Column<string>(type: "TEXT", nullable: true),
                    TotaleImportato = table.Column<decimal>(type: "TEXT", nullable: true),
                    TotaleDichiarato = table.Column<decimal>(type: "TEXT", nullable: true),
                    Oggetto = table.Column<string>(type: "TEXT", nullable: true),
                    Comune = table.Column<string>(type: "TEXT", nullable: true),
                    Committente = table.Column<string>(type: "TEXT", nullable: true),
                    Impresa = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Computi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_Computi_PRV_Preventivi_PreventivoId",
                        column: x => x.PreventivoId,
                        principalTable: "PRV_Preventivi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PRV_Categorie",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComputoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Livello = table.Column<int>(type: "INTEGER", nullable: false),
                    IdOriginale = table.Column<string>(type: "TEXT", nullable: false),
                    Codice = table.Column<string>(type: "TEXT", nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_Categorie", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_Categorie_PRV_Computi_ComputoId",
                        column: x => x.ComputoId,
                        principalTable: "PRV_Computi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_ElencoPrezzi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComputoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdOriginale = table.Column<string>(type: "TEXT", nullable: true),
                    Tariffa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Articolo = table.Column<string>(type: "TEXT", nullable: true),
                    DescrizioneRidotta = table.Column<string>(type: "TEXT", nullable: true),
                    DescrizioneBreve = table.Column<string>(type: "TEXT", nullable: true),
                    DescrizioneEstesa = table.Column<string>(type: "TEXT", nullable: true),
                    UnitaOriginale = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Prezzo = table.Column<decimal>(type: "TEXT", nullable: false),
                    IncidenzaSicurezza = table.Column<decimal>(type: "TEXT", nullable: true),
                    IncidenzaManodopera = table.Column<decimal>(type: "TEXT", nullable: true),
                    IncidenzaMateriali = table.Column<decimal>(type: "TEXT", nullable: true),
                    IncidenzaAttrezzature = table.Column<decimal>(type: "TEXT", nullable: true),
                    SuperCapitoloId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HaAnalisi = table.Column<bool>(type: "INTEGER", nullable: false),
                    SpeseGeneraliAnalisi = table.Column<decimal>(type: "TEXT", nullable: true),
                    UtileAnalisi = table.Column<decimal>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_ElencoPrezzi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_ElencoPrezzi_PRV_Computi_ComputoId",
                        column: x => x.ComputoId,
                        principalTable: "PRV_Computi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheAnalisi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoceElencoPrezziId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    ElencoPrezziRiferitoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: true),
                    Unita = table.Column<string>(type: "TEXT", nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: false),
                    Prezzo = table.Column<decimal>(type: "TEXT", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheAnalisi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheAnalisi_PRV_ElencoPrezzi_VoceElencoPrezziId",
                        column: x => x.VoceElencoPrezziId,
                        principalTable: "PRV_ElencoPrezzi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PRV_VociComputo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComputoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    IdOriginale = table.Column<string>(type: "TEXT", nullable: true),
                    VoceElencoPrezziId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tariffa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: false),
                    Unita = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: false),
                    Prezzo = table.Column<decimal>(type: "TEXT", nullable: false),
                    PercentualeNonSoggetta = table.Column<decimal>(type: "TEXT", nullable: false),
                    SuperCategoriaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoriaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubCategoriaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CodiceWbs = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Archiviata = table.Column<bool>(type: "INTEGER", nullable: false),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_VociComputo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_VociComputo_PRV_Computi_ComputoId",
                        column: x => x.ComputoId,
                        principalTable: "PRV_Computi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PRV_VociComputo_PRV_ElencoPrezzi_VoceElencoPrezziId",
                        column: x => x.VoceElencoPrezziId,
                        principalTable: "PRV_ElencoPrezzi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PRV_RigheMisura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoceComputoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommessaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: true),
                    PartiUguali = table.Column<decimal>(type: "TEXT", nullable: true),
                    Lunghezza = table.Column<decimal>(type: "TEXT", nullable: true),
                    Larghezza = table.Column<decimal>(type: "TEXT", nullable: true),
                    AltezzaPeso = table.Column<decimal>(type: "TEXT", nullable: true),
                    Quantita = table.Column<decimal>(type: "TEXT", nullable: true),
                    VoceRiferita = table.Column<string>(type: "TEXT", nullable: true),
                    Versione = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatoIl = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModificatoIl = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModificatoDa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRV_RigheMisura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRV_RigheMisura_PRV_VociComputo_VoceComputoId",
                        column: x => x.VoceComputoId,
                        principalTable: "PRV_VociComputo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Categorie_ComputoId_Livello_IdOriginale",
                table: "PRV_Categorie",
                columns: new[] { "ComputoId", "Livello", "IdOriginale" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Computi_CommessaId",
                table: "PRV_Computi",
                column: "CommessaId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Computi_PreventivoId",
                table: "PRV_Computi",
                column: "PreventivoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_ElencoPrezzi_ComputoId_Tariffa",
                table: "PRV_ElencoPrezzi",
                columns: new[] { "ComputoId", "Tariffa" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_Preventivi_CommessaId",
                table: "PRV_Preventivi",
                column: "CommessaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheAnalisi_VoceElencoPrezziId",
                table: "PRV_RigheAnalisi",
                column: "VoceElencoPrezziId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_RigheMisura_VoceComputoId",
                table: "PRV_RigheMisura",
                column: "VoceComputoId");

            migrationBuilder.CreateIndex(
                name: "IX_PRV_VociComputo_ComputoId_Ordine",
                table: "PRV_VociComputo",
                columns: new[] { "ComputoId", "Ordine" });

            migrationBuilder.CreateIndex(
                name: "IX_PRV_VociComputo_VoceElencoPrezziId",
                table: "PRV_VociComputo",
                column: "VoceElencoPrezziId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRV_Categorie");

            migrationBuilder.DropTable(
                name: "PRV_RigheAnalisi");

            migrationBuilder.DropTable(
                name: "PRV_RigheMisura");

            migrationBuilder.DropTable(
                name: "PRV_VociComputo");

            migrationBuilder.DropTable(
                name: "PRV_ElencoPrezzi");

            migrationBuilder.DropTable(
                name: "PRV_Computi");

            migrationBuilder.DropTable(
                name: "PRV_Preventivi");
        }
    }
}
