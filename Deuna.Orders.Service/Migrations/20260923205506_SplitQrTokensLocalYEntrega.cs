using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Orders.Service.Migrations
{
    /// <inheritdoc />
    public partial class SplitQrTokensLocalYEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "QrCodigo",
                table: "pedidos",
                newName: "TokenQrLocal");

            migrationBuilder.AddColumn<string>(
                name: "TokenQrEntrega",
                table: "pedidos",
                type: "character varying(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "");

            // Las filas que ya existían conservan el token del local que tenían (el rename de
            // arriba lo preserva), pero quedan sin token de entrega. Se les genera uno: un
            // token vacío sería un token que cualquiera acierta mandando una cadena vacía.
            migrationBuilder.Sql("""
                UPDATE pedidos
                SET "TokenQrEntrega" = replace(gen_random_uuid()::text, '-', '')
                WHERE "TokenQrEntrega" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenQrEntrega",
                table: "pedidos");

            migrationBuilder.RenameColumn(
                name: "TokenQrLocal",
                table: "pedidos",
                newName: "QrCodigo");
        }
    }
}
