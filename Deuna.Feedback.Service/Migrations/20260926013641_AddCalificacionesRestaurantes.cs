using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Feedback.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddCalificacionesRestaurantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ciudad",
                table: "restaurantes_replicados",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ciudad",
                table: "restaurantes_replicados");
        }
    }
}
