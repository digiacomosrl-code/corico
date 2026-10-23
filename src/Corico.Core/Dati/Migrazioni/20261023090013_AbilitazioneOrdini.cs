using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Corico.Core.Dati.Migrazioni
{
    /// <inheritdoc />
    public partial class AbilitazioneOrdini : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuoEmettereOrdini",
                table: "CORE_Utenti",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PuoEmettereOrdini",
                table: "CORE_Utenti");
        }
    }
}
