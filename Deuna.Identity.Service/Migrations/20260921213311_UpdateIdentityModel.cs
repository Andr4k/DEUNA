using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Identity.Service.Migrations
{
    /// <inheritdoc />
    public partial class UpdateIdentityModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_perfiles_restaurante_ruc",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "Direccion",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "Ruc",
                table: "perfiles_restaurante");

            migrationBuilder.AlterColumn<string>(
                name: "RazonSocial",
                table: "perfiles_restaurante",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ciudad",
                table: "perfiles_restaurante",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DireccionSede",
                table: "perfiles_restaurante",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "Latitud",
                table: "perfiles_restaurante",
                type: "numeric(10,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitud",
                table: "perfiles_restaurante",
                type: "numeric(11,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Nit",
                table: "perfiles_restaurante",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "NumeroLicencia",
                table: "perfiles_repartidor",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CiudadOperacion",
                table: "perfiles_repartidor",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NombreCompleto",
                table: "perfiles_repartidor",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_restaurante_nit",
                table: "perfiles_restaurante",
                column: "Nit",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_perfiles_restaurante_nit",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "Ciudad",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "DireccionSede",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "Nit",
                table: "perfiles_restaurante");

            migrationBuilder.DropColumn(
                name: "CiudadOperacion",
                table: "perfiles_repartidor");

            migrationBuilder.DropColumn(
                name: "NombreCompleto",
                table: "perfiles_repartidor");

            migrationBuilder.AlterColumn<string>(
                name: "RazonSocial",
                table: "perfiles_restaurante",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "Direccion",
                table: "perfiles_restaurante",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ruc",
                table: "perfiles_restaurante",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NumeroLicencia",
                table: "perfiles_repartidor",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_restaurante_ruc",
                table: "perfiles_restaurante",
                column: "Ruc",
                unique: true,
                filter: "\"Ruc\" IS NOT NULL");
        }
    }
}
