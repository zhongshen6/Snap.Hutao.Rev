using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snap.Hutao.Migrations
{
    /// <inheritdoc />
    public partial class AddCultivateEntryRelatedEntryId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelatedEntryId",
                table: "cultivate_entries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries",
                column: "RelatedEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_cultivate_entries_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries",
                column: "RelatedEntryId",
                principalTable: "cultivate_entries",
                principalColumn: "InnerId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cultivate_entries_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries");

            migrationBuilder.DropIndex(
                name: "IX_cultivate_entries_RelatedEntryId",
                table: "cultivate_entries");

            migrationBuilder.DropColumn(
                name: "RelatedEntryId",
                table: "cultivate_entries");
        }
    }
}
