using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snap.Hutao.Migrations
{
    /// <inheritdoc />
    public partial class AddCultivateProjectAvatarPropertyBatchPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cultivate_entries_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries");

            migrationBuilder.AddColumn<string>(
                name: "AvatarPropertyBatchCultivatePreferencesJson",
                table: "cultivate_projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_cultivate_entries_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries",
                column: "RelatedEntryId",
                principalTable: "cultivate_entries",
                principalColumn: "InnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cultivate_entries_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries");

            migrationBuilder.DropColumn(
                name: "AvatarPropertyBatchCultivatePreferencesJson",
                table: "cultivate_projects");

            migrationBuilder.AddForeignKey(
                name: "FK_cultivate_entries_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries",
                column: "RelatedEntryId",
                principalTable: "cultivate_entries",
                principalColumn: "InnerId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
